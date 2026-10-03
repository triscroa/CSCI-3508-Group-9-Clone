using Microsoft.AspNetCore.Mvc;
using static StudentExchangeBck.Register;

namespace StudentExchangeBck
{
    [ApiController]
    [Route("[controller]")]
    public class LogIn : ControllerBase
    {
        public static readonly Dictionary<long, (object lck, DateTime dt)> _userLock
            = new Dictionary<long, (object, DateTime)>();

        [HttpPost]
        public IActionResult Post(Info data)
        {
            var msg = AppAuthJson.Validate(data);
            if (msg != null) return msg;

            string emailCrypt, passCrypt;
            try
            {
                if (string.IsNullOrWhiteSpace(data.email))
                    throw new ArgumentException("There needs to be a email.");
                if (string.IsNullOrWhiteSpace(data.password))
                    throw new ArgumentException("There needs to be a password.");

                emailCrypt = DeterministicEncryption.Encrypt(data.email.Trim().ToLower(), Env._emailSalt);
                passCrypt = DeterministicEncryption.Encrypt(data.password.Trim(), Env._passSalt);

                var result = Sql.Read("SELECT [id],[first_name] FROM User WHERE [email]=@email AND [password]=@pass",
                                new Dictionary<string, object>
                                {
                                    {"@email", emailCrypt },
                                    {"@pass", passCrypt }
                                });

                msg = IncInvalidForUser_Email(result.Count == 0, this, emailCrypt);
                if (msg != null) return msg;

                if (result.Count == 0)
                    throw new ArgumentException("Invalid Email or Password.");

                var id = result["id"][0];
                var first_name = result["first_name"][0];

                result = Sql.Read("SELECT [token] FROM Access_Token WHERE [user_id]=@id AND [expiry]>@now",
                            new Dictionary<string, object>
                            {
                                {"@id", id },
                                {"@now", Utilz.ToMtn().AddDays(1).ToString("s") },
                            });

                object token;
                if (result.Count != 0)
                    token = result["token"][0];
                else
                {
                    token = null!;
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            token = AccessToken.Generate();
                            Sql.Write("INSERT INTO Access_Token ([user_id],[token],[expiry]) VALUES " +
                                "(@user,@token,@expiry)", new Dictionary<string, object>
                                {
                                    { "@user", id },
                                    { "@token", token },
                                    { "@expiry", Utilz.ToMtn(DateTime.UtcNow.Add(Env._tokenExpiry)).ToString("s") }
                                }, 1);
                            break;
                        }
                        catch { token = null!; }
                    }

                    if (token == null)
                        throw new Exception("After serveral attempts, the Access Token could not be generated.");
                }

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

        public static IActionResult? IncInvalidForUser_Email(bool increment, ControllerBase this_, object emailCrypt)
        {
            var result = Sql.Read("SELECT [id] FROM User WHERE [email]=@email",
                                    new Dictionary<string, object> { { "@email", emailCrypt } });
            if (result.Count == 0) return null;

            return IncInvalidForUser(increment, this_, result["id"][0]);
        }
        public static IActionResult? IncInvalidForUser_Token(bool increment, ControllerBase this_, object token)
        {
            var result = Sql.Read("SELECT [user_id] FROM Access_Token WHERE [token]=@token",
                                    new Dictionary<string, object> { { "@token", token } });
            if (result.Count == 0) return null;

            return IncInvalidForUser(increment, this_, result["user_id"][0]);
        }
        public static IActionResult? IncInvalidForUser(bool increment, ControllerBase this_, object userid_)
        {
            var userid = (long)userid_;

            object ulock;
            lock (_userLock)
            {
                var expiry = DateTime.Now.AddHours(1);
                if (!_userLock.TryGetValue(userid, out var s))
                {
                    ulock = new object();
                    _userLock.Add(userid, (ulock, expiry));
                }
                else
                {
                    ulock = s.lck;
                    _userLock[userid] = (ulock, expiry);
                }
            }

            lock (ulock)
            {
                var result = Sql.Read("SELECT [id],[count],[expiry] FROM Blocked_Users " +
                                        "WHERE [user_id]=@userid AND [expiry]>@date",
                                        new Dictionary<string, object> 
                                        { 
                                            { "@userid", userid },
                                            { "@date", Utilz.ToMtn().ToString("s") }
                                        });

                if (result.Count == 0)
                {
                    if (increment)
                    {
                        Sql.Write("INSERT INTO Blocked_Users ([user_id],[count],[expiry]) VALUES " +
                                    "(@userid,@count,@expiry)", new Dictionary<string, object>
                                    {
                                    {"@userid", userid },
                                    {"@count", 1 },
                                    {"@expiry", Utilz.ToMtn().Add(Env._blockedAccessTimeOut).ToString("s")}
                                    }, 1);
                    }
                    return null;
                }

                var id = result["id"][0];
                var count = (long)result["count"][0];
                var expiry = DateTime.Parse((string)result["expiry"][0]);

                if (increment)
                {
                    count++;
                    var update = new Dictionary<string, object> { { "count", count } };

                    if (count % Env._accessAttemps == 0 && count > Env._accessAttemps)
                    {
                        expiry = new[] { expiry, Utilz.ToMtn() }.Max().Add(Env._blockedAccessTimeOut);
                        update.Add("expiry", expiry.ToString("s"));
                    }

                    var ku = update.Keys.ToList();
                    update.Add("id", id);
                    Sql.Write($"UPDATE Blocked_Users SET {string.Join(", ", ku.Select(k => $"[{k}]=@{k}"))} " +
                                "WHERE [id]=@id", update.ToDictionary(s => "@" + s.Key, s => s.Value), 1);

                    if (count == Env._accessAttemps)
                        Sql.Write("DELETE FROM Access_Token WHERE [user_id]=@userid",
                                    new Dictionary<string, object> { { "@userid", userid } }, -2);
                }

                if (count >= Env._accessAttemps)
                    return this_.BadRequest(new
                    {
                        Message = $"You are now blocked from access till {expiry} Mountain Standard Time."
                    });
                return null;
            }
        }

        public static void CleanUp()
        {
            // Clean up old access tokens
            Sql.Write("DELETE FROM Access_Token WHERE [expiry]<@date", new Dictionary<string, object>
            {
                {"@date", Utilz.ToMtn().ToString("s") }
            }, -2);

            Sql.Write("DELETE FROM Blocked_Users WHERE [expiry]<@date", new Dictionary<string, object>
            {
                {"@date", Utilz.ToMtn().ToString("s") }
            }, -2);

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
