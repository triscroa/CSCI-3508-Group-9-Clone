using Microsoft.AspNetCore.Mvc;
using static StudentExchangeBck.Register;

/* LogIn EndPoint:
    
   POST Post():
        Login POST: Uses password and email to login.
        Input: {'email', 'password', 'app_access'}
        Returns: If successfull: {'access_token', 'first_name'}

   DELETE Delete():
        Log Out
        On Success: Deletes all access tokens for given user.
        Input: {'app_access', 'access_token'}
        Returns: OK(), Status 200

   ========================================================================

   IncInvalidForUser_Email():
   IncInvalidForUser_Token():
   IncInvalidForUser():
        Checks for to many access attemps to a user. 
        Will block user for a timeout of too many invalid access attemps.

   GetUserLock():
        Gets the mutex lock for each user id

   CleanUp():
        Occasionally should be used to clean up resources
 */

namespace StudentExchangeBck
{
    [ApiController]
    [Route("[controller]")]
    public class LogIn : ControllerBase
    {
        /// <summary>
        /// Locks based on userid.
        /// To keep Atomic nature usually imbetween sql calls
        /// </summary>
        static readonly Dictionary<long, (object lck, DateTime dt)> _userLock
            = new Dictionary<long, (object, DateTime)>();

        /// <summary>
        /// Login POST: Uses password and email to login.
        /// If successfull: Returns {'access_token', 'first_name'}
        /// </summary>
        /// <param name="data">Input: {'email', 'password', 'app_access'}</param>
        /// <returns>If successfull: Returns {'access_token', 'first_name'}</returns>
        [HttpPost]
        public IActionResult Post(Info data)
        {
            // Validate Application Access
            var msg = AppAuthJson.Validate(data);
            // If no Auth: return no auth
            if (msg != null) return msg;

            string emailCrypt, passCrypt;
            try
            {
                // Email & Password: can't be blank or white space
                if (string.IsNullOrWhiteSpace(data.email))
                    throw new ArgumentException("There needs to be a email.");
                if (string.IsNullOrWhiteSpace(data.password))
                    throw new ArgumentException("There needs to be a password.");

                // Encrypt for sql
                emailCrypt = DeterministicEncryption.Encrypt(data.email.Trim().ToLower(), Env._emailSalt);
                passCrypt = DeterministicEncryption.Encrypt(data.password.Trim(), Env._passSalt);

                // Find results for Email & Password given
                var result = Sql.Read("SELECT [id],[first_name] FROM User WHERE [email]=@email AND [password]=@pass",
                                new Dictionary<string, object>
                                {
                                    {"@email", emailCrypt },
                                    {"@pass", passCrypt }
                                });

                // Block user for timeout if to many access attemps
                // If has result: Check if Blocked.
                // If not has result: incremement invalid access attemps & Check if it needs to block.
                msg = IncInvalidForUser_Email(result.Count == 0, this, emailCrypt);
                // If it needs to block user
                if (msg != null) return msg;

                // Invalid Email or Password
                if (result.Count == 0)
                    throw new ArgumentException("Invalid Email or Password.");

                // Get id & first_name
                var id = (long)result["id"][0];
                var first_name = result["first_name"][0];
                object token;

                // Mutex lock on user
                var ulock = GetUserLock(id);
                lock (ulock)
                {
                    // Try to find not expired access token
                    result = Sql.Read("SELECT [token] FROM Access_Token WHERE [user_id]=@id AND [expiry]>@now",
                                new Dictionary<string, object>
                                {
                                {"@id", id },
                                {"@now", Utilz.ToMtn().AddDays(1).ToString("s") },
                                });

                    // Found access token
                    if (result.Count != 0)
                        token = result["token"][0];
                    // Did not find access token
                    else
                    {
                        token = null!;
                        // There is a very small chance that it generates the same token. However,
                        // if it does, this will fix it.
                        for (int i = 0; i < 5; i++)
                        {
                            try
                            {
                                // Generate Access Token
                                token = AccessToken.Generate();
                                // Insert Access token into db
                                Sql.Write("INSERT INTO Access_Token ([user_id],[token],[expiry]) VALUES " +
                                    "(@user,@token,@expiry)", new Dictionary<string, object>
                                    {
                                    { "@user", id },
                                    { "@token", token },
                                    { "@expiry", Utilz.ToMtn(DateTime.UtcNow.Add(Env._tokenExpiry)).ToString("s") }
                                    }, 1);
                                break;  // Success Break
                            }
                            // Unsucessfull: token was not unique
                            catch { token = null!; }
                        }

                        if (token == null)
                            throw new Exception("After serveral attempts, the Access Token could not be generated.");
                    }
                }

                // Success
                return StatusCode(201, new
                {
                    Message = "Successfull Login.",
                    access_token = token,
                    first_name = first_name
                });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new
                {
                    Message = e.Message
                });
            }
            catch (Exception e)
            {
                return StatusCode(500, new
                {
                    Message = $"An internal server error occurred: {e}"
                });
            }
        }

        /// <summary>
        /// Log Out
        /// On Success: Deletes all access tokens for given user.
        /// </summary>
        /// <param name="data">Input: {'app_access', 'access_token'}</param>
        /// <returns>OK(), Status 200</returns>
        [HttpDelete]
        public IActionResult Delete(AppAuthJson data)
        {
            // Validate Application Access & Access Token
            var msg = AppAuthJson.ValidateWithToken(this, data, out var userId);
            // Exit if invalid: No need to show an error.
            if (msg != null) return Ok(new
            {
                Message = "You have been logged out."
            });

            try
            {
                // Delete All Access Tokens for user id
                Sql.Write("DELETE FROM Access_Token WHERE [user_id]=@userid",
                            new Dictionary<string, object> { { @"userid", userId } }, -2);

                // Success
                return Ok(new
                {
                    Message = "You have been logged out."
                });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new
                {
                    Message = e.Message
                });
            }
            catch (Exception e)
            {
                return StatusCode(500, new
                {
                    Message = $"An internal server error occurred: {e}"
                });
            }
        }

        /// <summary>
        /// Checks for to many access attemps to a user. 
        /// Will block user for a timeout of too many invalid access attemps.
        /// </summary>
        /// <param name="increment">true: increment invalid access attemps, false: just check if it needs to block.</param>
        /// <param name="this_">Controller Base</param>
        /// <param name="emailCrypt">Encrypted Email</param>
        /// <returns>If ok || email not found: null else NoAuth.</returns>
        public static IActionResult? IncInvalidForUser_Email(bool increment, ControllerBase this_, object emailCrypt)
        {
            // Try to find userid for email
            var result = Sql.Read("SELECT [id] FROM User WHERE [email]=@email",
                                    new Dictionary<string, object> { { "@email", emailCrypt } });
            // Could'nt Find Exit
            if (result.Count == 0) return null;

            // Block user for timeout if to many access attemps
            // If has increment=true: Check if Blocked.
            // If increment=false: incremement invalid access attemps & Check if it needs to block.
            return IncInvalidForUser(increment, this_, result["id"][0]);
        }
        /// <summary>
        /// Checks for to many access attemps to a user. 
        /// Will block user for a timeout of too many invalid access attemps.
        /// </summary>
        /// <param name="increment">true: increment invalid access attemps, false: just check if it needs to block.</param>
        /// <param name="this_">Controller Base</param>
        /// <param name="token">Access Token</param>
        /// <returns>If ok || access token not found: null else NoAuth.</returns>
        public static IActionResult? IncInvalidForUser_Token(bool increment, ControllerBase this_, object token)
        {
            // Try to find userid for access token
            var result = Sql.Read("SELECT [user_id] FROM Access_Token WHERE [token]=@token",
                                    new Dictionary<string, object> { { "@token", token } });
            // Could'nt Find Exit
            if (result.Count == 0) return null;

            // Block user for timeout if to many access attemps
            // If has increment=true: Check if Blocked.
            // If increment=false: incremement invalid access attemps & Check if it needs to block.
            return IncInvalidForUser(increment, this_, result["user_id"][0]);
        }
        /// <summary>
        /// Checks for to many access attemps to a user. 
        /// Will block user for a timeout of too many invalid access attemps.
        /// </summary>
        /// <param name="increment">true: increment invalid access attemps, false: just check if it needs to block.</param>
        /// <param name="this_">Controller Base</param>
        /// <param name="userid_">(long) user id</param>
        /// <returns>If ok: null else NoAuth.</returns>
        public static IActionResult? IncInvalidForUser(bool increment, ControllerBase this_, object userid_)
        {
            var userid = (long)userid_;

            // Mutex lock on user
            var ulock = GetUserLock(userid);
            lock (ulock)
            {
                // Try to find row for an lately invalid access attemps that have not expired.
                var result = Sql.Read("SELECT [id],[count],[expiry] FROM Blocked_Users " +
                                        "WHERE [user_id]=@userid AND [expiry]>@date",
                                        new Dictionary<string, object> 
                                        { 
                                            { "@userid", userid },
                                            { "@date", Utilz.ToMtn().ToString("s") }
                                        });

                // If no invalid access attemps lately
                if (result.Count == 0)
                {
                    // If you want to increment invalid access attemps
                    if (increment)
                    {
                        // Insert row of first invalid access attempt.
                        Sql.Write("INSERT INTO Blocked_Users ([user_id],[count],[expiry]) VALUES " +
                                    "(@userid,@count,@expiry)", new Dictionary<string, object>
                                    {
                                    {"@userid", userid },
                                    {"@count", 1 },
                                    {"@expiry", Utilz.ToMtn().Add(Env._blockedAccessTimeOut).ToString("s")}
                                    }, 1);
                    }

                    // EXIT
                    return null;
                }

                // Has invalid access attemps lately
                // Get id, count, & expiry
                var id = result["id"][0];
                var count = (long)result["count"][0];
                var expiry = DateTime.Parse((string)result["expiry"][0]);

                // If you want to increment invalid access attemps
                if (increment)
                {
                    count++;            // increment invalid access attempts
                    // columns to update
                    var update = new Dictionary<string, object> { { "count", count } };

                    // If gone to next section: increase timeout more.
                    if (count % Env._accessAttemps == 0 && count > Env._accessAttemps)
                    {
                        // Increase timeout
                        expiry = new[] { expiry, Utilz.ToMtn() }.Max().Add(Env._blockedAccessTimeOut);
                        // Be sure to update it in sql
                        update.Add("expiry", expiry.ToString("s"));
                    }

                    // Update the invalid Access users table
                    var ku = update.Keys.ToList();
                    update.Add("id", id);
                    Sql.Write($"UPDATE Blocked_Users SET {string.Join(", ", ku.Select(k => $"[{k}]=@{k}"))} " +
                                "WHERE [id]=@id", update.ToDictionary(s => "@" + s.Key, s => s.Value), 1);

                    // If user just got a timeout: Delete all access_tokens for that user
                    if (count == Env._accessAttemps)
                        Sql.Write("DELETE FROM Access_Token WHERE [user_id]=@userid",
                                    new Dictionary<string, object> { { "@userid", userid } }, -2);
                }

                // User is blocked: too many invalid access attempts
                if (count >= Env._accessAttemps)
                    return this_.BadRequest(new
                    {
                        Message = $"You are now blocked from access till {expiry} Mountain Standard Time."
                    });

                // User is ok
                return null;
            }
        }

        /// <summary>
        /// Gets the mutex lock for each user id
        /// </summary>
        /// <param name="userid">User id to get the lock</param>
        /// <returns>lock object</returns>
        public static object GetUserLock(long userid)
        {
            object ulock;
            // Mutex lock on entire _userLock collection
            lock (_userLock)
            {
                // Expiry for clean up
                var expiry = DateTime.Now.AddHours(1);
                // If does Not have lock for userid
                if (!_userLock.TryGetValue(userid, out var s))
                {
                    ulock = new object();                       // Create Lock object
                    _userLock.Add(userid, (ulock, expiry));     // Add to Map
                }
                // If found
                else
                {
                    ulock = s.lck;                              // Get the lock object
                    _userLock[userid] = (ulock, expiry);        // Update expiry
                }
            }

            // Return lock object
            return ulock;
        }

        /// <summary>
        /// Occasionally should be used to clean up resources
        /// </summary>
        public static void CleanUp()
        {
            // Clean up old access tokens
            Sql.Write("DELETE FROM Access_Token WHERE [expiry]<@date", new Dictionary<string, object>
            {
                {"@date", Utilz.ToMtn().ToString("s") }
            }, -2);

            // Clean up invalid accesses that have expired
            Sql.Write("DELETE FROM Blocked_Users WHERE [expiry]<@date", new Dictionary<string, object>
            {
                {"@date", Utilz.ToMtn().ToString("s") }
            }, -2);

            // Clean up expired user mutex locks
            lock (_userLock)
            {
                var now = DateTime.Now;
                foreach (var s in _userLock.Where(s => now > s.Value.dt))
                    _userLock.Remove(s.Key);
            }
        }

        public class Info : AppAuthJson
        {
            public string? email { get; set; }
            public string? password { get; set; }
        }
    }
}
