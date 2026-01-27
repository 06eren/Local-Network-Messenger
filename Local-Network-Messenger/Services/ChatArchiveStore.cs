using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Local_Network_Messenger.Services
{
    public sealed record ChatArchiveRecord(
        string Id,
        string Owner,
        string ThreadId,
        string Sender,
        bool IsMine,
        string TextCipher,
        DateTimeOffset SentAt,
        string? DeliveryState,
        bool IsEdited,
        bool IsDeleted,
        string? AttachmentFileNameCipher,
        long? AttachmentSizeBytes,
        string? AttachmentStatus,
        double? AttachmentProgress,
        string? AttachmentTransferState);

    public interface IChatArchiveStore
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<ChatArchiveRecord>> LoadRecentAsync(string owner, int limit, CancellationToken cancellationToken);
        Task<IReadOnlyList<ChatArchiveRecord>> LoadRangeAsync(string owner, DateTimeOffset? since, CancellationToken cancellationToken);
        Task UpsertMessageAsync(ChatArchiveRecord record, CancellationToken cancellationToken);
        Task UpdateDeliveryStateAsync(string owner, string messageId, string? state, CancellationToken cancellationToken);
        Task UpdateAttachmentAsync(
            string owner,
            string messageId,
            string? status,
            double? progress,
            string? transferState,
            CancellationToken cancellationToken);
        Task ClearAsync(string owner, DateTimeOffset? since, CancellationToken cancellationToken);
    }

    public sealed class NoopChatArchiveStore : IChatArchiveStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ChatArchiveRecord>> LoadRecentAsync(string owner, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChatArchiveRecord>>(Array.Empty<ChatArchiveRecord>());
        public Task<IReadOnlyList<ChatArchiveRecord>> LoadRangeAsync(string owner, DateTimeOffset? since, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChatArchiveRecord>>(Array.Empty<ChatArchiveRecord>());
        public Task UpsertMessageAsync(ChatArchiveRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateDeliveryStateAsync(string owner, string messageId, string? state, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task UpdateAttachmentAsync(
            string owner,
            string messageId,
            string? status,
            double? progress,
            string? transferState,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(string owner, DateTimeOffset? since, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SqliteChatArchiveStore : IChatArchiveStore
    {
        private readonly string _connectionString;

        public SqliteChatArchiveStore(string databasePath)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            };
            _connectionString = builder.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Messages (
                    Id TEXT PRIMARY KEY,
                    Owner TEXT NOT NULL,
                    ThreadId TEXT NOT NULL,
                    Sender TEXT NOT NULL,
                    IsMine INTEGER NOT NULL,
                    TextCipher TEXT NOT NULL,
                    SentAt INTEGER NOT NULL,
                    DeliveryState TEXT NULL,
                    IsEdited INTEGER NULL,
                    IsDeleted INTEGER NULL,
                    AttachmentFileNameCipher TEXT NULL,
                    AttachmentSizeBytes INTEGER NULL,
                    AttachmentStatus TEXT NULL,
                    AttachmentProgress REAL NULL,
                    AttachmentTransferState TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Messages_Owner ON Messages (Owner);
                CREATE INDEX IF NOT EXISTS IX_Messages_Thread ON Messages (Owner, ThreadId, SentAt);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureColumnsAsync(connection, cancellationToken);
        }

        private static async Task EnsureColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(Messages);";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(1);
                existing.Add(name);
            }

            if (!existing.Contains("IsEdited"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Messages ADD COLUMN IsEdited INTEGER NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!existing.Contains("IsDeleted"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Messages ADD COLUMN IsDeleted INTEGER NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task<IReadOnlyList<ChatArchiveRecord>> LoadRecentAsync(string owner, int limit, CancellationToken cancellationToken)
        {
            var results = new List<ChatArchiveRecord>();
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Owner, ThreadId, Sender, IsMine, TextCipher, SentAt, DeliveryState,
                       IsEdited, IsDeleted, AttachmentFileNameCipher, AttachmentSizeBytes, AttachmentStatus, AttachmentProgress, AttachmentTransferState
                FROM Messages
                WHERE Owner = $owner
                ORDER BY SentAt ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6));
                var isEdited = !reader.IsDBNull(8) && reader.GetInt32(8) == 1;
                var isDeleted = !reader.IsDBNull(9) && reader.GetInt32(9) == 1;
                var attachmentFileName = reader.IsDBNull(10) ? null : reader.GetString(10);
                long? attachmentSize = reader.IsDBNull(11) ? null : reader.GetInt64(11);
                var attachmentStatus = reader.IsDBNull(12) ? null : reader.GetString(12);
                double? attachmentProgress = reader.IsDBNull(13) ? null : reader.GetDouble(13);
                var attachmentTransferState = reader.IsDBNull(14) ? null : reader.GetString(14);
                results.Add(new ChatArchiveRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4) == 1,
                    reader.GetString(5),
                    sentAt,
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    isEdited,
                    isDeleted,
                    attachmentFileName,
                    attachmentSize,
                    attachmentStatus,
                    attachmentProgress,
                    attachmentTransferState));
            }

            return results;
        }

        public async Task<IReadOnlyList<ChatArchiveRecord>> LoadRangeAsync(
            string owner,
            DateTimeOffset? since,
            CancellationToken cancellationToken)
        {
            var results = new List<ChatArchiveRecord>();
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Owner, ThreadId, Sender, IsMine, TextCipher, SentAt, DeliveryState,
                       IsEdited, IsDeleted, AttachmentFileNameCipher, AttachmentSizeBytes, AttachmentStatus, AttachmentProgress, AttachmentTransferState
                FROM Messages
                WHERE Owner = $owner
                  AND ($since IS NULL OR SentAt >= $since)
                ORDER BY SentAt ASC;
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$since", since.HasValue ? since.Value.ToUnixTimeMilliseconds() : DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6));
                var isEdited = !reader.IsDBNull(8) && reader.GetInt32(8) == 1;
                var isDeleted = !reader.IsDBNull(9) && reader.GetInt32(9) == 1;
                var attachmentFileName = reader.IsDBNull(10) ? null : reader.GetString(10);
                long? attachmentSize = reader.IsDBNull(11) ? null : reader.GetInt64(11);
                var attachmentStatus = reader.IsDBNull(12) ? null : reader.GetString(12);
                double? attachmentProgress = reader.IsDBNull(13) ? null : reader.GetDouble(13);
                var attachmentTransferState = reader.IsDBNull(14) ? null : reader.GetString(14);
                results.Add(new ChatArchiveRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4) == 1,
                    reader.GetString(5),
                    sentAt,
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    isEdited,
                    isDeleted,
                    attachmentFileName,
                    attachmentSize,
                    attachmentStatus,
                    attachmentProgress,
                    attachmentTransferState));
            }

            return results;
        }

        public async Task UpsertMessageAsync(ChatArchiveRecord record, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Messages (
                    Id, Owner, ThreadId, Sender, IsMine, TextCipher, SentAt, DeliveryState,
                    IsEdited, IsDeleted, AttachmentFileNameCipher, AttachmentSizeBytes, AttachmentStatus, AttachmentProgress, AttachmentTransferState
                )
                VALUES (
                    $id, $owner, $threadId, $sender, $isMine, $textCipher, $sentAt, $deliveryState,
                    $isEdited, $isDeleted, $fileNameCipher, $fileSize, $fileStatus, $fileProgress, $fileTransfer
                )
                ON CONFLICT(Id) DO UPDATE SET
                    Owner = excluded.Owner,
                    ThreadId = excluded.ThreadId,
                    Sender = excluded.Sender,
                    IsMine = excluded.IsMine,
                    TextCipher = excluded.TextCipher,
                    SentAt = excluded.SentAt,
                    DeliveryState = excluded.DeliveryState,
                    IsEdited = excluded.IsEdited,
                    IsDeleted = excluded.IsDeleted,
                    AttachmentFileNameCipher = excluded.AttachmentFileNameCipher,
                    AttachmentSizeBytes = excluded.AttachmentSizeBytes,
                    AttachmentStatus = excluded.AttachmentStatus,
                    AttachmentProgress = excluded.AttachmentProgress,
                    AttachmentTransferState = excluded.AttachmentTransferState;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
            command.Parameters.AddWithValue("$owner", record.Owner);
            command.Parameters.AddWithValue("$threadId", record.ThreadId);
            command.Parameters.AddWithValue("$sender", record.Sender);
            command.Parameters.AddWithValue("$isMine", record.IsMine ? 1 : 0);
            command.Parameters.AddWithValue("$textCipher", record.TextCipher);
            command.Parameters.AddWithValue("$sentAt", record.SentAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$deliveryState", (object?)record.DeliveryState ?? DBNull.Value);
            command.Parameters.AddWithValue("$isEdited", record.IsEdited ? 1 : 0);
            command.Parameters.AddWithValue("$isDeleted", record.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$fileNameCipher", (object?)record.AttachmentFileNameCipher ?? DBNull.Value);
            command.Parameters.AddWithValue("$fileSize", (object?)record.AttachmentSizeBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$fileStatus", (object?)record.AttachmentStatus ?? DBNull.Value);
            command.Parameters.AddWithValue("$fileProgress", (object?)record.AttachmentProgress ?? DBNull.Value);
            command.Parameters.AddWithValue("$fileTransfer", (object?)record.AttachmentTransferState ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task UpdateDeliveryStateAsync(string owner, string messageId, string? state, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Messages
                SET DeliveryState = $state
                WHERE Owner = $owner AND Id = $id;
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$id", messageId);
            command.Parameters.AddWithValue("$state", (object?)state ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task UpdateAttachmentAsync(
            string owner,
            string messageId,
            string? status,
            double? progress,
            string? transferState,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Messages
                SET AttachmentStatus = COALESCE($status, AttachmentStatus),
                    AttachmentProgress = COALESCE($progress, AttachmentProgress),
                    AttachmentTransferState = COALESCE($transferState, AttachmentTransferState)
                WHERE Owner = $owner AND Id = $id;
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$id", messageId);
            command.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("$progress", (object?)progress ?? DBNull.Value);
            command.Parameters.AddWithValue("$transferState", (object?)transferState ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task ClearAsync(string owner, DateTimeOffset? since, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM Messages
                WHERE Owner = $owner
                  AND ($since IS NULL OR SentAt >= $since);
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$since", since.HasValue ? since.Value.ToUnixTimeMilliseconds() : DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
