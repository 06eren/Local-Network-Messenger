using System;
using System.Security.Cryptography;
using System.Text;

namespace Local_Network_Messenger.Services
{
    public sealed record PasswordHash(byte[] Salt, byte[] Key, int Iterations);

    public sealed class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 120_000;

        public PasswordHash Hash(string password)
        {
            var salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            CryptographicOperations.ZeroMemory(passwordBytes);
            return new PasswordHash(salt, key, Iterations);
        }

        public bool Verify(string password, PasswordHash hash)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, hash.Salt, hash.Iterations, HashAlgorithmName.SHA256, hash.Key.Length);
            CryptographicOperations.ZeroMemory(passwordBytes);
            return CryptographicOperations.FixedTimeEquals(key, hash.Key);
        }
    }
}
