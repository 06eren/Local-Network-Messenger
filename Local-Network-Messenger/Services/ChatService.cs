using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed class ChatService
    {
        private const string NetworkThreadId = "all";
        private readonly SessionState _session;
        private readonly MessageCipher _cipher;
        private IChatArchiveStore _archive;
        private readonly Dictionary<string, Contact> _contacts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ChatMessage>> _threads = new();

        public event EventHandler<ChatNotification>? MessageReceived;

        public ChatService(SessionState session, MessageCipher cipher, IChatArchiveStore? archive = null)
        {
            _session = session;
            _cipher = cipher;
            _archive = archive ?? new NoopChatArchiveStore();
            Seed();
        }

        public void SetArchiveStore(IChatArchiveStore archive)
        {
            _archive = archive ?? new NoopChatArchiveStore();
        }

        public async Task LoadHistoryAsync(UserProfile user, CancellationToken cancellationToken)
        {
            _contacts.Clear();
            _threads.Clear();
            Seed();

            var records = await _archive.LoadRecentAsync(user.Username, 2000, cancellationToken);
            foreach (var record in records)
            {
                var attachment = record.AttachmentFileNameCipher == null
                    ? null
                    : new ChatAttachment(
                        record.AttachmentFileNameCipher,
                        record.AttachmentSizeBytes ?? 0,
                        record.AttachmentStatus ?? string.Empty,
                        record.AttachmentProgress,
                        record.AttachmentTransferState,
                        null,
                        null,
                        null);
                var message = new ChatMessage(
                    record.Id,
                    record.ThreadId,
                    record.Sender,
                    record.IsMine,
                    record.TextCipher,
                    record.SentAt,
                    attachment,
                    record.DeliveryState);

                if (!_threads.TryGetValue(record.ThreadId, out var list))
                {
                    list = new List<ChatMessage>();
                    _threads[record.ThreadId] = list;
                }

                list.Add(message);

                if (record.ThreadId == NetworkThreadId)
                {
                    var general = FindContact(NetworkThreadId);
                    general.PreviewCipher = record.TextCipher;
                    general.LastSeenAt = record.SentAt;
                    continue;
                }

                var displayName = record.IsMine ? record.ThreadId : record.Sender;
                var contact = EnsureContact(record.ThreadId, displayName);
                contact.IsOnline = false;
                contact.PreviewCipher = record.TextCipher;
                contact.LastSeenAt = record.SentAt;
                contact.UnreadCount = 0;
            }
        }

        public async Task<ChatSnapshot> GetSnapshotAsync(string? activeContactId, CancellationToken cancellationToken)
        {
            var user = RequireUser();
            var activeId = ResolveActiveContact(activeContactId);

            var contacts = new List<ContactDto>(_contacts.Count);
            foreach (var contact in _contacts.Values)
            {
                contacts.Add(await MapContactAsync(contact, cancellationToken));
            }

            var threads = new Dictionary<string, IReadOnlyList<ChatMessageDto>>();
            foreach (var entry in _threads)
            {
                var messages = new List<ChatMessageDto>(entry.Value.Count);
                foreach (var message in entry.Value)
                {
                    messages.Add(await MapMessageAsync(message, cancellationToken));
                }

                threads[entry.Key] = messages;
            }

            return new ChatSnapshot(user, contacts, threads, activeId);
        }

        public async Task<ChatMessageDto> AddMessageAsync(
            string threadId,
            string text,
            CancellationToken cancellationToken)
        {
            var user = RequireUser();
            var contact = FindContact(threadId);
            if (!_threads.TryGetValue(threadId, out var list))
            {
                list = new List<ChatMessage>();
                _threads[threadId] = list;
            }

            var cipherText = await _cipher.EncryptAsync(text, cancellationToken);
            var message = new ChatMessage(
                Guid.NewGuid().ToString("N"),
                threadId,
                user.DisplayName,
                true,
                cipherText,
                DateTimeOffset.UtcNow,
                null,
                "sent");

            list.Add(message);
            contact.PreviewCipher = cipherText;
            contact.UnreadCount = 0;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
            contact.IsTyping = false;
            await PersistAsync(message, cancellationToken);
            return await MapMessageAsync(message, cancellationToken);
        }

        public async Task<ChatMessageDto> AddFileMessageAsync(
            string threadId,
            string fileName,
            long sizeBytes,
            string status,
            CancellationToken cancellationToken,
            string? messageId = null,
            string? contentType = null,
            string? previewDataUrl = null,
            string? localPath = null)
        {
            var user = RequireUser();
            var contact = FindContact(threadId);
            if (!_threads.TryGetValue(threadId, out var list))
            {
                list = new List<ChatMessage>();
                _threads[threadId] = list;
            }

            var text = "Dosya paylasildi";
            var textCipher = await _cipher.EncryptAsync(text, cancellationToken);
            var fileCipher = await _cipher.EncryptAsync(fileName, cancellationToken);
            var attachment = new ChatAttachment(fileCipher, sizeBytes, status, 0, "in-progress", contentType, previewDataUrl, localPath);
            var message = new ChatMessage(
                string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId!,
                threadId,
                user.DisplayName,
                true,
                textCipher,
                DateTimeOffset.UtcNow,
                attachment,
                "sent");

            list.Add(message);
            contact.PreviewCipher = textCipher;
            contact.UnreadCount = 0;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
            contact.IsTyping = false;
            await PersistAsync(message, cancellationToken);
            return await MapMessageAsync(message, cancellationToken);
        }

        public async Task<ChatMessageDto> AddIncomingMessageAsync(
            string threadId,
            string sender,
            string text,
            CancellationToken cancellationToken)
        {
            var contact = EnsureContact(threadId, sender);
            if (!_threads.TryGetValue(threadId, out var list))
            {
                list = new List<ChatMessage>();
                _threads[threadId] = list;
            }

            var cipherText = await _cipher.EncryptAsync(text, cancellationToken);
            var message = new ChatMessage(
                Guid.NewGuid().ToString("N"),
                threadId,
                sender,
                false,
                cipherText,
                DateTimeOffset.UtcNow,
                null,
                null);

            list.Add(message);
            contact.PreviewCipher = cipherText;
            contact.UnreadCount += 1;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
            contact.IsTyping = false;
            await PersistAsync(message, cancellationToken);
            var dto = await MapMessageAsync(message, cancellationToken);
            MessageReceived?.Invoke(this, new ChatNotification(threadId, contact.DisplayName, sender, dto.Text));
            return dto;
        }

        public async Task<ChatMessageDto> AddIncomingFileMessageAsync(
            string threadId,
            string sender,
            string fileName,
            long sizeBytes,
            string status,
            CancellationToken cancellationToken,
            string? messageId = null,
            string? contentType = null,
            string? previewDataUrl = null,
            string? localPath = null)
        {
            var contact = EnsureContact(threadId, sender);
            if (!_threads.TryGetValue(threadId, out var list))
            {
                list = new List<ChatMessage>();
                _threads[threadId] = list;
            }

            if (!string.IsNullOrWhiteSpace(messageId))
            {
                var existing = list.Find(item => string.Equals(item.Id, messageId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (existing.Attachment != null)
                    {
                        existing.Attachment.Status = status;
                        if (!string.IsNullOrWhiteSpace(contentType))
                        {
                            existing.Attachment.ContentType = contentType;
                        }
                        if (!string.IsNullOrWhiteSpace(previewDataUrl))
                        {
                            existing.Attachment.PreviewDataUrl = previewDataUrl;
                        }
                        if (!string.IsNullOrWhiteSpace(localPath))
                        {
                            existing.Attachment.LocalPath = localPath;
                        }
                    }
                    contact.PreviewCipher = existing.TextCipher;
                    contact.IsOnline = true;
                    contact.LastSeenAt = DateTimeOffset.UtcNow;
                    contact.IsTyping = false;
                    await PersistAsync(existing, cancellationToken);
                    return await MapMessageAsync(existing, cancellationToken);
                }
            }

            var text = "Dosya paylasildi";
            var textCipher = await _cipher.EncryptAsync(text, cancellationToken);
            var fileCipher = await _cipher.EncryptAsync(fileName, cancellationToken);
            var attachment = new ChatAttachment(fileCipher, sizeBytes, status, 0, "in-progress", contentType, previewDataUrl, localPath);
            var message = new ChatMessage(
                string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId!,
                threadId,
                sender,
                false,
                textCipher,
                DateTimeOffset.UtcNow,
                attachment,
                null);

            list.Add(message);
            contact.PreviewCipher = textCipher;
            contact.UnreadCount += 1;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
            contact.IsTyping = false;
            await PersistAsync(message, cancellationToken);
            var dto = await MapMessageAsync(message, cancellationToken);
            MessageReceived?.Invoke(this, new ChatNotification(threadId, contact.DisplayName, sender, dto.Text));
            return dto;
        }

        public void UpdateContactPresence(string contactId, string displayName, bool isOnline, DateTimeOffset? lastSeenAt)
        {
            if (string.Equals(contactId, NetworkThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_contacts.TryGetValue(contactId, out var contact))
            {
                contact = new Contact(contactId, displayName, isOnline, lastSeenAt, string.Empty, false, null, null);
                _contacts[contactId] = contact;
                return;
            }

            contact.DisplayName = displayName;
            contact.IsOnline = isOnline;
            if (!isOnline)
            {
                contact.IsTyping = false;
                contact.PingMs = null;
                contact.LossPercent = null;
            }
            if (lastSeenAt.HasValue)
            {
                contact.LastSeenAt = lastSeenAt;
            }
        }

        public void MarkContactOffline(string contactId, DateTimeOffset? lastSeenAt)
        {
            if (!_contacts.TryGetValue(contactId, out var contact))
            {
                return;
            }

            contact.IsOnline = false;
            contact.IsTyping = false;
            contact.PingMs = null;
            contact.LossPercent = null;
            if (lastSeenAt.HasValue)
            {
                contact.LastSeenAt = lastSeenAt;
            }
        }

        public void MarkThreadAsRead(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            if (_contacts.TryGetValue(threadId, out var contact))
            {
                contact.UnreadCount = 0;
            }
        }

        public void MarkThreadReadByPeer(string threadId)
        {
            if (!_threads.TryGetValue(threadId, out var list))
            {
                return;
            }

            foreach (var message in list)
            {
                if (!message.IsMine)
                {
                    continue;
                }

                message.DeliveryState = "read";
            }
        }

        public void UpdateDeliveryState(string messageId, string state)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(state))
            {
                return;
            }

            foreach (var entry in _threads)
            {
                var message = entry.Value.Find(item => string.Equals(item.Id, messageId, StringComparison.OrdinalIgnoreCase));
                if (message == null)
                {
                    continue;
                }

                message.DeliveryState = state;
                break;
            }

            _ = _archive.UpdateDeliveryStateAsync(_session.CurrentUser?.Username ?? string.Empty, messageId, state, CancellationToken.None);
        }

        public void UpdateTypingStatus(string contactId, bool isTyping)
        {
            if (!_contacts.TryGetValue(contactId, out var contact))
            {
                return;
            }

            contact.IsTyping = isTyping;
        }

        public void UpdateConnectionQuality(string contactId, double? pingMs, double? lossPercent)
        {
            if (!_contacts.TryGetValue(contactId, out var contact))
            {
                return;
            }

            contact.PingMs = pingMs;
            contact.LossPercent = lossPercent;
        }

        public void UpdateFileProgress(string messageId, double progress, string? transferState)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            foreach (var entry in _threads)
            {
                var message = entry.Value.Find(item => string.Equals(item.Id, messageId, StringComparison.OrdinalIgnoreCase));
                if (message == null || message.Attachment == null)
                {
                    continue;
                }

                message.Attachment.Progress = progress;
                if (!string.IsNullOrWhiteSpace(transferState))
                {
                    message.Attachment.TransferState = transferState;
                }
                if (progress >= 100)
                {
                    message.Attachment.TransferState = "completed";
                }
                break;
            }

            _ = _archive.UpdateAttachmentAsync(
                _session.CurrentUser?.Username ?? string.Empty,
                messageId,
                null,
                progress,
                transferState,
                CancellationToken.None);
        }

        public void UpdateFileStatus(string messageId, string status)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            foreach (var entry in _threads)
            {
                var message = entry.Value.Find(item => string.Equals(item.Id, messageId, StringComparison.OrdinalIgnoreCase));
                if (message == null || message.Attachment == null)
                {
                    continue;
                }

                message.Attachment.Status = status;
                break;
            }

            _ = _archive.UpdateAttachmentAsync(
                _session.CurrentUser?.Username ?? string.Empty,
                messageId,
                status,
                null,
                null,
                CancellationToken.None);
        }

        public void Reset()
        {
            _contacts.Clear();
            _threads.Clear();
            Seed();
        }

        private Task PersistAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            var owner = _session.CurrentUser?.Username;
            if (string.IsNullOrWhiteSpace(owner))
            {
                return Task.CompletedTask;
            }

            var record = new ChatArchiveRecord(
                message.Id,
                owner,
                message.ThreadId,
                message.Sender,
                message.IsMine,
                message.TextCipher,
                message.SentAt,
                message.DeliveryState,
                message.Attachment?.FileNameCipher,
                message.Attachment?.SizeBytes,
                message.Attachment?.Status,
                message.Attachment?.Progress,
                message.Attachment?.TransferState);
            return _archive.UpsertMessageAsync(record, cancellationToken);
        }

        private UserProfile RequireUser()
        {
            return _session.CurrentUser ?? throw new InvalidOperationException("Oturum bulunamadi.");
        }

        private string ResolveActiveContact(string? requested)
        {
            if (!string.IsNullOrWhiteSpace(requested) && _contacts.ContainsKey(requested))
            {
                return requested!;
            }

            return _contacts.TryGetValue(NetworkThreadId, out var fallback)
                ? fallback.Id
                : _contacts.Keys.FirstOrDefault() ?? string.Empty;
        }

        private Contact FindContact(string contactId)
        {
            if (!_contacts.TryGetValue(contactId, out var contact) || contact == null)
            {
                throw new InvalidOperationException("Kisi bulunamadi.");
            }

            return contact;
        }

        private Contact EnsureContact(string contactId, string displayName)
        {
            if (string.Equals(contactId, NetworkThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return FindContact(contactId);
            }

            if (_contacts.TryGetValue(contactId, out var existing) && existing != null)
            {
                return existing;
            }

            var contact = new Contact(contactId, displayName, true, DateTimeOffset.UtcNow, string.Empty, false, null, null);
            _contacts[contactId] = contact;
            return contact;
        }

        private async Task<ContactDto> MapContactAsync(Contact contact, CancellationToken cancellationToken)
        {
            var preview = await _cipher.DecryptAsync(contact.PreviewCipher, cancellationToken);
            var status = contact.Id == NetworkThreadId
                ? "Aktif"
                : FormatLastSeen(contact.LastSeenAt, contact.IsOnline);
            return new ContactDto(
                contact.Id,
                contact.DisplayName,
                status,
                preview,
                contact.IsOnline,
                contact.UnreadCount,
                contact.IsTyping,
                contact.PingMs,
                contact.LossPercent);
        }

        private async Task<ChatMessageDto> MapMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            var text = await _cipher.DecryptAsync(message.TextCipher, cancellationToken);
            ChatAttachmentDto? attachment = null;
            if (message.Attachment != null)
            {
                var fileName = await _cipher.DecryptAsync(message.Attachment.FileNameCipher, cancellationToken);
                attachment = new ChatAttachmentDto(
                    fileName,
                    message.Attachment.SizeBytes,
                    message.Attachment.Status,
                    message.Attachment.Progress,
                    message.Attachment.TransferState,
                    message.Attachment.ContentType,
                    message.Attachment.PreviewDataUrl,
                    message.Attachment.LocalPath);
            }

            return new ChatMessageDto(
                message.Id,
                message.ThreadId,
                message.Sender,
                message.IsMine,
                text,
                message.SentAt,
                attachment,
                message.DeliveryState);
        }

        private void Seed()
        {
            _contacts[NetworkThreadId] = new Contact(
                NetworkThreadId,
                "Yerel Ag - Genel Sohbet",
                true,
                DateTimeOffset.UtcNow,
                string.Empty,
                false,
                null,
                null);
        }

        private static string FormatLastSeen(DateTimeOffset? lastSeenAt, bool isOnline)
        {
            if (isOnline)
            {
                return "Simdi";
            }

            if (!lastSeenAt.HasValue)
            {
                return "Cevrimdisi";
            }

            var delta = DateTimeOffset.UtcNow - lastSeenAt.Value;
            if (delta < TimeSpan.FromMinutes(1))
            {
                return "Az once";
            }

            if (delta < TimeSpan.FromHours(1))
            {
                return $"{Math.Max(1, (int)delta.TotalMinutes)} dk once";
            }

            if (delta < TimeSpan.FromHours(24))
            {
                return $"{Math.Max(1, (int)delta.TotalHours)} saat once";
            }

            return lastSeenAt.Value.ToLocalTime().ToString("dd.MM.yyyy");
        }

        private sealed class Contact
        {
            public Contact(
                string id,
                string displayName,
                bool isOnline,
                DateTimeOffset? lastSeenAt,
                string previewCipher,
                bool isTyping,
                double? pingMs,
                double? lossPercent)
            {
                Id = id;
                DisplayName = displayName;
                IsOnline = isOnline;
                LastSeenAt = lastSeenAt;
                PreviewCipher = previewCipher;
                IsTyping = isTyping;
                PingMs = pingMs;
                LossPercent = lossPercent;
            }

            public string Id { get; }
            public string DisplayName { get; set; }
            public bool IsOnline { get; set; }
            public DateTimeOffset? LastSeenAt { get; set; }
            public string PreviewCipher { get; set; }
            public int UnreadCount { get; set; }
            public bool IsTyping { get; set; }
            public double? PingMs { get; set; }
            public double? LossPercent { get; set; }
        }

        private sealed record ChatAttachment(
            string FileNameCipher,
            long SizeBytes,
            string Status,
            double? Progress,
            string? TransferState,
            string? ContentType,
            string? PreviewDataUrl,
            string? LocalPath)
        {
            public string Status { get; set; } = Status;
            public double? Progress { get; set; } = Progress;
            public string? TransferState { get; set; } = TransferState;
            public string? ContentType { get; set; } = ContentType;
            public string? PreviewDataUrl { get; set; } = PreviewDataUrl;
            public string? LocalPath { get; set; } = LocalPath;
        }

        private sealed record ChatMessage(
            string Id,
            string ThreadId,
            string Sender,
            bool IsMine,
            string TextCipher,
            DateTimeOffset SentAt,
            ChatAttachment? Attachment,
            string? DeliveryState)
        {
            public string? DeliveryState { get; set; } = DeliveryState;
        }

        public sealed record ChatNotification(string ThreadId, string DisplayName, string Sender, string Text);
    }
}
