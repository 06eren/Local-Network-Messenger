using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public sealed class MessageCipher
    {
        private const string PlainPrefix = "b64:";
        private const string CipherPrefix = "enc:";
        private static readonly Encoding TextEncoding = Encoding.UTF8;

        private readonly ICryptoBridge _bridge;
        private string _keyId;

        public MessageCipher(ICryptoBridge bridge, string keyId)
        {
            _bridge = bridge;
            _keyId = keyId;
        }

        public void UpdateKeyId(string keyId)
        {
            _keyId = string.IsNullOrWhiteSpace(keyId) ? "default" : keyId;
        }

        public async Task<string> EncryptAsync(string plainText, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            var plainBase64 = Convert.ToBase64String(TextEncoding.GetBytes(plainText));
            var result = await _bridge.EncryptAsync(new CryptoRequest(_keyId, plainBase64), cancellationToken);
            if (!string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return $"{PlainPrefix}{plainBase64}";
            }

            var payload = string.IsNullOrWhiteSpace(result.PayloadBase64) ? plainBase64 : result.PayloadBase64;
            return $"{CipherPrefix}{payload}";
        }

        public async Task<string> DecryptAsync(string payload, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return string.Empty;
            }

            if (payload.StartsWith(PlainPrefix, StringComparison.Ordinal))
            {
                var base64 = payload.Substring(PlainPrefix.Length);
                return DecodeBase64OrFallback(base64, string.Empty);
            }

            if (payload.StartsWith(CipherPrefix, StringComparison.Ordinal))
            {
                var base64 = payload.Substring(CipherPrefix.Length);
                var result = await _bridge.DecryptAsync(new CryptoRequest(_keyId, base64), cancellationToken);
                if (!string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return "Sifre cozulmedi.";
                }

                var resolved = string.IsNullOrWhiteSpace(result.PayloadBase64) ? base64 : result.PayloadBase64;
                return DecodeBase64OrFallback(resolved, "Sifre cozulmedi.");
            }

            return payload;
        }

        private static string DecodeBase64OrFallback(string base64, string fallback)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                return TextEncoding.GetString(bytes);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }
}
