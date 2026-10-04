using System.Security.Cryptography;

namespace StudentExchangeBck
{
    /* Access Token: Created by A.I. */
    public static class AccessToken
    {
        /// <summary>
        /// Generate Access Token
        /// </summary>
        /// <returns>Generates Access Token</returns>
        public static string Generate()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);

            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }
}
