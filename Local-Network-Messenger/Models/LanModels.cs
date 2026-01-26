using System.Text.Json;

namespace Local_Network_Messenger.Models
{
    public static class LanPacketTypes
    {
        public const string ChatMessage = "chat.message";
        public const string FileStart = "chat.file.start";
        public const string FileChunk = "chat.file.chunk";
    }

    public sealed record LanPacket(string Type, JsonElement Payload);

    public sealed record LanPresenceAnnouncement(
        string InstanceId,
        string Username,
        string DisplayName,
        int TcpPort,
        long Timestamp);

    public sealed record LanChatMessage(
        string From,
        string FromDisplayName,
        string To,
        string ThreadId,
        string CipherText,
        long SentAt);

    public sealed record LanFileStart(
        string FileId,
        string From,
        string FromDisplayName,
        string To,
        string FileNameCipher,
        long SizeBytes,
        string? ContentType,
        string? Sha256Base64);

    public sealed record LanFileChunk(
        string FileId,
        int Index,
        string DataCipher,
        bool IsLast);
}
