using CtYun.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace CtYun;

internal sealed class AdminAuthService
{
    public const string DefaultInitialPassword = "admin123";
    private const string SessionCookieName = "ctyun_admin_session";
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan FailedAttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LoginLockoutDuration = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly Dictionary<string, AdminSession> _sessions = [];
    private readonly Dictionary<string, LoginFailureState> _failedLogins = [];
    private AdminCredentialFile _credential;

    public AdminAuthService(CtYunConfigStore configStore)
    {
        CredentialPath = Path.Combine(configStore.DataDir, "admin-auth.json");
        _credential = LoadOrCreateCredential();
    }

    public string CredentialPath { get; }

    public bool MustChangePassword
    {
        get
        {
            lock (_sync)
            {
                return _credential.MustChangePassword;
            }
        }
    }

    public AdminAuthStatusResponse GetStatus(HttpContext context)
    {
        return new AdminAuthStatusResponse
        {
            Authenticated = TryGetSession(context, out _),
            MustChangePassword = MustChangePassword
        };
    }

    public bool VerifyPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        AdminCredentialFile credential;
        lock (_sync)
        {
            credential = _credential;
        }

        try
        {
            var salt = Convert.FromBase64String(credential.PasswordSalt);
            var expectedHash = Convert.FromBase64String(credential.PasswordHash);
            var actualHash = HashPassword(password, salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    public bool IsLoginLockedOut(HttpContext context, out TimeSpan retryAfter)
    {
        var key = GetClientKey(context);
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            if (!_failedLogins.TryGetValue(key, out var state))
            {
                retryAfter = TimeSpan.Zero;
                return false;
            }

            if (state.LockedUntil is { } lockedUntil)
            {
                if (lockedUntil > now)
                {
                    retryAfter = lockedUntil - now;
                    return true;
                }

                _failedLogins.Remove(key);
            }
        }

        retryAfter = TimeSpan.Zero;
        return false;
    }

    public void RecordFailedLogin(HttpContext context, out TimeSpan retryAfter)
    {
        var key = GetClientKey(context);
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            if (!_failedLogins.TryGetValue(key, out var state) ||
                now - state.FirstFailedAt > FailedAttemptWindow)
            {
                state = new LoginFailureState
                {
                    FirstFailedAt = now
                };
                _failedLogins[key] = state;
            }

            state.Attempts++;
            state.LastFailedAt = now;

            if (state.Attempts >= MaxFailedAttempts)
            {
                state.LockedUntil = now.Add(LoginLockoutDuration);
                retryAfter = LoginLockoutDuration;
                return;
            }
        }

        retryAfter = TimeSpan.Zero;
    }

    public void RecordSuccessfulLogin(HttpContext context)
    {
        var key = GetClientKey(context);
        lock (_sync)
        {
            _failedLogins.Remove(key);
        }
    }

    public void SignIn(HttpContext context)
    {
        var token = GenerateToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);

        lock (_sync)
        {
            _sessions[token] = new AdminSession { ExpiresAt = expiresAt };
        }

        context.Response.Cookies.Append(SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Expires = expiresAt,
            Path = "/"
        });
    }

    public void SignOut(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(SessionCookieName, out var token))
        {
            lock (_sync)
            {
                _sessions.Remove(token);
            }
        }

        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });
    }

    public bool TryGetSession(HttpContext context, out bool mustChangePassword)
    {
        mustChangePassword = MustChangePassword;
        if (!context.Request.Cookies.TryGetValue(SessionCookieName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_sessions.TryGetValue(token, out var session))
            {
                return false;
            }

            if (session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _sessions.Remove(token);
                return false;
            }

            session.ExpiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);
            return true;
        }
    }

    public bool TryChangePassword(string currentPassword, string newPassword, out string message)
    {
        if (!VerifyPassword(currentPassword))
        {
            message = "当前管理员密码不正确。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            message = "新密码至少需要 8 个字符。";
            return false;
        }

        if (VerifyPassword(newPassword))
        {
            message = "新密码不能与当前密码相同。";
            return false;
        }

        var credential = CreateCredential(newPassword, mustChangePassword: false);
        SaveCredential(credential);

        lock (_sync)
        {
            _credential = credential;
            _sessions.Clear();
        }

        message = "管理员密码已更新。";
        return true;
    }

    private AdminCredentialFile LoadOrCreateCredential()
    {
        if (File.Exists(CredentialPath))
        {
            try
            {
                var json = File.ReadAllText(CredentialPath);
                var credential = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AdminCredentialFile);
                if (IsUsable(credential))
                {
                    return credential;
                }
            }
            catch (Exception ex)
            {
                Utility.WriteLine(ConsoleColor.Red, $"Read admin auth file failed: {ex.Message}");
            }
        }

        var initialPassword = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD");
        if (string.IsNullOrWhiteSpace(initialPassword))
        {
            initialPassword = DefaultInitialPassword;
        }

        var created = CreateCredential(initialPassword, mustChangePassword: true);
        SaveCredential(created);
        Utility.WriteLine(ConsoleColor.Yellow, "Admin panel password initialized. Change it after first login.");
        return created;
    }

    private void SaveCredential(AdminCredentialFile credential)
    {
        var directory = Path.GetDirectoryName(CredentialPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(credential, AppJsonSerializerContext.Default.AdminCredentialFile);
        File.WriteAllText(CredentialPath, json);
    }

    private static AdminCredentialFile CreateCredential(string password, bool mustChangePassword)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);
        return new AdminCredentialFile
        {
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(hash),
            MustChangePassword = mustChangePassword,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
    }

    private static bool IsUsable(AdminCredentialFile credential)
    {
        return credential != null &&
               !string.IsNullOrWhiteSpace(credential.PasswordSalt) &&
               !string.IsNullOrWhiteSpace(credential.PasswordHash);
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string GetClientKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private sealed class AdminSession
    {
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class LoginFailureState
    {
        public int Attempts { get; set; }

        public DateTimeOffset FirstFailedAt { get; set; }

        public DateTimeOffset LastFailedAt { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }
    }
}
