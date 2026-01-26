using System;
using System.Collections.Generic;

namespace Local_Network_Messenger.Models
{
    public sealed record ContactDto(
        string Id,
        string DisplayName,
        string Status,
        string Preview,
        bool IsOnline,
        int UnreadCount,
        bool IsTyping);

    public sealed record ChatAttachmentDto(
        string FileName,
        long SizeBytes,
        string Status,
        double? Progress,
        string? TransferState);

    public sealed record ChatMessageDto(
        string Id,
        string ThreadId,
        string Sender,
        bool IsMine,
        string Text,
        DateTimeOffset SentAt,
        ChatAttachmentDto? Attachment,
        string? DeliveryState);

    public sealed record ChatSnapshot(
        UserProfile CurrentUser,
        IReadOnlyList<ContactDto> Contacts,
        IReadOnlyDictionary<string, IReadOnlyList<ChatMessageDto>> Threads,
        string ActiveContactId);

    public sealed record ChatSendRequest(string ThreadId, string Text);

    public sealed record ChatAttachRequest(string ThreadId, string FileName, long SizeBytes, string? ContentType, string? DataBase64);

    public sealed record ChatPickFileRequest(string ThreadId);

    public sealed record ChatActiveRequest(string ThreadId);

    public sealed record ChatTypingRequest(string ThreadId, bool IsTyping);

    public sealed record NetworkKeyRequest(string NetworkKey);

    public sealed record ManualPeerRequest(string Endpoint);

    public sealed record RelayConfigRequest(
        string Host,
        string Mode,
        bool RelayEnabled,
        bool RelayServerEnabled,
        int? RelayPort,
        int? RelayServerPort);

    public sealed record FileScanRequest(string FileName, long SizeBytes, string? ContentType);

    public sealed record FileScanResult(string Status, string Message, string? Details);
}
