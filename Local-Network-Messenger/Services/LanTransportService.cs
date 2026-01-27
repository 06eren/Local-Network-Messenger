using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed record PeerInfo(
        string Username,
        string DisplayName,
        IPEndPoint EndPoint,
        DateTimeOffset LastSeen,
        bool IsOnline,
        string Source);

    public sealed record LanSendResult(bool Success, int SentCount, string Message);

    public sealed class PeerChangedEventArgs : EventArgs
    {
        public PeerChangedEventArgs(PeerInfo peer)
        {
            Peer = peer;
        }

        public PeerInfo Peer { get; }
    }

    public sealed class LanMessageReceivedEventArgs : EventArgs
    {
        public LanMessageReceivedEventArgs(string messageId, string from, string fromDisplayName, string threadId, string text)
        {
            MessageId = messageId;
            From = from;
            FromDisplayName = fromDisplayName;
            ThreadId = threadId;
            Text = text;
        }

        public string MessageId { get; }
        public string From { get; }
        public string FromDisplayName { get; }
        public string ThreadId { get; }
        public string Text { get; }
    }

    public sealed class LanChatEditReceivedEventArgs : EventArgs
    {
        public LanChatEditReceivedEventArgs(string messageId, string from, string threadId, string text)
        {
            MessageId = messageId;
            From = from;
            ThreadId = threadId;
            Text = text;
        }

        public string MessageId { get; }
        public string From { get; }
        public string ThreadId { get; }
        public string Text { get; }
    }

    public sealed class LanChatDeleteReceivedEventArgs : EventArgs
    {
        public LanChatDeleteReceivedEventArgs(string messageId, string from, string threadId)
        {
            MessageId = messageId;
            From = from;
            ThreadId = threadId;
        }

        public string MessageId { get; }
        public string From { get; }
        public string ThreadId { get; }
    }

    public sealed class LanFileReceivedEventArgs : EventArgs
    {
        public LanFileReceivedEventArgs(
            string from,
            string fromDisplayName,
            string threadId,
            string messageId,
            string fileName,
            string filePath,
            long sizeBytes,
            string? contentType,
            FileScanResult scanResult)
        {
            From = from;
            FromDisplayName = fromDisplayName;
            ThreadId = threadId;
            MessageId = messageId;
            FileName = fileName;
            FilePath = filePath;
            SizeBytes = sizeBytes;
            ContentType = contentType;
            ScanResult = scanResult;
        }

        public string From { get; }
        public string FromDisplayName { get; }
        public string ThreadId { get; }
        public string MessageId { get; }
        public string FileName { get; }
        public string FilePath { get; }
        public long SizeBytes { get; }
        public string? ContentType { get; }
        public FileScanResult ScanResult { get; }
    }

    public sealed class LanChatAckReceivedEventArgs : EventArgs
    {
        public LanChatAckReceivedEventArgs(string messageId, string threadId, string status)
        {
            MessageId = messageId;
            ThreadId = threadId;
            Status = status;
        }

        public string MessageId { get; }
        public string ThreadId { get; }
        public string Status { get; }
    }

    public sealed class LanChatReadReceivedEventArgs : EventArgs
    {
        public LanChatReadReceivedEventArgs(string threadId)
        {
            ThreadId = threadId;
        }

        public string ThreadId { get; }
    }

    public sealed class LanTypingReceivedEventArgs : EventArgs
    {
        public LanTypingReceivedEventArgs(string threadId, string from, bool isTyping)
        {
            ThreadId = threadId;
            From = from;
            IsTyping = isTyping;
        }

        public string ThreadId { get; }
        public string From { get; }
        public bool IsTyping { get; }
    }

    public sealed class FileTransferProgressEventArgs : EventArgs
    {
        public FileTransferProgressEventArgs(string threadId, string messageId, double progress, bool isOutgoing)
        {
            ThreadId = threadId;
            MessageId = messageId;
            Progress = progress;
            IsOutgoing = isOutgoing;
        }

        public string ThreadId { get; }
        public string MessageId { get; }
        public double Progress { get; }
        public bool IsOutgoing { get; }
    }

    public sealed class FileTransferStartedEventArgs : EventArgs
    {
        public FileTransferStartedEventArgs(
            string threadId,
            string messageId,
            string from,
            string fromDisplayName,
            string fileName,
            long sizeBytes)
        {
            ThreadId = threadId;
            MessageId = messageId;
            From = from;
            FromDisplayName = fromDisplayName;
            FileName = fileName;
            SizeBytes = sizeBytes;
        }

        public string ThreadId { get; }
        public string MessageId { get; }
        public string From { get; }
        public string FromDisplayName { get; }
        public string FileName { get; }
        public long SizeBytes { get; }
    }

    public sealed class ConnectionQualityEventArgs : EventArgs
    {
        public ConnectionQualityEventArgs(string username, double? pingMs, double? lossPercent)
        {
            Username = username;
            PingMs = pingMs;
            LossPercent = lossPercent;
        }

        public string Username { get; }
        public double? PingMs { get; }
        public double? LossPercent { get; }
    }

    public sealed class LanTransportService : IAsyncDisposable
    {
        private const int ChunkSize = 256 * 1024;
        private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(8);
        private const int PingWindowSize = 12;
        private readonly MessageCipher _cipher;
        private readonly IFileScanService _scanService;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly ConcurrentDictionary<string, PeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FileReceiveSession> _incomingFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _manualPeers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RelayClientSession> _relaySessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, PingTracker> _pingTrackers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ProgressState> _progressStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _relayWriteLock = new(1, 1);
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private readonly int _discoveryPort;
        private int _listenPort;
        private UdpClient? _udpClient;
        private TcpListener? _listener;
        private TcpListener? _relayListener;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _relayClientCts;
        private CancellationTokenSource? _relayServerCts;
        private Task? _udpReceiveTask;
        private Task? _announceTask;
        private Task? _cleanupTask;
        private Task? _pingTask;
        private Task? _acceptTask;
        private Task? _relayAcceptTask;
        private Task? _relayReceiveTask;
        private UserProfile? _user;
        private TcpClient? _relayClient;
        private StreamReader? _relayReader;
        private StreamWriter? _relayWriter;
        private string? _relayHost;
        private int _relayPort;
        private string _relayMode = "local";
        private bool _relayEnabled;
        private IPEndPoint? _relayServerEndPoint;
        private bool _relayConnected;

        public LanTransportService(MessageCipher cipher, IFileScanService scanService, int discoveryPort, int listenPort)
        {
            _cipher = cipher;
            _scanService = scanService;
            _discoveryPort = discoveryPort;
            _listenPort = listenPort;
        }

        public event EventHandler<PeerChangedEventArgs>? PeerChanged;
        public event EventHandler<LanMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<LanChatEditReceivedEventArgs>? ChatEditReceived;
        public event EventHandler<LanChatDeleteReceivedEventArgs>? ChatDeleteReceived;
        public event EventHandler<LanFileReceivedEventArgs>? FileReceived;
        public event EventHandler<LanChatAckReceivedEventArgs>? ChatAckReceived;
        public event EventHandler<LanChatReadReceivedEventArgs>? ChatReadReceived;
        public event EventHandler<LanTypingReceivedEventArgs>? TypingReceived;
        public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgress;
        public event EventHandler<FileTransferStartedEventArgs>? FileTransferStarted;
        public event EventHandler<ConnectionQualityEventArgs>? ConnectionQualityUpdated;

        public int ListenPort => _listenPort;

        public IEnumerable<PeerInfo> Peers => _peers.Values;

        public int ManualPeerCount => _manualPeers.Count;

        public bool IsRelayConnected => _relayConnected;

        public async Task StartAsync(UserProfile user, CancellationToken cancellationToken)
        {
            _user = user;
            if (_cts != null)
            {
                await BroadcastPresenceAsync(cancellationToken);
                return;
            }

            _cts = new CancellationTokenSource();
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => _cts?.Cancel());
            }

            var token = _cts.Token;
            InitializeUdpListener();
            InitializeTcpListener();
            _udpReceiveTask = Task.Run(() => ReceivePresenceLoopAsync(token), token);
            _announceTask = Task.Run(() => AnnounceLoopAsync(token), token);
            _cleanupTask = Task.Run(() => CleanupLoopAsync(token), token);
            _pingTask = Task.Run(() => PingLoopAsync(token), token);
            _acceptTask = Task.Run(() => AcceptLoopAsync(token), token);
            await BroadcastPresenceAsync(cancellationToken);
        }

        public async Task StopAsync()
        {
            if (_cts == null)
            {
                return;
            }

            _cts.Cancel();
            _udpClient?.Dispose();
            _listener?.Stop();

            var tasks = new[] { _udpReceiveTask, _announceTask, _cleanupTask, _pingTask, _acceptTask };
            foreach (var task in tasks)
            {
                if (task == null)
                {
                    continue;
                }

                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _cts.Dispose();
            _cts = null;
            _udpReceiveTask = null;
            _announceTask = null;
            _cleanupTask = null;
            _pingTask = null;
            _acceptTask = null;
            _udpClient = null;
            _listener = null;
            _peers.Clear();
            _incomingFiles.Clear();
            _manualPeers.Clear();
            _pingTrackers.Clear();
            _progressStates.Clear();

            await StopRelayClientAsync();
            await StopRelayServerAsync();
        }

        public async Task<PeerInfo?> AddManualPeerAsync(string host, int port, string? displayName, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            if (port <= 0)
            {
                port = _listenPort;
            }

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host);
            }
            catch (SocketException)
            {
                return null;
            }

            var address = Array.Find(addresses, ip => ip.AddressFamily == AddressFamily.InterNetwork);
            if (address == null)
            {
                return null;
            }

            var endPoint = new IPEndPoint(address, port);
            var ack = await SendPeerHelloAsync(endPoint, cancellationToken);
            if (ack == null)
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var peer = new PeerInfo(
                ack.Username,
                string.IsNullOrWhiteSpace(displayName) ? ack.DisplayName : displayName!,
                new IPEndPoint(address, ack.ListenPort),
                now,
                true,
                "manual");

            _peers[peer.Username] = peer;
            _manualPeers[peer.Username] = true;
            PeerChanged?.Invoke(this, new PeerChangedEventArgs(peer));
            return peer;
        }

        public async Task ConfigureRelayAsync(AppConfig config, UserProfile? user, CancellationToken cancellationToken)
        {
            _relayEnabled = config.EffectiveRelayEnabled;
            _relayMode = config.EffectiveRelayMode;
            _relayHost = string.IsNullOrWhiteSpace(config.RelayHost) ? null : config.RelayHost.Trim();
            _relayPort = config.EffectiveRelayPort;

            if (config.RelayServerEnabled == true)
            {
                await StartRelayServerAsync(config.EffectiveRelayServerPort, cancellationToken);
            }
            else
            {
                await StopRelayServerAsync();
            }

            if (!_relayEnabled ||
                string.Equals(_relayMode, "local", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(_relayHost))
            {
                await StopRelayClientAsync();
                return;
            }

            await StartRelayClientAsync(_relayHost, _relayPort, user ?? _user, cancellationToken);
        }

        public async Task<LanSendResult> SendMessageAsync(string targetUser, string text, string messageId, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return new LanSendResult(false, 0, "Oturum bulunamadi.");
            }

            if (string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (UseRelayForAll())
                {
                    var sent = await SendRelayMessageAsync("all", text, messageId, cancellationToken);
                    return new LanSendResult(sent, sent ? 1 : 0, sent ? "Gonderildi." : "Relay baglantisi yok.");
                }

                return await BroadcastMessageAsync(text, cancellationToken);
            }

            if (!_peers.TryGetValue(targetUser, out var peer))
            {
                return new LanSendResult(false, 0, "Kisi agda bulunamadi.");
            }

            if (!peer.IsOnline && DateTimeOffset.UtcNow - peer.LastSeen > TimeSpan.FromMinutes(2))
            {
                return new LanSendResult(false, 0, "Kisi agda bulunamadi.");
            }

            if (string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase) && !_relayConnected)
            {
                return new LanSendResult(false, 0, "Relay baglantisi yok.");
            }

            if (ShouldUseRelay(targetUser, peer))
            {
                var sent = await SendRelayMessageAsync(targetUser, text, messageId, cancellationToken);
                return new LanSendResult(sent, sent ? 1 : 0, sent ? "Gonderildi." : "Relay baglantisi yok.");
            }

            var cipherText = await _cipher.EncryptAsync(text, cancellationToken);
            var payload = new LanChatMessage(
                messageId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                _user.Username,
                cipherText,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _listenPort);
            await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatMessage, payload, cancellationToken);
            return new LanSendResult(true, 1, "Gonderildi.");
        }

        public async Task<LanSendResult> SendFileAsync(
            string targetUser,
            string fileName,
            byte[] data,
            string? contentType,
            string messageId,
            CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return new LanSendResult(false, 0, "Oturum bulunamadi.");
            }

            await using var stream = new MemoryStream(data, writable: false);
            return await SendFileStreamAsync(
                targetUser,
                fileName,
                data.LongLength,
                stream,
                contentType,
                messageId,
                cancellationToken);
        }

        public async Task<LanSendResult> SendFileFromPathAsync(
            string targetUser,
            string filePath,
            string? contentType,
            string messageId,
            CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return new LanSendResult(false, 0, "Oturum bulunamadi.");
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new LanSendResult(false, 0, "Dosya bulunamadi.");
            }

            var info = new FileInfo(filePath);
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await SendFileStreamAsync(
                targetUser,
                info.Name,
                info.Length,
                stream,
                contentType,
                messageId,
                cancellationToken);
        }

        public Task AnnouncePresenceAsync(CancellationToken cancellationToken)
        {
            return BroadcastPresenceAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }

        private async Task StartRelayServerAsync(int port, CancellationToken cancellationToken)
        {
            if (_relayListener != null)
            {
                if (_relayListener.LocalEndpoint is IPEndPoint endpoint && endpoint.Port == port)
                {
                    return;
                }

                await StopRelayServerAsync();
            }

            _relayListener = new TcpListener(IPAddress.Any, port);
            _relayListener.Start();
            _relayServerCts = new CancellationTokenSource();
            _relayAcceptTask = Task.Run(() => AcceptRelayLoopAsync(_relayServerCts.Token), _relayServerCts.Token);
        }

        private async Task StopRelayServerAsync()
        {
            if (_relayListener == null && _relayServerCts == null)
            {
                return;
            }

            _relayServerCts?.Cancel();
            try
            {
                _relayListener?.Stop();
            }
            catch (SocketException)
            {
            }

            if (_relayAcceptTask != null)
            {
                try
                {
                    await _relayAcceptTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            foreach (var session in _relaySessions.Values)
            {
                try
                {
                    session.Client.Close();
                }
                catch (SocketException)
                {
                }
            }

            _relaySessions.Clear();
            _relayServerCts?.Dispose();
            _relayServerCts = null;
            _relayAcceptTask = null;
            _relayListener = null;
        }

        private async Task AcceptRelayLoopAsync(CancellationToken cancellationToken)
        {
            if (_relayListener == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await _relayListener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                _ = Task.Run(() => HandleRelayClientAsync(client, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleRelayClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using var clientScope = client;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            RelayClientSession? session = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync();
                }
                catch (IOException)
                {
                    break;
                }

                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                RelayPacket? packet;
                try
                {
                    packet = JsonSerializer.Deserialize<RelayPacket>(line, _jsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (packet == null)
                {
                    continue;
                }

                if (string.Equals(packet.Type, RelayPacketTypes.Register, StringComparison.OrdinalIgnoreCase))
                {
                    RelayRegister? register;
                    try
                    {
                        register = packet.Payload.Deserialize<RelayRegister>(_jsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (register == null || string.IsNullOrWhiteSpace(register.Username))
                    {
                        continue;
                    }

                    var username = register.Username.Trim();
                    var displayName = string.IsNullOrWhiteSpace(register.DisplayName)
                        ? username
                        : register.DisplayName.Trim();
                    session = new RelayClientSession(client, writer, username, displayName);
                    _relaySessions.AddOrUpdate(
                        username,
                        _ => session,
                        (_, existing) =>
                        {
                            try
                            {
                                existing.Client.Close();
                            }
                            catch (SocketException)
                            {
                            }

                            return session;
                        });

                    await SendRelayPeerListAsync(session, cancellationToken);
                    await BroadcastRelayPresenceAsync(session, cancellationToken);
                    continue;
                }

                if (session == null)
                {
                    continue;
                }

                await RouteRelayPacketAsync(session, packet, cancellationToken);
            }

            if (session != null)
            {
                _relaySessions.TryRemove(session.Username, out _);
                await BroadcastRelayOfflineAsync(session, CancellationToken.None);
            }
        }

        private async Task SendRelayPeerListAsync(RelayClientSession session, CancellationToken cancellationToken)
        {
            foreach (var entry in _relaySessions.Values)
            {
                if (string.Equals(entry.Username, session.Username, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var payload = new LanPeerHello(entry.Username, entry.DisplayName, 0);
                var packet = new RelayPacket(
                    LanPacketTypes.PeerHello,
                    session.Username,
                    JsonSerializer.SerializeToElement(payload, _jsonOptions));
                await SendRelayPacketToSessionAsync(session, packet, cancellationToken);
            }
        }

        private async Task BroadcastRelayPresenceAsync(RelayClientSession session, CancellationToken cancellationToken)
        {
            var payload = new LanPeerHello(session.Username, session.DisplayName, 0);
            var packet = new RelayPacket(
                LanPacketTypes.PeerHello,
                "all",
                JsonSerializer.SerializeToElement(payload, _jsonOptions));

            foreach (var entry in _relaySessions.Values)
            {
                if (string.Equals(entry.Username, session.Username, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await SendRelayPacketToSessionAsync(entry, packet, cancellationToken);
            }
        }

        private async Task BroadcastRelayOfflineAsync(RelayClientSession session, CancellationToken cancellationToken)
        {
            var payload = new LanPeerOffline(session.Username, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var packet = new RelayPacket(
                LanPacketTypes.PeerOffline,
                "all",
                JsonSerializer.SerializeToElement(payload, _jsonOptions));

            foreach (var entry in _relaySessions.Values)
            {
                if (string.Equals(entry.Username, session.Username, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await SendRelayPacketToSessionAsync(entry, packet, cancellationToken);
            }
        }

        private async Task RouteRelayPacketAsync(RelayClientSession sender, RelayPacket packet, CancellationToken cancellationToken)
        {
            if (string.Equals(packet.To, "all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in _relaySessions.Values)
                {
                    if (string.Equals(entry.Username, sender.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await SendRelayPacketToSessionAsync(entry, packet, cancellationToken);
                }

                return;
            }

            if (_relaySessions.TryGetValue(packet.To, out var target))
            {
                await SendRelayPacketToSessionAsync(target, packet, cancellationToken);
            }
        }

        private async Task SendRelayPacketToSessionAsync(
            RelayClientSession session,
            RelayPacket packet,
            CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(packet, _jsonOptions);
            await session.SendLock.WaitAsync(cancellationToken);
            try
            {
                await session.Writer.WriteLineAsync(json);
            }
            finally
            {
                session.SendLock.Release();
            }
        }

        private async Task StartRelayClientAsync(string host, int port, UserProfile? user, CancellationToken cancellationToken)
        {
            if (user == null)
            {
                return;
            }

            if (_relayClient != null &&
                _relayConnected &&
                string.Equals(_relayHost, host, StringComparison.OrdinalIgnoreCase) &&
                _relayPort == port)
            {
                return;
            }

            await StopRelayClientAsync();

            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, cancellationToken);
            }
            catch (SocketException)
            {
                _relayConnected = false;
                return;
            }

            _relayClient = client;
            _relayHost = host;
            _relayPort = port;
            _relayServerEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            _relayClientCts = new CancellationTokenSource();
            var token = _relayClientCts.Token;

            var stream = client.GetStream();
            _relayReader = new StreamReader(stream, Encoding.UTF8);
            _relayWriter = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            _relayConnected = true;
            _relayReceiveTask = Task.Run(() => ReceiveRelayLoopAsync(token), token);
            await SendRelayRegisterAsync(user, cancellationToken);
        }

        private async Task StopRelayClientAsync()
        {
            if (_relayClient == null && _relayClientCts == null)
            {
                return;
            }

            _relayConnected = false;
            _relayClientCts?.Cancel();
            try
            {
                _relayClient?.Close();
            }
            catch (SocketException)
            {
            }

            if (_relayReceiveTask != null)
            {
                try
                {
                    await _relayReceiveTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _relayClientCts?.Dispose();
            _relayClientCts = null;
            _relayReceiveTask = null;
            _relayClient = null;
            _relayReader = null;
            _relayWriter = null;
            _relayServerEndPoint = null;
            MarkRelayPeersOffline();
        }

        private async Task ReceiveRelayLoopAsync(CancellationToken cancellationToken)
        {
            if (_relayReader == null)
            {
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await _relayReader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    RelayPacket? packet;
                    try
                    {
                        packet = JsonSerializer.Deserialize<RelayPacket>(line, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (packet == null)
                    {
                        continue;
                    }

                    await HandleRelayPacketAsync(packet, cancellationToken);
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                _relayConnected = false;
                MarkRelayPeersOffline();
            }
        }

        private async Task HandleRelayPacketAsync(RelayPacket packet, CancellationToken cancellationToken)
        {
            switch (packet.Type)
            {
                case LanPacketTypes.PeerHello:
                    await HandleRelayPeerHelloAsync(packet.Payload, cancellationToken);
                    break;
                case LanPacketTypes.PeerOffline:
                    await HandleRelayPeerOfflineAsync(packet.Payload, cancellationToken);
                    break;
                case LanPacketTypes.ChatMessage:
                    await HandleChatMessageAsync(packet.Payload, null, true, cancellationToken);
                    break;
                case LanPacketTypes.ChatAck:
                    await HandleChatAckAsync(packet.Payload, cancellationToken);
                    break;
                case LanPacketTypes.ChatRead:
                    await HandleChatReadAsync(packet.Payload, cancellationToken);
                    break;
                case LanPacketTypes.ChatTyping:
                    await HandleChatTypingAsync(packet.Payload, cancellationToken);
                    break;
                case LanPacketTypes.ChatEdit:
                    await HandleChatEditAsync(packet.Payload, null, true, cancellationToken);
                    break;
                case LanPacketTypes.ChatDelete:
                    await HandleChatDeleteAsync(packet.Payload, null, true, cancellationToken);
                    break;
                case LanPacketTypes.FileStart:
                    await HandleFileStartAsync(packet.Payload, null, true, cancellationToken);
                    break;
                case LanPacketTypes.FileChunk:
                    await HandleFileChunkAsync(packet.Payload, cancellationToken);
                    break;
                case LanPacketTypes.NetPing:
                    await HandlePingAsync(packet.Payload, null, true, cancellationToken);
                    break;
                case LanPacketTypes.NetPong:
                    await HandlePongAsync(packet.Payload, cancellationToken);
                    break;
                default:
                    break;
            }
        }

        private async Task<bool> SendRelayMessageAsync(string targetUser, string text, string messageId, CancellationToken cancellationToken)
        {
            if (_user == null || !_relayConnected)
            {
                return false;
            }

            var cipherText = await _cipher.EncryptAsync(text, cancellationToken);
            var payload = new LanChatMessage(
                messageId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                _user.Username,
                cipherText,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _listenPort);
            await SendRelayPacketAsync(LanPacketTypes.ChatMessage, targetUser, payload, cancellationToken);
            return true;
        }

        private async Task<bool> SendFileViaRelayAsync(
            string targetUser,
            string fileName,
            long sizeBytes,
            Stream stream,
            string? contentType,
            string fileId,
            CancellationToken cancellationToken,
            string? sha256Base64 = null)
        {
            if (_user == null || !_relayConnected)
            {
                return false;
            }

            var sha256 = sha256Base64 ?? await ComputeSha256Base64Async(stream, cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var nameCipher = await _cipher.EncryptAsync(fileName, cancellationToken);
            var startPayload = new LanFileStart(
                fileId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                nameCipher,
                sizeBytes,
                contentType,
                sha256,
                _listenPort);
            await SendRelayPacketAsync(LanPacketTypes.FileStart, targetUser, startPayload, cancellationToken);

            var buffer = new byte[ChunkSize];
            var index = 0;
            long sentBytes = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                sentBytes += read;
                var base64 = Convert.ToBase64String(buffer, 0, read);
                var cipher = await _cipher.EncryptBase64PayloadAsync(base64, cancellationToken);
                var isLast = sentBytes >= sizeBytes;
                var chunkPayload = new LanFileChunk(fileId, index, cipher, isLast);
                await SendRelayPacketAsync(LanPacketTypes.FileChunk, targetUser, chunkPayload, cancellationToken);
                var progress = sizeBytes > 0 ? Math.Min(100, (sentBytes * 100d) / sizeBytes) : 0;
                var threadId = string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase)
                    ? "all"
                    : targetUser;
                if (ShouldReportProgress(fileId, progress))
                {
                    FileTransferProgress?.Invoke(
                        this,
                        new FileTransferProgressEventArgs(threadId, fileId, progress, true));
                }
                index += 1;
            }

            ClearProgressState(fileId);
            return true;
        }

        private async Task SendRelayRegisterAsync(UserProfile user, CancellationToken cancellationToken)
        {
            var payload = new RelayRegister(user.Username, user.DisplayName);
            await SendRelayPacketAsync(RelayPacketTypes.Register, "server", payload, cancellationToken);
        }

        private async Task SendRelayPacketAsync(string type, string to, object payload, CancellationToken cancellationToken)
        {
            if (_relayWriter == null)
            {
                return;
            }

            var packet = new RelayPacket(type, to, JsonSerializer.SerializeToElement(payload, _jsonOptions));
            var json = JsonSerializer.Serialize(packet, _jsonOptions);
            await _relayWriteLock.WaitAsync(cancellationToken);
            try
            {
                await _relayWriter.WriteLineAsync(json);
            }
            finally
            {
                _relayWriteLock.Release();
            }
        }

        private void InitializeUdpListener()
        {
            var udpClient = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
            _udpClient = udpClient;
        }

        private void InitializeTcpListener()
        {
            var listener = new TcpListener(IPAddress.Any, _listenPort);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();
            }

            _listener = listener;
            _listenPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private async Task BroadcastPresenceAsync(CancellationToken cancellationToken)
        {
            if (_udpClient == null || _user == null)
            {
                return;
            }

            var announcement = new LanPresenceAnnouncement(
                _instanceId,
                _user.Username,
                _user.DisplayName,
                _listenPort,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var json = JsonSerializer.Serialize(announcement, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var endPoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
            await _udpClient.SendAsync(bytes, bytes.Length, endPoint);
        }

        private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await BroadcastPresenceAsync(cancellationToken);
                await Task.Delay(PresenceInterval, cancellationToken);
            }
        }

        private async Task ReceivePresenceLoopAsync(CancellationToken cancellationToken)
        {
            if (_udpClient == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udpClient.ReceiveAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(result.Buffer);
                LanPresenceAnnouncement? announcement;
                try
                {
                    announcement = JsonSerializer.Deserialize<LanPresenceAnnouncement>(json, _jsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (announcement == null)
                {
                    continue;
                }

                if (string.Equals(announcement.InstanceId, _instanceId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_user != null &&
                    string.Equals(announcement.Username, _user.Username, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var endPoint = new IPEndPoint(result.RemoteEndPoint.Address, announcement.TcpPort);
                var updated = new PeerInfo(announcement.Username, announcement.DisplayName, endPoint, now, true, "local");
                var shouldRaise = false;
                _peers.AddOrUpdate(
                    updated.Username,
                    _ =>
                    {
                        shouldRaise = true;
                        return updated;
                    },
                    (_, existing) =>
                    {
                        if (!existing.IsOnline ||
                            !string.Equals(existing.DisplayName, updated.DisplayName, StringComparison.Ordinal) ||
                            !Equals(existing.EndPoint, updated.EndPoint) ||
                            !string.Equals(existing.Source, updated.Source, StringComparison.Ordinal))
                        {
                            shouldRaise = true;
                        }

                        return updated with { LastSeen = now, IsOnline = true, Source = "local" };
                    });

                if (shouldRaise)
                {
                    PeerChanged?.Invoke(this, new PeerChangedEventArgs(updated));
                }
            }
        }

        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var entry in _peers)
                {
                    var peer = entry.Value;
                    if (!peer.IsOnline)
                    {
                        continue;
                    }

                    if (!string.Equals(peer.Source, "local", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (now - peer.LastSeen < PresenceTimeout)
                    {
                        continue;
                    }

                    var updated = peer with { IsOnline = false };
                    _peers[entry.Key] = updated;
                    PeerChanged?.Invoke(this, new PeerChangedEventArgs(updated));
                    ClearPingState(entry.Key);
                }

                await Task.Delay(CleanupInterval, cancellationToken);
            }
        }

        private async Task PingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await SendPingBatchAsync(cancellationToken);
                CleanupPingTimeouts();
                await Task.Delay(PingInterval, cancellationToken);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            if (_listener == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using var clientScope = client;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync();
                }
                catch (IOException)
                {
                    break;
                }

                if (line == null)
                {
                    break;
                }

                LanPacket? packet;
                try
                {
                    packet = JsonSerializer.Deserialize<LanPacket>(line, _jsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (packet == null)
                {
                    continue;
                }

                switch (packet.Type)
                {
                    case LanPacketTypes.PeerHello:
                        await HandlePeerHelloAsync(packet.Payload, remoteEndPoint, writer, cancellationToken);
                        break;
                    case LanPacketTypes.PeerHelloAck:
                        await HandlePeerHelloAckAsync(packet.Payload, remoteEndPoint, cancellationToken);
                        break;
                    case LanPacketTypes.ChatMessage:
                        await HandleChatMessageAsync(packet.Payload, remoteEndPoint, false, cancellationToken);
                        break;
                    case LanPacketTypes.ChatAck:
                        await HandleChatAckAsync(packet.Payload, cancellationToken);
                        break;
                    case LanPacketTypes.ChatRead:
                        await HandleChatReadAsync(packet.Payload, cancellationToken);
                        break;
                    case LanPacketTypes.ChatTyping:
                        await HandleChatTypingAsync(packet.Payload, cancellationToken);
                        break;
                    case LanPacketTypes.ChatEdit:
                        await HandleChatEditAsync(packet.Payload, remoteEndPoint, false, cancellationToken);
                        break;
                    case LanPacketTypes.ChatDelete:
                        await HandleChatDeleteAsync(packet.Payload, remoteEndPoint, false, cancellationToken);
                        break;
                    case LanPacketTypes.FileStart:
                        await HandleFileStartAsync(packet.Payload, remoteEndPoint, false, cancellationToken);
                        break;
                    case LanPacketTypes.FileChunk:
                        await HandleFileChunkAsync(packet.Payload, cancellationToken);
                        break;
                    case LanPacketTypes.NetPing:
                        await HandlePingAsync(packet.Payload, remoteEndPoint, false, cancellationToken);
                        break;
                    case LanPacketTypes.NetPong:
                        await HandlePongAsync(packet.Payload, cancellationToken);
                        break;
                    default:
                        break;
                }
            }
        }

        private async Task HandleChatMessageAsync(JsonElement payload, IPEndPoint? remoteEndPoint, bool viaRelay, CancellationToken cancellationToken)
        {
            LanChatMessage? message;
            try
            {
                message = payload.Deserialize<LanChatMessage>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (message == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(message.To))
            {
                return;
            }

            if (!viaRelay)
            {
                UpsertPeer(message.From, message.FromDisplayName, remoteEndPoint, message.ListenPort, "local");
            }
            else
            {
                UpsertPeer(message.From, message.FromDisplayName, _relayServerEndPoint, message.ListenPort, "relay");
            }
            var threadId = string.Equals(message.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : message.From;
            var text = await _cipher.DecryptAsync(message.CipherText, cancellationToken);
            MessageReceived?.Invoke(this, new LanMessageReceivedEventArgs(message.MessageId, message.From, message.FromDisplayName, threadId, text));

            if (!string.Equals(message.To, "all", StringComparison.OrdinalIgnoreCase))
            {
                await SendChatAckAsync(message, remoteEndPoint, viaRelay, cancellationToken);
            }
        }

        private async Task HandleChatAckAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanChatAck? ack;
            try
            {
                ack = payload.Deserialize<LanChatAck>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (ack == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(ack.To))
            {
                return;
            }

            ChatAckReceived?.Invoke(this, new LanChatAckReceivedEventArgs(ack.MessageId, ack.ThreadId, ack.Status));
            await Task.CompletedTask;
        }

        private async Task HandleChatReadAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanChatRead? read;
            try
            {
                read = payload.Deserialize<LanChatRead>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (read == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(read.To))
            {
                return;
            }

            ChatReadReceived?.Invoke(this, new LanChatReadReceivedEventArgs(read.ThreadId));
            await Task.CompletedTask;
        }

        private async Task HandleChatTypingAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanChatTyping? typing;
            try
            {
                typing = payload.Deserialize<LanChatTyping>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (typing == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(typing.To))
            {
                return;
            }

            TypingReceived?.Invoke(this, new LanTypingReceivedEventArgs(typing.ThreadId, typing.From, typing.IsTyping));
            await Task.CompletedTask;
        }

        private async Task HandleChatEditAsync(JsonElement payload, IPEndPoint? remoteEndPoint, bool viaRelay, CancellationToken cancellationToken)
        {
            LanChatEdit? edit;
            try
            {
                edit = payload.Deserialize<LanChatEdit>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (edit == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(edit.To))
            {
                return;
            }

            if (!viaRelay)
            {
                UpsertPeer(edit.From, edit.FromDisplayName, remoteEndPoint, 0, "local");
            }
            else
            {
                UpsertPeer(edit.From, edit.FromDisplayName, _relayServerEndPoint, _listenPort, "relay");
            }

            var threadId = string.Equals(edit.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : edit.From;
            var text = await _cipher.DecryptAsync(edit.CipherText, cancellationToken);
            ChatEditReceived?.Invoke(this, new LanChatEditReceivedEventArgs(edit.MessageId, edit.From, threadId, text));
        }

        private async Task HandleChatDeleteAsync(JsonElement payload, IPEndPoint? remoteEndPoint, bool viaRelay, CancellationToken cancellationToken)
        {
            LanChatDelete? deleted;
            try
            {
                deleted = payload.Deserialize<LanChatDelete>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (deleted == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(deleted.To))
            {
                return;
            }

            if (!viaRelay)
            {
                UpsertPeer(deleted.From, deleted.FromDisplayName, remoteEndPoint, 0, "local");
            }
            else
            {
                UpsertPeer(deleted.From, deleted.FromDisplayName, _relayServerEndPoint, _listenPort, "relay");
            }

            var threadId = string.Equals(deleted.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : deleted.From;
            ChatDeleteReceived?.Invoke(this, new LanChatDeleteReceivedEventArgs(deleted.MessageId, deleted.From, threadId));
            await Task.CompletedTask;
        }

        private async Task HandlePingAsync(
            JsonElement payload,
            IPEndPoint? remoteEndPoint,
            bool viaRelay,
            CancellationToken cancellationToken)
        {
            LanPing? ping;
            try
            {
                ping = payload.Deserialize<LanPing>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (ping == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(ping.To))
            {
                return;
            }

            if (!viaRelay)
            {
                UpsertPeer(ping.From, ping.FromDisplayName, remoteEndPoint, ping.ListenPort, "local");
            }
            else
            {
                UpsertPeer(ping.From, ping.FromDisplayName, _relayServerEndPoint, ping.ListenPort, "relay");
            }

            if (_user == null)
            {
                return;
            }

            var pong = new LanPong(ping.PingId, _user.Username, _user.DisplayName, ping.From, _listenPort);
            if (viaRelay)
            {
                await SendRelayPacketAsync(LanPacketTypes.NetPong, ping.From, pong, cancellationToken);
                return;
            }

            if (remoteEndPoint == null)
            {
                return;
            }

            var port = ping.ListenPort > 0 ? ping.ListenPort : remoteEndPoint.Port;
            var endPoint = new IPEndPoint(remoteEndPoint.Address, port);
            await SendPacketAsync(endPoint, LanPacketTypes.NetPong, pong, cancellationToken);
        }

        private async Task HandlePongAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanPong? pong;
            try
            {
                pong = payload.Deserialize<LanPong>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (pong == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(pong.To))
            {
                return;
            }

            if (!_pingTrackers.TryGetValue(pong.From, out var tracker))
            {
                tracker = new PingTracker();
                _pingTrackers[pong.From] = tracker;
            }

            if (tracker.TryComplete(pong.PingId, out var latencyMs))
            {
                RecordPingOutcome(pong.From, latencyMs, success: true);
            }

            await Task.CompletedTask;
        }

        private async Task HandlePeerHelloAsync(
            JsonElement payload,
            IPEndPoint? remoteEndPoint,
            StreamWriter writer,
            CancellationToken cancellationToken)
        {
            LanPeerHello? hello;
            try
            {
                hello = payload.Deserialize<LanPeerHello>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (hello == null)
            {
                return;
            }

            UpsertPeer(hello.Username, hello.DisplayName, remoteEndPoint, hello.ListenPort, "local");
            if (_user == null)
            {
                return;
            }

            var ackPayload = new LanPeerHello(_user.Username, _user.DisplayName, _listenPort);
            await WritePacketAsync(writer, LanPacketTypes.PeerHelloAck, ackPayload, cancellationToken);
        }

        private async Task HandlePeerHelloAckAsync(JsonElement payload, IPEndPoint? remoteEndPoint, CancellationToken cancellationToken)
        {
            LanPeerHello? hello;
            try
            {
                hello = payload.Deserialize<LanPeerHello>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (hello == null)
            {
                return;
            }

            UpsertPeer(hello.Username, hello.DisplayName, remoteEndPoint, hello.ListenPort, "local");
            await Task.CompletedTask;
        }

        private async Task HandleRelayPeerHelloAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanPeerHello? hello;
            try
            {
                hello = payload.Deserialize<LanPeerHello>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (hello == null)
            {
                return;
            }

            UpsertPeer(hello.Username, hello.DisplayName, _relayServerEndPoint, hello.ListenPort, "relay");
            await Task.CompletedTask;
        }

        private async Task HandleRelayPeerOfflineAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanPeerOffline? offline;
            try
            {
                offline = payload.Deserialize<LanPeerOffline>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (offline == null)
            {
                return;
            }

            MarkPeerOffline(offline.Username, offline.LastSeenAt);
            await Task.CompletedTask;
        }

        private async Task HandleFileStartAsync(JsonElement payload, IPEndPoint? remoteEndPoint, bool viaRelay, CancellationToken cancellationToken)
        {
            LanFileStart? start;
            try
            {
                start = payload.Deserialize<LanFileStart>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (start == null)
            {
                return;
            }

            if (!IsMessageForLocalUser(start.To))
            {
                return;
            }

            if (!viaRelay)
            {
                UpsertPeer(start.From, start.FromDisplayName, remoteEndPoint, start.FromPort, "local");
            }
            else
            {
                UpsertPeer(start.From, start.FromDisplayName, _relayServerEndPoint, start.FromPort, "relay");
            }
            var fileName = await _cipher.DecryptAsync(start.FileNameCipher, cancellationToken);
            var safeName = SanitizeFileName(fileName);
            var filePath = Path.Combine(AppPaths.ReceivedFilesPath, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{safeName}");
            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            var session = new FileReceiveSession(start, fileName, filePath, stream);
            _incomingFiles[start.FileId] = session;

            var threadId = string.Equals(start.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : start.From;
            FileTransferStarted?.Invoke(
                this,
                new FileTransferStartedEventArgs(
                    threadId,
                    start.FileId,
                    start.From,
                    start.FromDisplayName,
                    fileName,
                    start.SizeBytes));
        }

        private async Task HandleFileChunkAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            LanFileChunk? chunk;
            try
            {
                chunk = payload.Deserialize<LanFileChunk>(_jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (chunk == null)
            {
                return;
            }

            if (!_incomingFiles.TryGetValue(chunk.FileId, out var session))
            {
                return;
            }

            var base64 = await _cipher.DecryptToBase64Async(chunk.DataCipher, cancellationToken);
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                await FinalizeFailedTransferAsync(session, "Dosya bozuk.");
                return;
            }

            await session.Stream.WriteAsync(bytes, cancellationToken);
            session.Hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
            session.BytesWritten += bytes.Length;
            var progress = session.Start.SizeBytes > 0
                ? Math.Min(100, (session.BytesWritten * 100d) / session.Start.SizeBytes)
                : 0;
            var progressThreadId = string.Equals(session.Start.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : session.Start.From;
            if (ShouldReportProgress(session.Start.FileId, progress))
            {
                FileTransferProgress?.Invoke(
                    this,
                    new FileTransferProgressEventArgs(progressThreadId, session.Start.FileId, progress, false));
            }

            if (chunk.IsLast || session.BytesWritten >= session.Start.SizeBytes)
            {
                session.Hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                await session.Stream.FlushAsync(cancellationToken);
                session.Stream.Dispose();
                _incomingFiles.TryRemove(chunk.FileId, out _);
                var scan = await _scanService.ScanAsync(
                    new FileScanRequest(session.FileName, session.Start.SizeBytes, session.Start.ContentType),
                    cancellationToken);
                var actualHash = session.Hash.Hash == null
                    ? string.Empty
                    : Convert.ToBase64String(session.Hash.Hash);
                session.Hash.Dispose();
                if (!string.IsNullOrWhiteSpace(session.Start.Sha256Base64) &&
                    !string.Equals(session.Start.Sha256Base64, actualHash, StringComparison.Ordinal))
                {
                    scan = new FileScanResult("error", "Dosya butunlugu dogrulanamadi.", actualHash);
                }
                var threadId = string.Equals(session.Start.To, "all", StringComparison.OrdinalIgnoreCase)
                    ? "all"
                    : session.Start.From;
                FileReceived?.Invoke(
                    this,
                    new LanFileReceivedEventArgs(
                        session.Start.From,
                        session.Start.FromDisplayName,
                        threadId,
                        session.Start.FileId,
                        session.FileName,
                        session.FilePath,
                        session.Start.SizeBytes,
                        session.Start.ContentType,
                        scan));
                ClearProgressState(session.Start.FileId);
            }
        }

        private async Task FinalizeFailedTransferAsync(FileReceiveSession session, string message)
        {
            try
            {
                session.Stream.Dispose();
            }
            catch (IOException)
            {
            }

            session.Hash.Dispose();

            _incomingFiles.TryRemove(session.Start.FileId, out _);
            try
            {
                if (File.Exists(session.FilePath))
                {
                    File.Delete(session.FilePath);
                }
            }
            catch (IOException)
            {
            }

            var result = new FileScanResult("error", message, null);
            var threadId = string.Equals(session.Start.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : session.Start.From;
            FileReceived?.Invoke(
                this,
                new LanFileReceivedEventArgs(
                    session.Start.From,
                    session.Start.FromDisplayName,
                    threadId,
                    session.Start.FileId,
                    session.FileName,
                    session.FilePath,
                    session.Start.SizeBytes,
                    session.Start.ContentType,
                    result));
            ClearProgressState(session.Start.FileId);
        }

        private async Task WritePacketAsync(StreamWriter writer, string type, object payload, CancellationToken cancellationToken)
        {
            var packet = new LanPacket(type, JsonSerializer.SerializeToElement(payload, _jsonOptions));
            var json = JsonSerializer.Serialize(packet, _jsonOptions);
            await writer.WriteLineAsync(json);
        }

        private async Task<LanPeerHello?> SendPeerHelloAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return null;
            }

            using var client = new TcpClient();
            await client.ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
            await using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var payload = new LanPeerHello(_user.Username, _user.DisplayName, _listenPort);
            await WritePacketAsync(writer, LanPacketTypes.PeerHello, payload, cancellationToken);

            var readTask = reader.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
            if (completed != readTask)
            {
                return null;
            }

            var line = await readTask;
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            LanPacket? packet;
            try
            {
                packet = JsonSerializer.Deserialize<LanPacket>(line, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (packet == null || packet.Type != LanPacketTypes.PeerHelloAck)
            {
                return null;
            }

            try
            {
                return packet.Payload.Deserialize<LanPeerHello>(_jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task SendChatAckAsync(LanChatMessage message, IPEndPoint? remoteEndPoint, bool viaRelay, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message.MessageId))
            {
                return;
            }

            var ack = new LanChatAck(message.MessageId, _user.Username, message.From, message.ThreadId, "delivered");

            if (viaRelay)
            {
                await SendRelayPacketAsync(LanPacketTypes.ChatAck, message.From, ack, cancellationToken);
                return;
            }

            if (remoteEndPoint == null)
            {
                return;
            }

            var port = message.ListenPort > 0 ? message.ListenPort : remoteEndPoint.Port;
            var endPoint = new IPEndPoint(remoteEndPoint.Address, port);
            await SendPacketAsync(endPoint, LanPacketTypes.ChatAck, ack, cancellationToken);
        }

        public async Task SendReadReceiptAsync(string targetUser, string threadId, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return;
            }

            if (string.Equals(threadId, "all", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_peers.TryGetValue(targetUser, out var peer) || !peer.IsOnline)
            {
                return;
            }

            var read = new LanChatRead(_user.Username, targetUser, threadId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (ShouldUseRelay(targetUser, peer))
            {
                await SendRelayPacketAsync(LanPacketTypes.ChatRead, targetUser, read, cancellationToken);
                return;
            }

            if (string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatRead, read, cancellationToken);
        }

        public async Task SendTypingAsync(string targetUser, string threadId, bool isTyping, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return;
            }

            if (string.Equals(threadId, "all", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_peers.TryGetValue(targetUser, out var peer) || !peer.IsOnline)
            {
                return;
            }

            var payload = new LanChatTyping(_user.Username, targetUser, threadId, isTyping);
            if (ShouldUseRelay(targetUser, peer))
            {
                await SendRelayPacketAsync(LanPacketTypes.ChatTyping, targetUser, payload, cancellationToken);
                return;
            }

            if (string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatTyping, payload, cancellationToken);
        }

        public async Task<bool> SendChatEditAsync(string targetUser, string threadId, string messageId, string newText, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return false;
            }

            var cipherText = await _cipher.EncryptAsync(newText, cancellationToken);
            var payload = new LanChatEdit(
                messageId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                threadId,
                cipherText,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (UseRelayForAll())
                {
                    await SendRelayPacketAsync(LanPacketTypes.ChatEdit, "all", payload, cancellationToken);
                    return true;
                }

                return await BroadcastPacketAsync(LanPacketTypes.ChatEdit, payload, cancellationToken);
            }

            if (!_peers.TryGetValue(targetUser, out var peer))
            {
                return false;
            }

            if (ShouldUseRelay(targetUser, peer))
            {
                await SendRelayPacketAsync(LanPacketTypes.ChatEdit, targetUser, payload, cancellationToken);
                return true;
            }

            await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatEdit, payload, cancellationToken);
            return true;
        }

        public async Task<bool> SendChatDeleteAsync(string targetUser, string threadId, string messageId, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return false;
            }

            var payload = new LanChatDelete(
                messageId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                threadId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (UseRelayForAll())
                {
                    await SendRelayPacketAsync(LanPacketTypes.ChatDelete, "all", payload, cancellationToken);
                    return true;
                }

                return await BroadcastPacketAsync(LanPacketTypes.ChatDelete, payload, cancellationToken);
            }

            if (!_peers.TryGetValue(targetUser, out var peer))
            {
                return false;
            }

            if (ShouldUseRelay(targetUser, peer))
            {
                await SendRelayPacketAsync(LanPacketTypes.ChatDelete, targetUser, payload, cancellationToken);
                return true;
            }

            await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatDelete, payload, cancellationToken);
            return true;
        }

        private void UpsertPeer(string username, string displayName, IPEndPoint? remoteEndPoint, int listenPort, string source)
        {
            if (_user == null)
            {
                return;
            }

            if (string.Equals(username, _user.Username, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var endPoint = ResolvePeerEndPoint(remoteEndPoint, listenPort, source);
            if (endPoint == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var updated = new PeerInfo(username, displayName, endPoint, now, true, source);

            var shouldRaise = false;
            _peers.AddOrUpdate(
                updated.Username,
                _ =>
                {
                    shouldRaise = true;
                    return updated;
                },
                (_, existing) =>
                {
                    if (!existing.IsOnline ||
                        !string.Equals(existing.DisplayName, updated.DisplayName, StringComparison.Ordinal) ||
                        !Equals(existing.EndPoint, updated.EndPoint) ||
                        !string.Equals(existing.Source, updated.Source, StringComparison.Ordinal))
                    {
                        shouldRaise = true;
                    }

                    return updated with { LastSeen = now, IsOnline = true, Source = updated.Source };
                });

            if (shouldRaise)
            {
                PeerChanged?.Invoke(this, new PeerChangedEventArgs(updated));
            }
        }

        private IPEndPoint? ResolvePeerEndPoint(IPEndPoint? remoteEndPoint, int listenPort, string source)
        {
            if (remoteEndPoint != null)
            {
                var port = listenPort > 0 ? listenPort : remoteEndPoint.Port;
                return new IPEndPoint(remoteEndPoint.Address, port);
            }

            if (string.Equals(source, "relay", StringComparison.OrdinalIgnoreCase))
            {
                if (_relayServerEndPoint != null)
                {
                    return _relayServerEndPoint;
                }

                return new IPEndPoint(IPAddress.Loopback, 0);
            }

            return null;
        }

        private bool ShouldUseRelay(string targetUser, PeerInfo peer)
        {
            if (!_relayConnected)
            {
                return false;
            }

            if (string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _relayEnabled && string.Equals(_relayMode, "relay", StringComparison.OrdinalIgnoreCase);
        }

        private bool UseRelayForAll()
        {
            if (!_relayConnected)
            {
                return false;
            }

            return _relayEnabled && string.Equals(_relayMode, "relay", StringComparison.OrdinalIgnoreCase);
        }

        private void MarkRelayPeersOffline()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _peers)
            {
                var peer = entry.Value;
                if (!peer.IsOnline)
                {
                    continue;
                }

                if (!string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var updated = peer with { IsOnline = false, LastSeen = now };
                _peers[entry.Key] = updated;
                PeerChanged?.Invoke(this, new PeerChangedEventArgs(updated));
                ClearPingState(entry.Key);
            }
        }

        private void MarkPeerOffline(string username, long lastSeenAt)
        {
            if (!_peers.TryGetValue(username, out var peer))
            {
                return;
            }

            DateTimeOffset lastSeen;
            try
            {
                lastSeen = DateTimeOffset.FromUnixTimeMilliseconds(lastSeenAt);
            }
            catch (ArgumentOutOfRangeException)
            {
                lastSeen = DateTimeOffset.UtcNow;
            }

            var updated = peer with { IsOnline = false, LastSeen = lastSeen };
            _peers[username] = updated;
            PeerChanged?.Invoke(this, new PeerChangedEventArgs(updated));
            ClearPingState(username);
        }

        private void ClearPingState(string username)
        {
            if (_pingTrackers.TryRemove(username, out _))
            {
                ConnectionQualityUpdated?.Invoke(this, new ConnectionQualityEventArgs(username, null, null));
            }
        }

        private bool ShouldReportProgress(string fileId, double progress)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_progressStates.TryGetValue(fileId, out var state))
            {
                _progressStates[fileId] = new ProgressState(progress, now);
                return true;
            }

            if (progress >= 100 || progress - state.Progress >= 1 || now - state.UpdatedAt >= TimeSpan.FromMilliseconds(250))
            {
                _progressStates[fileId] = new ProgressState(progress, now);
                return true;
            }

            return false;
        }

        private void ClearProgressState(string fileId)
        {
            _progressStates.TryRemove(fileId, out _);
        }

        private async Task SendPingBatchAsync(CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return;
            }

            foreach (var peer in _peers.Values)
            {
                if (!peer.IsOnline)
                {
                    continue;
                }

                if (string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase) && !_relayConnected)
                {
                    continue;
                }

                await SendPingToPeerAsync(peer, cancellationToken);
            }
        }

        private async Task SendPingToPeerAsync(PeerInfo peer, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return;
            }

            var pingId = Guid.NewGuid().ToString("N");
            var tracker = _pingTrackers.GetOrAdd(peer.Username, _ => new PingTracker());
            tracker.TrackPending(pingId);

            var payload = new LanPing(pingId, _user.Username, _user.DisplayName, peer.Username, _listenPort);
            try
            {
                if (ShouldUseRelay(peer.Username, peer))
                {
                    await SendRelayPacketAsync(LanPacketTypes.NetPing, peer.Username, payload, cancellationToken);
                    return;
                }

                await SendPacketAsync(peer.EndPoint, LanPacketTypes.NetPing, payload, cancellationToken);
            }
            catch (SocketException)
            {
                RecordPingOutcome(peer.Username, null, false);
            }
            catch (IOException)
            {
                RecordPingOutcome(peer.Username, null, false);
            }
        }

        private void CleanupPingTimeouts()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _pingTrackers)
            {
                foreach (var pending in entry.Value.Pending)
                {
                    if (now - pending.Value < PingTimeout)
                    {
                        continue;
                    }

                    if (entry.Value.Pending.TryRemove(pending.Key, out _))
                    {
                        RecordPingOutcome(entry.Key, null, false);
                    }
                }
            }
        }

        private void RecordPingOutcome(string username, double? latencyMs, bool success)
        {
            if (!_pingTrackers.TryGetValue(username, out var tracker))
            {
                return;
            }

            double? pingMs;
            double? lossPercent;
            lock (tracker.Sync)
            {
                if (tracker.Outcomes.Count >= PingWindowSize)
                {
                    tracker.Outcomes.Dequeue();
                }

                tracker.Outcomes.Enqueue(new PingOutcome(success, latencyMs));
                if (success && latencyMs.HasValue)
                {
                    tracker.LastRttMs = latencyMs.Value;
                }

                pingMs = tracker.LastRttMs.HasValue ? Math.Round(tracker.LastRttMs.Value, 1) : null;
                if (tracker.Outcomes.Count == 0)
                {
                    lossPercent = null;
                }
                else
                {
                    var failures = 0;
                    foreach (var outcome in tracker.Outcomes)
                    {
                        if (!outcome.Success)
                        {
                            failures += 1;
                        }
                    }

                    lossPercent = Math.Round(failures * 100d / tracker.Outcomes.Count, 1);
                }
            }

            ConnectionQualityUpdated?.Invoke(this, new ConnectionQualityEventArgs(username, pingMs, lossPercent));
        }

        private async Task SendPacketAsync(IPEndPoint endPoint, string type, object payload, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
            await using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            var packet = new LanPacket(type, JsonSerializer.SerializeToElement(payload, _jsonOptions));
            var json = JsonSerializer.Serialize(packet, _jsonOptions);
            await writer.WriteLineAsync(json);
        }

        private async Task<LanSendResult> SendFileStreamAsync(
            string targetUser,
            string fileName,
            long sizeBytes,
            Stream stream,
            string? contentType,
            string messageId,
            CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return new LanSendResult(false, 0, "Oturum bulunamadi.");
            }

            var fileId = string.IsNullOrWhiteSpace(messageId)
                ? Guid.NewGuid().ToString("N")
                : messageId;

            if (string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (UseRelayForAll())
                {
                    var sent = await SendFileViaRelayAsync("all", fileName, sizeBytes, stream, contentType, fileId, cancellationToken);
                    return new LanSendResult(sent, sent ? 1 : 0, sent ? "Gonderildi." : "Relay baglantisi yok.");
                }

                return await BroadcastFileStreamAsync(fileName, sizeBytes, stream, contentType, fileId, cancellationToken);
            }

            if (!_peers.TryGetValue(targetUser, out var peer))
            {
                return new LanSendResult(false, 0, "Kisi agda bulunamadi.");
            }

            if (!peer.IsOnline && DateTimeOffset.UtcNow - peer.LastSeen > TimeSpan.FromMinutes(2))
            {
                return new LanSendResult(false, 0, "Kisi agda bulunamadi.");
            }

            if (string.Equals(peer.Source, "relay", StringComparison.OrdinalIgnoreCase) && !_relayConnected)
            {
                return new LanSendResult(false, 0, "Relay baglantisi yok.");
            }

            var sha256 = await ComputeSha256Base64Async(stream, cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            if (ShouldUseRelay(targetUser, peer))
            {
                var sent = await SendFileViaRelayAsync(targetUser, fileName, sizeBytes, stream, contentType, fileId, cancellationToken, sha256);
                return new LanSendResult(sent, sent ? 1 : 0, sent ? "Gonderildi." : "Relay baglantisi yok.");
            }

            await SendFileToPeerAsync(peer.EndPoint, targetUser, fileName, sizeBytes, stream, contentType, sha256, fileId, cancellationToken);
            return new LanSendResult(true, 1, "Gonderildi.");
        }

        private async Task SendFileToPeerAsync(
            IPEndPoint endPoint,
            string targetUser,
            string fileName,
            long sizeBytes,
            Stream stream,
            string? contentType,
            string? sha256Base64,
            string fileId,
            CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return;
            }

            using var client = new TcpClient();
            await client.ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
            await using var networkStream = client.GetStream();
            using var writer = new StreamWriter(networkStream, Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            var nameCipher = await _cipher.EncryptAsync(fileName, cancellationToken);
            var startPayload = new LanFileStart(
                fileId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                nameCipher,
                sizeBytes,
                contentType,
                sha256Base64,
                _listenPort);
            var startPacket = new LanPacket(LanPacketTypes.FileStart, JsonSerializer.SerializeToElement(startPayload, _jsonOptions));
            await writer.WriteLineAsync(JsonSerializer.Serialize(startPacket, _jsonOptions));

            var buffer = new byte[ChunkSize];
            var index = 0;
            long sentBytes = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                sentBytes += read;
                var base64 = Convert.ToBase64String(buffer, 0, read);
                var cipher = await _cipher.EncryptBase64PayloadAsync(base64, cancellationToken);
                var isLast = sentBytes >= sizeBytes;
                var chunkPayload = new LanFileChunk(fileId, index, cipher, isLast);
                var chunkPacket = new LanPacket(LanPacketTypes.FileChunk, JsonSerializer.SerializeToElement(chunkPayload, _jsonOptions));
                await writer.WriteLineAsync(JsonSerializer.Serialize(chunkPacket, _jsonOptions));
                var progress = sizeBytes > 0 ? Math.Min(100, (sentBytes * 100d) / sizeBytes) : 0;
                var threadId = string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase)
                    ? "all"
                    : targetUser;
                if (ShouldReportProgress(fileId, progress))
                {
                    FileTransferProgress?.Invoke(
                        this,
                        new FileTransferProgressEventArgs(threadId, fileId, progress, true));
                }
                index += 1;
            }

            ClearProgressState(fileId);
        }

        private async Task<LanSendResult> BroadcastMessageAsync(string text, CancellationToken cancellationToken)
        {
            var peers = _peers.Values;
            if (peers.Count == 0)
            {
                return new LanSendResult(false, 0, "Agda aktif kisi yok.");
            }

            var sent = 0;
            foreach (var peer in peers)
            {
                if (!peer.IsOnline)
                {
                    continue;
                }

                try
                {
                    var cipherText = await _cipher.EncryptAsync(text, cancellationToken);
                    var payload = new LanChatMessage(
                        Guid.NewGuid().ToString("N"),
                        _user!.Username,
                        _user.DisplayName,
                        "all",
                        "all",
                        cipherText,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        _listenPort);
                    await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatMessage, payload, cancellationToken);
                    sent += 1;
                }
                catch (SocketException)
                {
                }
                catch (IOException)
                {
                }
            }

            var message = sent == 0 ? "Agda aktif kisi yok." : "Gonderildi.";
            return new LanSendResult(sent > 0, sent, message);
        }

        private async Task<bool> BroadcastPacketAsync(string type, object payload, CancellationToken cancellationToken)
        {
            var peers = _peers.Values;
            if (peers.Count == 0)
            {
                return false;
            }

            var sent = 0;
            foreach (var peer in peers)
            {
                if (!peer.IsOnline)
                {
                    continue;
                }

                try
                {
                    await SendPacketAsync(peer.EndPoint, type, payload, cancellationToken);
                    sent += 1;
                }
                catch (SocketException)
                {
                }
                catch (IOException)
                {
                }
            }

            return sent > 0;
        }

        private async Task<LanSendResult> BroadcastFileStreamAsync(
            string fileName,
            long sizeBytes,
            Stream stream,
            string? contentType,
            string fileId,
            CancellationToken cancellationToken)
        {
            var peers = _peers.Values;
            if (peers.Count == 0)
            {
                return new LanSendResult(false, 0, "Agda aktif kisi yok.");
            }

            var sha256 = await ComputeSha256Base64Async(stream, cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var sent = 0;
            foreach (var peer in peers)
            {
                if (!peer.IsOnline)
                {
                    continue;
                }

                try
                {
                    if (sent > 0 && stream.CanSeek)
                    {
                        stream.Position = 0;
                    }
                    else if (sent > 0 && !stream.CanSeek)
                    {
                        break;
                    }

                    await SendFileToPeerAsync(peer.EndPoint, "all", fileName, sizeBytes, stream, contentType, sha256, fileId, cancellationToken);
                    sent += 1;
                }
                catch (SocketException)
                {
                }
                catch (IOException)
                {
                }
            }

            var message = sent == 0 ? "Agda aktif kisi yok." : "Gonderildi.";
            return new LanSendResult(sent > 0, sent, message);
        }

        private static async Task<string?> ComputeSha256Base64Async(Stream stream, CancellationToken cancellationToken)
        {
            if (!stream.CanSeek)
            {
                return null;
            }

            stream.Position = 0;
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, cancellationToken);
            stream.Position = 0;
            return Convert.ToBase64String(hash);
        }

        private bool IsMessageForLocalUser(string to)
        {
            if (_user == null)
            {
                return false;
            }

            if (string.Equals(to, "all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(to, _user.Username, StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string name)
        {
            var safe = Path.GetFileName(name);
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(ch, '_');
            }
            return string.IsNullOrWhiteSpace(safe) ? "dosya.bin" : safe;
        }

        private sealed class PingTracker
        {
            public ConcurrentDictionary<string, DateTimeOffset> Pending { get; } = new();
            public Queue<PingOutcome> Outcomes { get; } = new();
            public object Sync { get; } = new();
            public double? LastRttMs { get; set; }

            public void TrackPending(string pingId)
            {
                Pending[pingId] = DateTimeOffset.UtcNow;
            }

            public bool TryComplete(string pingId, out double latencyMs)
            {
                if (Pending.TryRemove(pingId, out var sentAt))
                {
                    latencyMs = (DateTimeOffset.UtcNow - sentAt).TotalMilliseconds;
                    return true;
                }

                latencyMs = 0;
                return false;
            }
        }

        private sealed record PingOutcome(bool Success, double? LatencyMs);

        private sealed record ProgressState(double Progress, DateTimeOffset UpdatedAt);

        private sealed class RelayClientSession
        {
            public RelayClientSession(TcpClient client, StreamWriter writer, string username, string displayName)
            {
                Client = client;
                Writer = writer;
                Username = username;
                DisplayName = displayName;
            }

            public TcpClient Client { get; }
            public StreamWriter Writer { get; }
            public string Username { get; }
            public string DisplayName { get; }
            public SemaphoreSlim SendLock { get; } = new(1, 1);
        }

        private sealed class FileReceiveSession
        {
            public FileReceiveSession(LanFileStart start, string fileName, string filePath, FileStream stream)
            {
                Start = start;
                FileName = fileName;
                FilePath = filePath;
                Stream = stream;
                Hash = SHA256.Create();
            }

            public LanFileStart Start { get; }
            public string FileName { get; }
            public string FilePath { get; }
            public FileStream Stream { get; }
            public HashAlgorithm Hash { get; }
            public long BytesWritten { get; set; }
        }
    }
}
