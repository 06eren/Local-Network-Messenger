using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public sealed class SecurityEventLogger
    {
        private readonly string _path;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SecurityEventLogger(string path)
        {
            _path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        public async Task LogAsync(string eventType, string message, string? username = null, string? details = null)
        {
            var entry = new
            {
                at = DateTimeOffset.UtcNow,
                type = eventType,
                user = username,
                message,
                details
            };
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_path, json + Environment.NewLine);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
