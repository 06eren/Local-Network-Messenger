using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public sealed record UserRecord(string Username, string DisplayName, PasswordHash PasswordHash);

    public interface IUserStore
    {
        Task InitializeAsync(CancellationToken cancellationToken);

        Task<bool> ExistsAsync(string username, CancellationToken cancellationToken);

        Task<UserRecord?> FindAsync(string username, CancellationToken cancellationToken);

        Task AddAsync(UserRecord record, CancellationToken cancellationToken);

        Task<bool> RenameAsync(string currentUsername, string newUsername, CancellationToken cancellationToken);

        Task<bool> UpdateDisplayNameAsync(string username, string displayName, CancellationToken cancellationToken);
    }

    public sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<string, UserRecord> _users = new(StringComparer.OrdinalIgnoreCase);

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string username, CancellationToken cancellationToken)
        {
            return Task.FromResult(_users.ContainsKey(username));
        }

        public Task<UserRecord?> FindAsync(string username, CancellationToken cancellationToken)
        {
            _users.TryGetValue(username, out var record);
            return Task.FromResult(record);
        }

        public Task AddAsync(UserRecord record, CancellationToken cancellationToken)
        {
            _users[record.Username] = record;
            return Task.CompletedTask;
        }

        public Task<bool> RenameAsync(string currentUsername, string newUsername, CancellationToken cancellationToken)
        {
            if (!_users.TryGetValue(currentUsername, out var record))
            {
                return Task.FromResult(false);
            }

            if (_users.ContainsKey(newUsername))
            {
                return Task.FromResult(false);
            }

            _users.Remove(currentUsername);
            _users[newUsername] = record with { Username = newUsername };
            return Task.FromResult(true);
        }

        public Task<bool> UpdateDisplayNameAsync(string username, string displayName, CancellationToken cancellationToken)
        {
            if (!_users.TryGetValue(username, out var record))
            {
                return Task.FromResult(false);
            }

            _users[username] = record with { DisplayName = displayName };
            return Task.FromResult(true);
        }
    }
}
