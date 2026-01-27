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
            payload = NormalizeBase64(payload);
            return $"{CipherPrefix}{payload}";
        }

        public async Task<string> EncryptBase64PayloadAsync(string base64Payload, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(base64Payload))
            {
                return string.Empty;
            }

            var normalized = NormalizeBase64(base64Payload);
            var result = await _bridge.EncryptAsync(new CryptoRequest(_keyId, normalized), cancellationToken);
            if (!string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return $"{PlainPrefix}{normalized}";
            }

            var payload = string.IsNullOrWhiteSpace(result.PayloadBase64) ? normalized : result.PayloadBase64;
            payload = NormalizeBase64(payload);
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
                var base64 = NormalizeBase64(payload.Substring(CipherPrefix.Length));
                var result = await _bridge.DecryptAsync(new CryptoRequest(_keyId, base64), cancellationToken);
                if (!string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return "Sifre cozulmedi.";
                }

                var resolved = string.IsNullOrWhiteSpace(result.PayloadBase64) ? base64 : result.PayloadBase64;
                resolved = NormalizeBase64(resolved);
                return DecodeBase64OrFallback(resolved, "Sifre cozulmedi.");
            }

            return payload;
        }

        public async Task<string> DecryptToBase64Async(string payload, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            if (payload.StartsWith(PlainPrefix, StringComparison.Ordinal))
            {
                return NormalizeBase64(payload.Substring(PlainPrefix.Length));
            }

            if (payload.StartsWith(CipherPrefix, StringComparison.Ordinal))
            {
                var base64 = NormalizeBase64(payload.Substring(CipherPrefix.Length));
                var result = await _bridge.DecryptAsync(new CryptoRequest(_keyId, base64), cancellationToken);
                if (!string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return "!";
                }

                var resolved = string.IsNullOrWhiteSpace(result.PayloadBase64) ? base64 : result.PayloadBase64;
                return NormalizeBase64(resolved);
            }

            return NormalizeBase64(payload);
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

        private static string NormalizeBase64(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var buffer = new char[value.Length];
            var length = 0;
            foreach (var ch in value)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    buffer[length++] = ch;
                }
            }

            return new string(buffer, 0, length);
        }
    }
}
