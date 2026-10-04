using System.Security.Cryptography;
using System.Text;

/* Deterministic Encryption: created by A.I. */

namespace StudentExchangeBck
{
    public static class DeterministicEncryption
    {
        public static string Encrypt(string text, string password)
        {
            byte[] plainText = Encoding.UTF8.GetBytes(text);

            // Derive a stable master key from the password.
            byte[] passwordKey = SHA256.HashData(
                Encoding.UTF8.GetBytes(password));

            // Derive an IV deterministically from:
            // password + plaintext
            byte[] ivSource;

            using (HMACSHA256 hmac = new HMACSHA256(passwordKey))
            {
                ivSource = hmac.ComputeHash(plainText);
            }

            byte[] iv = ivSource[..16];

            // Derive a separate AES key.
            byte[] encryptionKey;

            using (HMACSHA256 hmac = new HMACSHA256(passwordKey))
            {
                encryptionKey = hmac.ComputeHash(
                    Encoding.UTF8.GetBytes("EncryptionKey"));
            }

            byte[] cipherText;

            using (Aes aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using ICryptoTransform encryptor = aes.CreateEncryptor();

                cipherText = encryptor.TransformFinalBlock(
                    plainText,
                    0,
                    plainText.Length);
            }

            // Store IV + ciphertext.
            byte[] encryptedData = new byte[
                iv.Length + cipherText.Length];

            Buffer.BlockCopy(
                iv, 0,
                encryptedData, 0,
                iv.Length);

            Buffer.BlockCopy(
                cipherText, 0,
                encryptedData, iv.Length,
                cipherText.Length);

            return Convert.ToBase64String(encryptedData);
        }


        public static string Decrypt(string encryptedText, string password)
        {
            byte[] encryptedData =
                Convert.FromBase64String(encryptedText);

            if (encryptedData.Length < 32 || encryptedData.Length % 16 != 0)
                throw new ArgumentException("Invalid encrypted text.");

            byte[] iv = encryptedData[..16];
            byte[] cipherText = encryptedData[16..];

            byte[] passwordKey = SHA256.HashData(
                Encoding.UTF8.GetBytes(password));

            byte[] encryptionKey;

            using (HMACSHA256 hmac = new HMACSHA256(passwordKey))
            {
                encryptionKey = hmac.ComputeHash(
                    Encoding.UTF8.GetBytes("EncryptionKey"));
            }

            byte[] plainText;

            using (Aes aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using ICryptoTransform decryptor =
                    aes.CreateDecryptor();

                plainText = decryptor.TransformFinalBlock(
                    cipherText,
                    0,
                    cipherText.Length);
            }

            return Encoding.UTF8.GetString(plainText);
        }
    }
}
