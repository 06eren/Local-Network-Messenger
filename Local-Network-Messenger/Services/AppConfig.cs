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
        int? LanTcpPort,
        string? RelayHost,
        int? RelayPort,
        string? RelayMode,
        bool? RelayEnabled,
        bool? RelayServerEnabled,
        int? RelayServerPort,
        string[]? ManualPeers)
    {
        public string EffectiveCryptoKeyId => string.IsNullOrWhiteSpace(CryptoKeyId) ? "default" : CryptoKeyId;
        public int EffectiveDiscoveryPort => LanDiscoveryPort is > 0 ? LanDiscoveryPort.Value : 32145;
        public int EffectiveTcpPort => LanTcpPort is > 0 ? LanTcpPort.Value : 32146;
        public int EffectiveRelayPort => RelayPort is > 0 ? RelayPort.Value : 42100;
        public int EffectiveRelayServerPort => RelayServerPort is > 0 ? RelayServerPort.Value : 42100;
        public string EffectiveRelayMode => string.IsNullOrWhiteSpace(RelayMode) ? "local" : RelayMode!;
        public bool EffectiveRelayEnabled => RelayEnabled ?? false;
        public string[] EffectiveManualPeers => ManualPeers ?? Array.Empty<string>();

        public static AppConfig Load(string path)
        {
            var config = new AppConfig(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
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

        public static void Save(string path, AppConfig config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(config, options);
            File.WriteAllBytes(path, json);
        }

        private static AppConfig ResolveDefaults(AppConfig config)
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(config.CryptoDllPath) && !File.Exists(config.CryptoDllPath))
            {
                config = config with { CryptoDllPath = null };
            }

            if (!string.IsNullOrWhiteSpace(config.CryptoExecutable) && !File.Exists(config.CryptoExecutable))
            {
                config = config with { CryptoExecutable = null };
            }

            if (!string.IsNullOrWhiteSpace(config.ScanExecutable) && !File.Exists(config.ScanExecutable))
            {
                config = config with { ScanExecutable = null, ScanArguments = null };
            }

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
                        ScanArguments = $"-u -X utf8 \"{scanScript}\""
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
