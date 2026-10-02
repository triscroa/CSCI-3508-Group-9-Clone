using System.Security.Cryptography;

namespace StudentExchangeBck
{
    public static class AccessToken
    {
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
