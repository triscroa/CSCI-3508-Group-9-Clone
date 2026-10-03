using Microsoft.AspNetCore.Mvc;
using static StudentExchangeBck.Register;

namespace StudentExchangeBck
{
    [ApiController]
    [Route("[controller]")]
    public class LogIn : ControllerBase
    {
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

        public static void CleanUp()
        {
            // Clean up old access tokens
            Sql.Write("DELETE FROM Access_Token WHERE [expiry]<@date", new Dictionary<string, object>
            {
                {"@date", Utilz.ToMtn().ToString("s") }
            }, -2);
        }

        public class Info : AppAuthJson
        {
            public string? email { get; set; }
            public string? password { get; set; }
        }
    }
}
