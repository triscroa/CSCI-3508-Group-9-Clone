using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace StudentExchangeBck
{
    [ApiController]
    [Route("[controller]")]
    public class Register : ControllerBase
    {
        [HttpPost]
        public IActionResult Post(Info request)
        {
            var msg = AppAuthJson.Validate(request);
            if (msg != null) return msg;

            try
            {
                ValidateFormatInfo(ref request, out var schoolId, out var emailCrypt, out var passCrypt);

                Sql.Write($"INSERT INTO User ([email],[first_name],[last_name],[password],[school_id],[datetime]) " +
                        $"VALUES (@email,@first_name,@last_name,@password,@school_id,@datetime)",
                        new Dictionary<string, object>
                        {
                            {"@email", emailCrypt},
                            {"@first_name", request.first_name!},
                            {"@last_name", request.last_name!},
                            {"@password", passCrypt},
                            {"@school_id", schoolId},
                            {"@datetime", Utilz.ToMtn().ToString("s")},
                        }, 1);

                return StatusCode(201, new
                {
                    Message = "User added.",
                    Email = request.email
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

        static readonly Regex _valEmail = new Regex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$");
        static readonly Regex _valName = new Regex(@"^[A-Z][a-z]*( [A-Z][a-z]*)*$");
        static readonly Regex _hasUpper = new Regex(@"[A-Z]+");
        static readonly Regex _hasSymbol = new Regex(@"[!@#$%^&*_\-+=]"); 
        static readonly Regex _valPass = new Regex(@"^[a-zA-Z0-9!@#$%^&*_\-+=]+$");

        static void ValidateFormatInfo(ref Info info, out object schoolID, out string emailCrypt, out string passCrypt)
        {
            if (string.IsNullOrWhiteSpace(info.email))
                throw new ArgumentException("There needs to be an Email.");
            if (string.IsNullOrWhiteSpace(info.first_name))
                throw new ArgumentException("There needs to be an First Name.");
            if (string.IsNullOrWhiteSpace(info.last_name))
                throw new ArgumentException("There needs to be an Last Name.");
            if (string.IsNullOrWhiteSpace(info.password))
                throw new ArgumentException("There needs to be an Password.");
            if (string.IsNullOrWhiteSpace(info.school))
                throw new ArgumentException("There needs to be an School.");

            info.email = info.email.Trim().ToLower();
            info.first_name = Utilz.FirstUpper(info.first_name.Trim());
            info.last_name = Utilz.FirstUpper(info.last_name.Trim());
            info.password = info.password.Trim();
            info.school = info.school.Trim();

            if (info.email.Length > 50)
                throw new ArgumentException("Email must be <=50 characters.");
            if (!_valEmail.IsMatch(info.email))
                throw new ArgumentException("Email is not in the correct format.");
            if (!_valName.IsMatch(info.first_name))
                throw new ArgumentException("First name is not in the correct format.");
            if (!_valName.IsMatch(info.last_name))
                throw new ArgumentException("Last name is not in the correct format.");

            if (info.password.Length < 8)
                throw new ArgumentException("Password needs to be at least 8+ characters.");
            if (info.password.Length > 50)
                throw new ArgumentException("Password must be <=50 characters.");
            if (!_hasUpper.IsMatch(info.password))
                throw new ArgumentException("Password must have at least 1+ uppercase character.");
            if (!_hasSymbol.IsMatch(info.password))
                throw new ArgumentException("Password must have at least 1+ symbol: '!@#$%^&*_-+='.");
            if (!_valPass.IsMatch(info.password))
                throw new ArgumentException("Password is invalid.");

            var result = Sql.Read("SELECT id FROM Schools WHERE [name]=@name",
                            new Dictionary<string, object> { { "@name", info.school } });
            if (result.Count == 0)
                throw new ArgumentException($"This school is not found in the data base: '{info.school}'.");
            schoolID = result["id"][0];

            emailCrypt = DeterministicEncryption.Encrypt(info.email, Env._emailSalt);
            passCrypt = DeterministicEncryption.Encrypt(info.password, Env._passSalt);
        }

        public class Info : AppAuthJson
        {
            public string? email { get; set; }
            public string? first_name { get; set; }
            public string? last_name { get; set; }
            public string? password { get; set; }
            public string? school { get; set; }
        }
    }
}
