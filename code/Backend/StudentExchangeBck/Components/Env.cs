using Newtonsoft.Json;

namespace StudentExchangeBck
{
    public static class Env
    {
        public static string _appAccess { get; private set; } = null!;
        public static string _emailSalt { get; private set; } = null!;
        public static string _passSalt { get; private set; } = null!;

        public static readonly TimeSpan _tokenExpiry = new TimeSpan(21, 0, 0, 0);

        public static void GetEnv(string salt = "tempPass!^74*", string path = "bin_/.env")
        {
            var env = System.IO.File.ReadAllText(path);
            env = DeterministicEncryption.Decrypt(env, salt);
            var d = JsonConvert.DeserializeObject<Dictionary<string, string>>(env)!
                        .ToDictionary(s => s.Key, s => s.Value.Trim());
            _appAccess = d["_appAccess"];
            _emailSalt = d["_emailSalt"];
            _passSalt = d["_passSalt"];

            if (_tokenExpiry.TotalDays <= 1)
                throw new Exception("Login needs expiry larger than 1 day.");
        }
    }
}
