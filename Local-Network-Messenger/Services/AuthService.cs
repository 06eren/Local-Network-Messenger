using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed record AuthResult(bool Success, UserProfile? User, IReadOnlyList<ValidationError> Errors, string Message);

    public sealed class AuthService
    {
        private static readonly Regex UsernameRegex = new("^[a-zA-Z0-9._-]{3,20}$", RegexOptions.Compiled);
        private const int MaxFailedAttempts = 5;
        private static readonly System.TimeSpan LockoutDuration = System.TimeSpan.FromMinutes(10);

        private readonly SessionState _session;
        private readonly IUserStore _store;
        private readonly PasswordHasher _hasher;
        private readonly SessionStore _sessionStore;
        private readonly SecurityEventLogger? _logger;

        public AuthService(SessionState session, IUserStore store, PasswordHasher hasher, SessionStore sessionStore, SecurityEventLogger? logger = null)
        {
            _session = session;
            _store = store;
            _hasher = hasher;
            _sessionStore = sessionStore;
            _logger = logger;
        }

        public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var username = Normalize(request.Username);
            var password = request.Password?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                errors.Add(new ValidationError("username", "Kullanici adini gir."));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                errors.Add(new ValidationError("password", "Sifreni gir."));
            }

            if (errors.Count > 0)
            {
                return new AuthResult(false, null, errors, "Alanlari kontrol et.");
            }

            var record = await _store.FindAsync(username, cancellationToken);
            if (record == null)
            {
                errors.Add(new ValidationError("username", "Bu kullanici bulunamadi."));
                await LogAsync("login.failed", "Kullanici bulunamadi.", username);
                return new AuthResult(false, null, errors, "Giris yapilamadi.");
            }

            var security = await _store.GetSecurityInfoAsync(username, cancellationToken);
            if (security?.LockoutUntil is not null && security.LockoutUntil > System.DateTimeOffset.UtcNow)
            {
                var remaining = security.LockoutUntil.Value - System.DateTimeOffset.UtcNow;
                var minutes = System.Math.Max(1, (int)System.Math.Ceiling(remaining.TotalMinutes));
                errors.Add(new ValidationError("general", $"Cok fazla hatali giris. {minutes} dk sonra tekrar dene."));
                await LogAsync("login.locked", "Hesap kilitli.", username);
                return new AuthResult(false, null, errors, "Hesap gecici olarak kilitli.");
            }

            if (!_hasher.Verify(password, record.PasswordHash))
            {
                var failed = (security?.FailedCount ?? 0) + 1;
                System.DateTimeOffset? lockoutUntil = null;
                if (failed >= MaxFailedAttempts)
                {
                    lockoutUntil = System.DateTimeOffset.UtcNow.Add(LockoutDuration);
                }
                await _store.UpdateSecurityInfoAsync(username, failed, lockoutUntil, cancellationToken);
                if (lockoutUntil.HasValue)
                {
                    errors.Add(new ValidationError("general", "Cok fazla hatali giris. Hesap gecici olarak kilitlendi."));
                    await LogAsync("login.locked", "Cok fazla hatali giris.", username);
                }
                else
                {
                    errors.Add(new ValidationError("password", "Sifre hatali."));
                    await LogAsync("login.failed", "Sifre hatali.", username);
                }
                return new AuthResult(false, null, errors, "Giris yapilamadi.");
            }

            var user = new UserProfile(record.Username, record.DisplayName);
            _session.SetUser(user);
            await _sessionStore.SaveAsync(user, cancellationToken);
            await _store.UpdateSecurityInfoAsync(username, 0, null, cancellationToken);
            await LogAsync("login.success", "Giris basarili.", username);
            return new AuthResult(true, user, new List<ValidationError>(), "Giris basarili.");
        }

        public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var username = Normalize(request.Username);
            var displayName = request.DisplayName?.Trim() ?? string.Empty;
            var password = request.Password ?? string.Empty;
            var confirm = request.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                errors.Add(new ValidationError("username", "Kullanici adini gir."));
            }
            else if (!UsernameRegex.IsMatch(username))
            {
                errors.Add(new ValidationError("username", "Kullanici adi 3-20 karakter olmali ve ozel isaret icermemeli."));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add(new ValidationError("displayName", "Gorunen adini gir."));
            }
            else if (displayName.Length < 2 || displayName.Length > 30)
            {
                errors.Add(new ValidationError("displayName", "Gorunen ad 2-30 karakter olmali."));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                errors.Add(new ValidationError("password", "Sifreni gir."));
            }
            else
            {
                errors.AddRange(ValidatePasswordPolicy(password));
            }

            if (string.IsNullOrWhiteSpace(confirm))
            {
                errors.Add(new ValidationError("confirmPassword", "Sifreyi tekrar gir."));
            }
            else if (password != confirm)
            {
                errors.Add(new ValidationError("confirmPassword", "Sifreler eslesmiyor."));
            }

            if (errors.Count > 0)
            {
                return new AuthResult(false, null, errors, "Alanlari kontrol et.");
            }

            if (await _store.ExistsAsync(username, cancellationToken))
            {
                errors.Add(new ValidationError("username", "Bu kullanici adi kullaniliyor."));
                return new AuthResult(false, null, errors, "Kayit yapilamadi.");
            }

            var hash = _hasher.Hash(password);
            await _store.AddAsync(new UserRecord(username, displayName, hash), cancellationToken);
            await _store.UpdateSecurityInfoAsync(username, 0, null, cancellationToken);
            var user = new UserProfile(username, displayName);
            _session.SetUser(user);
            await _sessionStore.SaveAsync(user, cancellationToken);
            await LogAsync("register.success", "Kayit basarili.", username);
            return new AuthResult(true, user, new List<ValidationError>(), "Kayit basarili.");
        }

        public async Task<UserProfile?> RestoreSessionAsync(CancellationToken cancellationToken)
        {
            var user = await _sessionStore.LoadAsync(cancellationToken);
            if (user == null)
            {
                return null;
            }

            var exists = await _store.ExistsAsync(user.Username, cancellationToken);
            if (!exists)
            {
                await _sessionStore.ClearAsync(cancellationToken);
                return null;
            }

            _session.SetUser(user);
            return user;
        }

        public async Task<AuthResult> RenameAsync(RenameRequest request, CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var current = _session.CurrentUser;
            if (current == null)
            {
                errors.Add(new ValidationError("general", "Oturum bulunamadi."));
                return new AuthResult(false, null, errors, "Oturum bulunamadi.");
            }

            var nextUsername = Normalize(request.NewUsername);
            if (string.IsNullOrWhiteSpace(nextUsername))
            {
                errors.Add(new ValidationError("username", "Yeni kullanici adini gir."));
            }
            else if (!UsernameRegex.IsMatch(nextUsername))
            {
                errors.Add(new ValidationError("username", "Kullanici adi 3-20 karakter olmali ve ozel isaret icermemeli."));
            }
            else if (string.Equals(current.Username, nextUsername, System.StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError("username", "Yeni kullanici adi mevcut adinla ayni."));
            }

            if (errors.Count > 0)
            {
                return new AuthResult(false, null, errors, "Alanlari kontrol et.");
            }

            var renamed = await _store.RenameAsync(current.Username, nextUsername, cancellationToken);
            if (!renamed)
            {
                errors.Add(new ValidationError("username", "Bu kullanici adi kullaniliyor."));
                return new AuthResult(false, null, errors, "Kullanici adi guncellenemedi.");
            }

            var updatedUser = new UserProfile(nextUsername, current.DisplayName);
            _session.SetUser(updatedUser);
            await _sessionStore.SaveAsync(updatedUser, cancellationToken);
            return new AuthResult(true, updatedUser, new List<ValidationError>(), "Kullanici adi guncellendi.");
        }

        public async Task<AuthResult> UpdateDisplayNameAsync(DisplayNameRequest request, CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var current = _session.CurrentUser;
            if (current == null)
            {
                errors.Add(new ValidationError("general", "Oturum bulunamadi."));
                return new AuthResult(false, null, errors, "Oturum bulunamadi.");
            }

            var displayName = request.DisplayName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add(new ValidationError("displayName", "Gorunen adini gir."));
            }
            else if (displayName.Length < 2 || displayName.Length > 30)
            {
                errors.Add(new ValidationError("displayName", "Gorunen ad 2-30 karakter olmali."));
            }
            else if (string.Equals(current.DisplayName, displayName, System.StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError("displayName", "Gorunen ad mevcut adinla ayni."));
            }

            if (errors.Count > 0)
            {
                return new AuthResult(false, null, errors, "Alanlari kontrol et.");
            }

            var updated = await _store.UpdateDisplayNameAsync(current.Username, displayName, cancellationToken);
            if (!updated)
            {
                errors.Add(new ValidationError("displayName", "Kullanici bulunamadi."));
                return new AuthResult(false, null, errors, "Gorunen ad guncellenemedi.");
            }

            var updatedUser = new UserProfile(current.Username, displayName);
            _session.SetUser(updatedUser);
            await _sessionStore.SaveAsync(updatedUser, cancellationToken);
            return new AuthResult(true, updatedUser, new List<ValidationError>(), "Gorunen ad guncellendi.");
        }

        public async Task<AuthResult> LogoutAsync(CancellationToken cancellationToken)
        {
            _session.Clear();
            await _sessionStore.ClearAsync(cancellationToken);
            return new AuthResult(true, null, new List<ValidationError>(), "Cikis yapildi.");
        }

        private static IEnumerable<ValidationError> ValidatePasswordPolicy(string password)
        {
            var errors = new List<ValidationError>();
            if (password.Length < 8 || password.Length > 64)
            {
                errors.Add(new ValidationError("password", "Sifre 8-64 karakter olmali."));
                return errors;
            }

            var hasUpper = false;
            var hasLower = false;
            var hasDigit = false;
            var hasSpecial = false;

            foreach (var ch in password)
            {
                if (char.IsUpper(ch))
                {
                    hasUpper = true;
                }
                else if (char.IsLower(ch))
                {
                    hasLower = true;
                }
                else if (char.IsDigit(ch))
                {
                    hasDigit = true;
                }
                else
                {
                    hasSpecial = true;
                }
            }

            if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
            {
                errors.Add(new ValidationError(
                    "password",
                    "Sifre buyuk harf, kucuk harf, rakam ve ozel karakter icermeli."));
            }

            return errors;
        }

        private static string Normalize(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private Task LogAsync(string eventType, string message, string? username)
        {
            if (_logger == null)
            {
                return Task.CompletedTask;
            }

            return _logger.LogAsync(eventType, message, username);
        }
    }
}
