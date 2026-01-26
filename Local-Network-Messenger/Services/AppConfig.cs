using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Local_Network_Messenger.Services
{
    public sealed record AppConfig(
        string? CryptoDllPath,
        string? CryptoExecutable,
        string? CryptoArguments,
        string? CryptoKeyId,
        string? ScanExecutable,
        string? ScanArguments,
        int? LanDiscoveryPort,
        int? LanTcpPort)
    {
        public string EffectiveCryptoKeyId => string.IsNullOrWhiteSpace(CryptoKeyId) ? "default" : CryptoKeyId;
        public int EffectiveDiscoveryPort => LanDiscoveryPort is > 0 ? LanDiscoveryPort.Value : 32145;
        public int EffectiveTcpPort => LanTcpPort is > 0 ? LanTcpPort.Value : 32146;

        public static AppConfig Load(string path)
        {
            var config = new AppConfig(null, null, null, null, null, null, null, null);
            if (!File.Exists(path))
            {
                return ResolveDefaults(config);
            }

            try
            {
                var json = File.ReadAllBytes(path);
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true
                };
                config = JsonSerializer.Deserialize<AppConfig>(json, options)
                    ?? config;
            }
            catch (IOException)
            {
                return ResolveDefaults(config);
            }
            catch (JsonException)
            {
                return ResolveDefaults(config);
            }

            return ResolveDefaults(config);
        }

        private static AppConfig ResolveDefaults(AppConfig config)
        {
            var baseDir = AppContext.BaseDirectory;
            var cryptoDll = Path.Combine(baseDir, "Tools", "crypto_bridge.dll");
            if (string.IsNullOrWhiteSpace(config.CryptoDllPath) && File.Exists(cryptoDll))
            {
                config = config with { CryptoDllPath = cryptoDll };
            }

            var cryptoDefault = Path.Combine(baseDir, "Tools", "crypto_bridge.exe");
            if (string.IsNullOrWhiteSpace(config.CryptoExecutable) && File.Exists(cryptoDefault))
            {
                config = config with { CryptoExecutable = cryptoDefault };
            }

            var scanScript = Path.Combine(baseDir, "Tools", "scan_service.py");
            if (string.IsNullOrWhiteSpace(config.ScanExecutable) && File.Exists(scanScript))
            {
                var pythonPath = FindOnPath("python.exe") ?? FindOnPath("py.exe");
                if (!string.IsNullOrWhiteSpace(pythonPath))
                {
                    config = config with
                    {
                        ScanExecutable = pythonPath,
                        ScanArguments = $"\"{scanScript}\""
                    };
                }
            }

            return config;
        }

        private static string? FindOnPath(string executable)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
            {
                return null;
            }

            var paths = pathEnv.Split(Path.PathSeparator)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path));

            foreach (var folder in paths)
            {
                try
                {
                    var fullPath = Path.Combine(folder, executable);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }
    }
}
