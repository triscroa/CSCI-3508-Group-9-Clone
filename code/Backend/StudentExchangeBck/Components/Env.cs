using Newtonsoft.Json;

/* Enviroment Object: containing secrets & Settings. */

namespace StudentExchangeBck
{
    public static class Env
    {
        // Application Access Password
        public static string _appAccess { get; private set; } = null!;
        // Email Password Encryption
        public static string _emailSalt { get; private set; } = null!;
        // Password Encryption
        public static string _passSalt { get; private set; } = null!;

        // When the Access token expires
        public static readonly TimeSpan _tokenExpiry = new TimeSpan(21, 0, 0, 0);
        // After unsuccessfull attemps, timeout client is blocked
        public static readonly TimeSpan _blockedAccessTimeOut = new TimeSpan(0, 5, 0);
        // Number of unsuccessfull attemps before blocked timeout.
        public static readonly int _accessAttemps = 10;

        /// <summary>
        /// Get Enviroment from file
        /// </summary>
        /// <param name="salt">Temporary extra security password</param>
        /// <exception cref="Exception"></exception>
        public static void GetEnv(string salt = "tempPass!^74*")
        {
#if DEBUG
            string path = "bin_/.env";
#else
            string path = "/home/bin_/.env";
#endif
            // Read in Encypted env
            var env = System.IO.File.ReadAllText(path);
            // Decrypt
            env = DeterministicEncryption.Decrypt(env, salt);
            // Convert to dictionary object
            var d = JsonConvert.DeserializeObject<Dictionary<string, string>>(env)!
                        .ToDictionary(s => s.Key, s => s.Value.Trim());
            // Get values
            _appAccess = d["_appAccess"];
            _emailSalt = d["_emailSalt"];
            _passSalt = d["_passSalt"];

            // Validate Members
            if (_tokenExpiry.TotalDays <= 1)
                throw new Exception("Login needs expiry larger than 1 day.");
            if (_accessAttemps <= 1)
                throw new Exception("Login needs access attemps >1.");
        }
    }
}
