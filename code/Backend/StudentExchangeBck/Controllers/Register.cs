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

        [HttpGet]
        public IActionResult Get(AppAuthJson request)
        {
            var msg = AppAuthJson.ValidateWithToken(this, request, out var userId);
            if (msg != null) return msg;

            try
            {
                var result = Sql.Read("SELECT [email],[first_name],[last_name],S.[name] school " +
                                        "FROM User U, Schools S " +
                                        "WHERE U.[id]=@id AND " +
                                        "U.[school_id]=S.[id]",
                                        new Dictionary<string, object>
                                        {
                                            {"@id", userId },
                                        });
                if (result.Count == 0)
                    throw new Exception("There should have been a result for this user id, but there was none?");

                return Ok(new
                {
                    email = DeterministicEncryption.Decrypt((string)result["email"][0], Env._emailSalt),
                    first_name = result["first_name"][0],
                    last_name = result["last_name"][0],
                    school = result["school"][0]
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

        [HttpPatch]
        public IActionResult Patch(Info info)
        {
            var msg = AppAuthJson.ValidateWithToken(this, info, out var userId);
            if (msg != null) return msg;

            try
            {
                var updateKeys = "email first_name last_name password school".Split().ToHashSet();
                var cnt = updateKeys.Count;
                var nullKeys = new HashSet<string>(cnt);
                if (string.IsNullOrWhiteSpace(info.email))
                { info.email = null; nullKeys.Add("email"); }
                if (string.IsNullOrWhiteSpace(info.first_name))
                { info.first_name = null; nullKeys.Add("first_name"); }
                if (string.IsNullOrWhiteSpace(info.last_name))
                { info.last_name = null; nullKeys.Add("last_name"); }
                if (string.IsNullOrWhiteSpace(info.password))
                { info.password = null; nullKeys.Add("password"); }
                if (string.IsNullOrWhiteSpace(info.school))
                { info.school = null; nullKeys.Add("school"); }
                foreach (var k in nullKeys)
                    if (!updateKeys.Remove(k))
                        throw new Exception($"'nullkeys' has an extra key: {k}");
                if (nullKeys.Count > cnt) throw new Exception("nullKeys.Count > cnt");
                if (updateKeys.Count == 0)
                    throw new ArgumentException("There is nothing to update.");

                if (nullKeys.Count != 0)
                {
                    var col = nullKeys.Select(k => $"[{k}]").ToHashSet();
                    if (col.Remove("[school]"))
                        col.Add("S.[name] school");
                    var result = Sql.Read($"SELECT {string.Join(',', col)} " +
                                            "FROM User U, Schools S " +
                                            "WHERE U.[id]=@id AND " +
                                            "U.[school_id]=S.[id]",
                                            new Dictionary<string, object>
                                            {
                                                {"@id", userId },
                                            });
                    if (result.Count == 0)
                        throw new Exception("There should have been a result for this user id, but there was none?");

                    if (info.email == null)
                        info.email = DeterministicEncryption.Decrypt((string)result["email"][0], Env._emailSalt);
                    if (info.first_name == null)
                        info.first_name = (string)result["first_name"][0];
                    if (info.last_name == null)
                        info.last_name = (string)result["last_name"][0];
                    if (info.password == null)
                        info.password = DeterministicEncryption.Decrypt((string)result["password"][0], Env._passSalt);
                    if (info.school == null)
                        info.school = (string)result["school"][0];
                }

                ValidateFormatInfo(ref info, out var schoolId, out var emailCrypt, out var passCrypt);

                var update = new Dictionary<string, object>(updateKeys.Count);
                if (updateKeys.Contains("email"))
                    update.Add("email", emailCrypt);
                if (updateKeys.Contains("first_name"))
                    update.Add("first_name", info.first_name!);
                if (updateKeys.Contains("last_name"))
                    update.Add("last_name", info.last_name!);
                if (updateKeys.Contains("password"))
                    update.Add("password", passCrypt);
                if (updateKeys.Contains("school"))
                    update.Add("school_id", schoolId);
                update.Add("id", userId);

                Sql.Write($"UPDATE User SET {string.Join(", ", update.Keys.Select(k => $"[{k}]=@{k}"))} " +
                            "WHERE [id]=@id", update.ToDictionary(s => "@" + s.Key, s => s.Value), 1);

                return Ok(new
                {
                    Message = $"Successfully updated: {string.Join(", ", updateKeys)}.",
                    email = info.email
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
