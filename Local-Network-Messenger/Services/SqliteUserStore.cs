using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Local_Network_Messenger.Services
{
    public sealed class SqliteUserStore : IUserStore
    {
        private readonly string _connectionString;

        public SqliteUserStore(string databasePath)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            };
            _connectionString = builder.ToString();
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Users (
                    Username TEXT PRIMARY KEY COLLATE NOCASE,
                    DisplayName TEXT NOT NULL,
                    Salt BLOB NOT NULL,
                    Key BLOB NOT NULL,
                    Iterations INTEGER NOT NULL,
                    FailedCount INTEGER NOT NULL DEFAULT 0,
                    LockoutUntil INTEGER NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureSecurityColumnsAsync(connection, cancellationToken);
        }

        private static async Task EnsureSecurityColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(Users);";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add(reader.GetString(1));
                }
            }

            if (!columns.Contains("FailedCount"))
            {
                await using var addFailed = connection.CreateCommand();
                addFailed.CommandText = "ALTER TABLE Users ADD COLUMN FailedCount INTEGER NOT NULL DEFAULT 0;";
                await addFailed.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!columns.Contains("LockoutUntil"))
            {
                await using var addLockout = connection.CreateCommand();
                addLockout.CommandText = "ALTER TABLE Users ADD COLUMN LockoutUntil INTEGER NULL;";
                await addLockout.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task<bool> ExistsAsync(string username, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM Users WHERE Username = $username LIMIT 1;";
            command.Parameters.AddWithValue("$username", username);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result != null;
        }

        public async Task<UserRecord?> FindAsync(string username, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Username, DisplayName, Salt, Key, Iterations
                FROM Users
                WHERE Username = $username
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$username", username);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var storedUsername = reader.GetString(0);
            var displayName = reader.GetString(1);
            var salt = reader.GetFieldValue<byte[]>(2);
            var key = reader.GetFieldValue<byte[]>(3);
            var iterations = reader.GetInt32(4);
            var hash = new PasswordHash(salt, key, iterations);
            return new UserRecord(storedUsername, displayName, hash);
        }

        public async Task AddAsync(UserRecord record, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Users (Username, DisplayName, Salt, Key, Iterations)
                VALUES ($username, $displayName, $salt, $key, $iterations);
                """;
            command.Parameters.AddWithValue("$username", record.Username);
            command.Parameters.AddWithValue("$displayName", record.DisplayName);
            command.Parameters.Add("$salt", SqliteType.Blob).Value = record.PasswordHash.Salt;
            command.Parameters.Add("$key", SqliteType.Blob).Value = record.PasswordHash.Key;
            command.Parameters.AddWithValue("$iterations", record.PasswordHash.Iterations);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> RenameAsync(string currentUsername, string newUsername, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var existsCommand = connection.CreateCommand())
            {
                existsCommand.CommandText = "SELECT 1 FROM Users WHERE Username = $username LIMIT 1;";
                existsCommand.Parameters.AddWithValue("$username", newUsername);
                var exists = await existsCommand.ExecuteScalarAsync(cancellationToken);
                if (exists != null)
                {
                    return false;
                }
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET Username = $newUsername WHERE Username = $currentUsername;";
            command.Parameters.AddWithValue("$newUsername", newUsername);
            command.Parameters.AddWithValue("$currentUsername", currentUsername);
            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }

        public async Task<bool> UpdateDisplayNameAsync(string username, string displayName, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET DisplayName = $displayName WHERE Username = $username;";
            command.Parameters.AddWithValue("$displayName", displayName);
            command.Parameters.AddWithValue("$username", username);
            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }

        public async Task<LoginSecurityInfo?> GetSecurityInfoAsync(string username, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT FailedCount, LockoutUntil
                FROM Users
                WHERE Username = $username
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$username", username);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var failed = reader.GetInt32(0);
            System.DateTimeOffset? lockoutUntil = null;
            if (!reader.IsDBNull(1))
            {
                lockoutUntil = System.DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
            }
            return new LoginSecurityInfo(failed, lockoutUntil);
        }

        public async Task UpdateSecurityInfoAsync(string username, int failedCount, System.DateTimeOffset? lockoutUntil, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Users
                SET FailedCount = $failedCount,
                    LockoutUntil = $lockoutUntil
                WHERE Username = $username;
                """;
            command.Parameters.AddWithValue("$failedCount", failedCount);
            command.Parameters.AddWithValue("$lockoutUntil", lockoutUntil.HasValue ? lockoutUntil.Value.ToUnixTimeMilliseconds() : DBNull.Value);
            command.Parameters.AddWithValue("$username", username);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
