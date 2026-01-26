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
        private readonly Dictionary<string, Contact> _contacts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ChatMessage>> _threads = new();

        public event EventHandler<ChatNotification>? MessageReceived;

        public ChatService(SessionState session, MessageCipher cipher)
        {
            _session = session;
            _cipher = cipher;
            Seed();
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
                null);

            list.Add(message);
            contact.PreviewCipher = cipherText;
            contact.UnreadCount = 0;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
            return await MapMessageAsync(message, cancellationToken);
        }

        public async Task<ChatMessageDto> AddFileMessageAsync(
            string threadId,
            string fileName,
            long sizeBytes,
            string status,
            CancellationToken cancellationToken)
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
            var attachment = new ChatAttachment(fileCipher, sizeBytes, status);
            var message = new ChatMessage(
                Guid.NewGuid().ToString("N"),
                threadId,
                user.DisplayName,
                true,
                textCipher,
                DateTimeOffset.UtcNow,
                attachment);

            list.Add(message);
            contact.PreviewCipher = textCipher;
            contact.UnreadCount = 0;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
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
                null);

            list.Add(message);
            contact.PreviewCipher = cipherText;
            contact.UnreadCount += 1;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
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
            CancellationToken cancellationToken)
        {
            var contact = EnsureContact(threadId, sender);
            if (!_threads.TryGetValue(threadId, out var list))
            {
                list = new List<ChatMessage>();
                _threads[threadId] = list;
            }

            var text = "Dosya paylasildi";
            var textCipher = await _cipher.EncryptAsync(text, cancellationToken);
            var fileCipher = await _cipher.EncryptAsync(fileName, cancellationToken);
            var attachment = new ChatAttachment(fileCipher, sizeBytes, status);
            var message = new ChatMessage(
                Guid.NewGuid().ToString("N"),
                threadId,
                sender,
                false,
                textCipher,
                DateTimeOffset.UtcNow,
                attachment);

            list.Add(message);
            contact.PreviewCipher = textCipher;
            contact.UnreadCount += 1;
            contact.IsOnline = true;
            contact.LastSeenAt = DateTimeOffset.UtcNow;
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
                contact = new Contact(contactId, displayName, isOnline, lastSeenAt, string.Empty);
                _contacts[contactId] = contact;
                return;
            }

            contact.DisplayName = displayName;
            contact.IsOnline = isOnline;
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

        public void Reset()
        {
            _contacts.Clear();
            _threads.Clear();
            Seed();
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

            var contact = new Contact(contactId, displayName, true, DateTimeOffset.UtcNow, string.Empty);
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
                contact.UnreadCount);
        }

        private async Task<ChatMessageDto> MapMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            var text = await _cipher.DecryptAsync(message.TextCipher, cancellationToken);
            ChatAttachmentDto? attachment = null;
            if (message.Attachment != null)
            {
                var fileName = await _cipher.DecryptAsync(message.Attachment.FileNameCipher, cancellationToken);
                attachment = new ChatAttachmentDto(fileName, message.Attachment.SizeBytes, message.Attachment.Status);
            }

            return new ChatMessageDto(
                message.Id,
                message.ThreadId,
                message.Sender,
                message.IsMine,
                text,
                message.SentAt,
                attachment);
        }

        private void Seed()
        {
            _contacts[NetworkThreadId] = new Contact(
                NetworkThreadId,
                "Yerel Ag - Genel Sohbet",
                true,
                DateTimeOffset.UtcNow,
                string.Empty);
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
            public Contact(string id, string displayName, bool isOnline, DateTimeOffset? lastSeenAt, string previewCipher)
            {
                Id = id;
                DisplayName = displayName;
                IsOnline = isOnline;
                LastSeenAt = lastSeenAt;
                PreviewCipher = previewCipher;
            }

            public string Id { get; }
            public string DisplayName { get; set; }
            public bool IsOnline { get; set; }
            public DateTimeOffset? LastSeenAt { get; set; }
            public string PreviewCipher { get; set; }
            public int UnreadCount { get; set; }
        }

        private sealed record ChatAttachment(string FileNameCipher, long SizeBytes, string Status);

        private sealed record ChatMessage(
            string Id,
            string ThreadId,
            string Sender,
            bool IsMine,
            string TextCipher,
            DateTimeOffset SentAt,
            ChatAttachment? Attachment);

        public sealed record ChatNotification(string ThreadId, string DisplayName, string Sender, string Text);
    }
}
