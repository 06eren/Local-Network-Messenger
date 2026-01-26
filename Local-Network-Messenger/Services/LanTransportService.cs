using System;
using System.Collections.Concurrent;
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
        bool IsOnline);

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
        public LanMessageReceivedEventArgs(string from, string fromDisplayName, string threadId, string text)
        {
            From = from;
            FromDisplayName = fromDisplayName;
            ThreadId = threadId;
            Text = text;
        }

        public string From { get; }
        public string FromDisplayName { get; }
        public string ThreadId { get; }
        public string Text { get; }
    }

    public sealed class LanFileReceivedEventArgs : EventArgs
    {
        public LanFileReceivedEventArgs(
            string from,
            string fromDisplayName,
            string threadId,
            string fileName,
            string filePath,
            long sizeBytes,
            FileScanResult scanResult)
        {
            From = from;
            FromDisplayName = fromDisplayName;
            ThreadId = threadId;
            FileName = fileName;
            FilePath = filePath;
            SizeBytes = sizeBytes;
            ScanResult = scanResult;
        }

        public string From { get; }
        public string FromDisplayName { get; }
        public string ThreadId { get; }
        public string FileName { get; }
        public string FilePath { get; }
        public long SizeBytes { get; }
        public FileScanResult ScanResult { get; }
    }

    public sealed class LanTransportService : IAsyncDisposable
    {
        private const int ChunkSize = 64 * 1024;
        private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);
        private readonly MessageCipher _cipher;
        private readonly IFileScanService _scanService;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly ConcurrentDictionary<string, PeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FileReceiveSession> _incomingFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private readonly int _discoveryPort;
        private int _listenPort;
        private UdpClient? _udpClient;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _udpReceiveTask;
        private Task? _announceTask;
        private Task? _cleanupTask;
        private Task? _acceptTask;
        private UserProfile? _user;

        public LanTransportService(MessageCipher cipher, IFileScanService scanService, int discoveryPort, int listenPort)
        {
            _cipher = cipher;
            _scanService = scanService;
            _discoveryPort = discoveryPort;
            _listenPort = listenPort;
        }

        public event EventHandler<PeerChangedEventArgs>? PeerChanged;
        public event EventHandler<LanMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<LanFileReceivedEventArgs>? FileReceived;

        public int ListenPort => _listenPort;

        public IEnumerable<PeerInfo> Peers => _peers.Values;

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

            var tasks = new[] { _udpReceiveTask, _announceTask, _cleanupTask, _acceptTask };
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
            _acceptTask = null;
            _udpClient = null;
            _listener = null;
            _peers.Clear();
            _incomingFiles.Clear();
        }

        public async Task<LanSendResult> SendMessageAsync(string targetUser, string text, CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return new LanSendResult(false, 0, "Oturum bulunamadi.");
            }

            if (string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                return await BroadcastMessageAsync(text, cancellationToken);
            }

            if (!_peers.TryGetValue(targetUser, out var peer) || !peer.IsOnline)
            {
                return new LanSendResult(false, 0, "Kisi agda bulunamadi.");
            }

            var cipherText = await _cipher.EncryptAsync(text, cancellationToken);
            var payload = new LanChatMessage(
                _user.Username,
                _user.DisplayName,
                targetUser,
                _user.Username,
                cipherText,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await SendPacketAsync(peer.EndPoint, LanPacketTypes.ChatMessage, payload, cancellationToken);
            return new LanSendResult(true, 1, "Gonderildi.");
        }

        public async Task<LanSendResult> SendFileAsync(
            string targetUser,
            string fileName,
            byte[] data,
            string? contentType,
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
                cancellationToken);
        }

        public async Task<LanSendResult> SendFileFromPathAsync(
            string targetUser,
            string filePath,
            string? contentType,
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
                var updated = new PeerInfo(announcement.Username, announcement.DisplayName, endPoint, now, true);
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
                            !Equals(existing.EndPoint, updated.EndPoint))
                        {
                            shouldRaise = true;
                        }

                        return updated with { LastSeen = now, IsOnline = true };
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

                    if (now - peer.LastSeen < PresenceTimeout)
                    {
                        continue;
                    }

                    var updated = peer with { IsOnline = false };
                    _peers[entry.Key] = updated;
                    PeerChanged?.Invoke(this, new PeerChangedEventArgs(updated));
                }

                await Task.Delay(CleanupInterval, cancellationToken);
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
            using var _ = client;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);

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
                    case LanPacketTypes.ChatMessage:
                        await HandleChatMessageAsync(packet.Payload, cancellationToken);
                        break;
                    case LanPacketTypes.FileStart:
                        await HandleFileStartAsync(packet.Payload, cancellationToken);
                        break;
                    case LanPacketTypes.FileChunk:
                        await HandleFileChunkAsync(packet.Payload, cancellationToken);
                        break;
                    default:
                        break;
                }
            }
        }

        private async Task HandleChatMessageAsync(JsonElement payload, CancellationToken cancellationToken)
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

            var threadId = string.Equals(message.To, "all", StringComparison.OrdinalIgnoreCase)
                ? "all"
                : message.From;
            var text = await _cipher.DecryptAsync(message.CipherText, cancellationToken);
            MessageReceived?.Invoke(this, new LanMessageReceivedEventArgs(message.From, message.FromDisplayName, threadId, text));
        }

        private async Task HandleFileStartAsync(JsonElement payload, CancellationToken cancellationToken)
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

            var fileName = await _cipher.DecryptAsync(start.FileNameCipher, cancellationToken);
            var safeName = SanitizeFileName(fileName);
            var filePath = Path.Combine(AppPaths.ReceivedFilesPath, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{safeName}");
            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            var session = new FileReceiveSession(start, fileName, filePath, stream);
            _incomingFiles[start.FileId] = session;
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

            var base64 = await _cipher.DecryptAsync(chunk.DataCipher, cancellationToken);
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
                        session.FileName,
                        session.FilePath,
                        session.Start.SizeBytes,
                        scan));
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
                    session.FileName,
                    session.FilePath,
                    session.Start.SizeBytes,
                    result));
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
            CancellationToken cancellationToken)
        {
            if (_user == null)
            {
                return new LanSendResult(false, 0, "Oturum bulunamadi.");
            }

            if (string.Equals(targetUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                return await BroadcastFileStreamAsync(fileName, sizeBytes, stream, contentType, cancellationToken);
            }

            if (!_peers.TryGetValue(targetUser, out var peer) || !peer.IsOnline)
            {
                return new LanSendResult(false, 0, "Kisi agda bulunamadi.");
            }

            var sha256 = await ComputeSha256Base64Async(stream, cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            await SendFileToPeerAsync(peer.EndPoint, targetUser, fileName, sizeBytes, stream, contentType, sha256, cancellationToken);
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

            var fileId = Guid.NewGuid().ToString("N");
            var nameCipher = await _cipher.EncryptAsync(fileName, cancellationToken);
            var startPayload = new LanFileStart(
                fileId,
                _user.Username,
                _user.DisplayName,
                targetUser,
                nameCipher,
                sizeBytes,
                contentType,
                sha256Base64);
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
                var cipher = await _cipher.EncryptAsync(base64, cancellationToken);
                var isLast = sentBytes >= sizeBytes;
                var chunkPayload = new LanFileChunk(fileId, index, cipher, isLast);
                var chunkPacket = new LanPacket(LanPacketTypes.FileChunk, JsonSerializer.SerializeToElement(chunkPayload, _jsonOptions));
                await writer.WriteLineAsync(JsonSerializer.Serialize(chunkPacket, _jsonOptions));
                index += 1;
            }
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
                        _user!.Username,
                        _user.DisplayName,
                        "all",
                        "all",
                        cipherText,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        private async Task<LanSendResult> BroadcastFileStreamAsync(
            string fileName,
            long sizeBytes,
            Stream stream,
            string? contentType,
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

                    await SendFileToPeerAsync(peer.EndPoint, "all", fileName, sizeBytes, stream, contentType, sha256, cancellationToken);
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
