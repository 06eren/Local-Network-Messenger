using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed class SessionStore
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        public SessionStore(string path)
        {
            _path = path;
        }

        public async Task SaveAsync(UserProfile user, CancellationToken cancellationToken)
        {
            var payload = new SessionPayload(user.Username, user.DisplayName, DateTimeOffset.UtcNow);
            var json = JsonSerializer.SerializeToUtf8Bytes(payload, _options);
            var protectedBytes = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllBytesAsync(_path, protectedBytes, cancellationToken);
        }

        public async Task<UserProfile?> LoadAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                var data = await File.ReadAllBytesAsync(_path, cancellationToken);
                var unprotected = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                var payload = JsonSerializer.Deserialize<SessionPayload>(unprotected, _options);
                if (payload == null)
                {
                    return null;
                }

                return new UserProfile(payload.Username, payload.DisplayName);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            if (File.Exists(_path))
            {
                try
                {
                    File.Delete(_path);
                }
                catch (IOException)
                {
                    // ignore
                }
            }

            return Task.CompletedTask;
        }

        private sealed record SessionPayload(string Username, string DisplayName, DateTimeOffset SavedAt);
    }
}
