using System.Text.Json;

namespace Local_Network_Messenger.Models
{
    public static class LanPacketTypes
    {
        public const string ChatMessage = "chat.message";
        public const string ChatAck = "chat.ack";
        public const string ChatRead = "chat.read";
        public const string ChatTyping = "chat.typing";
        public const string PeerHello = "peer.hello";
        public const string PeerHelloAck = "peer.hello.ack";
        public const string PeerOffline = "peer.offline";
        public const string FileStart = "chat.file.start";
        public const string FileChunk = "chat.file.chunk";
    }

    public static class RelayPacketTypes
    {
        public const string Register = "relay.register";
    }

    public sealed record LanPacket(string Type, JsonElement Payload);

    public sealed record RelayPacket(string Type, string To, JsonElement Payload);

    public sealed record RelayRegister(string Username, string DisplayName);

    public sealed record LanPresenceAnnouncement(
        string InstanceId,
        string Username,
        string DisplayName,
        int TcpPort,
        long Timestamp);

    public sealed record LanPeerHello(string Username, string DisplayName, int ListenPort);

    public sealed record LanPeerOffline(string Username, long LastSeenAt);

    public sealed record LanChatMessage(
        string MessageId,
        string From,
        string FromDisplayName,
        string To,
        string ThreadId,
        string CipherText,
        long SentAt,
        int ListenPort);

    public sealed record LanChatAck(
        string MessageId,
        string From,
        string To,
        string ThreadId,
        string Status);

    public sealed record LanChatRead(
        string From,
        string To,
        string ThreadId,
        long ReadAt);

    public sealed record LanChatTyping(
        string From,
        string To,
        string ThreadId,
        bool IsTyping);

    public sealed record LanFileStart(
        string FileId,
        string From,
        string FromDisplayName,
        string To,
        string FileNameCipher,
        long SizeBytes,
        string? ContentType,
        string? Sha256Base64,
        int FromPort);

    public sealed record LanFileChunk(
        string FileId,
        int Index,
        string DataCipher,
        bool IsLast);
}
