using CtYun.Models;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CtYun;

internal sealed class CtYunConfigStore
{
    private const string PasswordMask = "********";
    private const string ConfigEncryptionKeyEnvironmentVariable = "CTYUN_CONFIG_KEY";
    private const string LocalConfigKeyFileName = "config-encryption.key";
    private const string EncryptedConfigFormat = "ctyun-config-v1";
    private const int AesGcmNonceSize = 12;
    private const int AesGcmTagSize = 16;
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _generatedDeviceCodes = [];
    private bool _loadedPlaintextConfig;
    private bool _configNeedsSave;
    private AppConfig _config;

    public CtYunConfigStore()
    {
        DataDir = GetDataDir();
        Directory.CreateDirectory(DataDir);

        ConfigPath = Environment.GetEnvironmentVariable("CTYUN_CONFIG");
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            ConfigPath = Path.Combine(DataDir, "accounts.json");
        }

        _config = LoadFromDisk() ?? LoadFromEnvironment() ?? new AppConfig();
        _config = Normalize(_config, keepExistingPasswords: false);

        if (!File.Exists(ConfigPath) && _config.Accounts.Count > 0)
        {
            Save(_config);
        }
        else if (_loadedPlaintextConfig || _configNeedsSave)
        {
            Save(_config);
            Utility.WriteLine(ConsoleColor.Yellow, "Migrated account config to encrypted storage.");
        }
    }

    public string DataDir { get; }

    public string ConfigPath { get; }

    public AppConfig GetConfig()
    {
        lock (_sync)
        {
            return Clone(_config);
        }
    }

    public AppConfig GetSanitizedConfig()
    {
        var config = GetConfig();
        foreach (var account in config.Accounts)
        {
            account.Password = string.IsNullOrWhiteSpace(account.Password) ? "" : PasswordMask;
        }

        return config;
    }

    public async Task SaveAsync(AppConfig config, CancellationToken ct)
    {
        var normalized = Normalize(config, keepExistingPasswords: true);
        var json = SerializeConfigFile(normalized);
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(ConfigPath, json, ct);
        SetOwnerOnlyFilePermissions(ConfigPath);
        lock (_sync)
        {
            _config = Clone(normalized);
        }
    }

    public AppConfig Normalize(AppConfig config, bool keepExistingPasswords)
    {
        config ??= new AppConfig();
        var existing = keepExistingPasswords ? GetConfig() : new AppConfig();
        var normalized = new AppConfig
        {
            KeepAliveSeconds = Math.Max(10, config.KeepAliveSeconds <= 0 ? 60 : config.KeepAliveSeconds),
            Accounts = []
        };

        foreach (var account in config.Accounts ?? [])
        {
            var normalizedAccount = NormalizeAccount(account, keepExistingPasswords, existing);
            if (!string.IsNullOrWhiteSpace(normalizedAccount.User))
            {
                normalized.Accounts.Add(normalizedAccount);
            }
        }

        return normalized;
    }

    public AccountConfig NormalizeAccount(AccountConfig account, bool keepExistingPassword)
    {
        return NormalizeAccount(account, keepExistingPassword, GetConfig());
    }

    public string ResolveDeviceCode(AccountConfig account)
    {
        if (!string.IsNullOrWhiteSpace(account.DeviceCode))
        {
            return account.DeviceCode.Trim();
        }

        var deviceKey = SafeName(FirstNotEmpty(account.Name, account.User));
        var devicesDir = Path.Combine(DataDir, "devices");
        var deviceCodePath = Path.Combine(devicesDir, deviceKey);
        deviceCodePath = Path.ChangeExtension(deviceCodePath, ".txt");
        if (File.Exists(deviceCodePath))
        {
            SetOwnerOnlyFilePermissions(deviceCodePath);
            _configNeedsSave = true;
            return File.ReadAllText(deviceCodePath).Trim();
        }

        lock (_sync)
        {
            if (!_generatedDeviceCodes.TryGetValue(deviceKey, out var deviceCode))
            {
                deviceCode = "web_" + GenerateRandomString(32);
                _generatedDeviceCodes[deviceKey] = deviceCode;
                _configNeedsSave = true;
            }

            return deviceCode;
        }
    }

    private AccountConfig NormalizeAccount(AccountConfig account, bool keepExistingPassword, AppConfig existingConfig)
    {
        account ??= new AccountConfig();
        var user = account.User?.Trim();
        var name = FirstNotEmpty(account.Name?.Trim(), user);
        var existing = FindExistingAccount(existingConfig, name, user);
        var password = account.Password;
        if (keepExistingPassword && (string.IsNullOrWhiteSpace(password) || password == PasswordMask))
        {
            password = existing?.Password;
        }

        var normalized = new AccountConfig
        {
            Name = name,
            User = user,
            Password = password?.Trim(),
            DeviceCode = FirstNotEmpty(account.DeviceCode?.Trim(), existing?.DeviceCode)
        };
        if (!string.IsNullOrWhiteSpace(normalized.User))
        {
            normalized.DeviceCode = ResolveDeviceCode(normalized);
        }

        return normalized;
    }

    private AppConfig LoadFromDisk()
    {
        if (!File.Exists(ConfigPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = DeserializeConfigFile(json);
            Utility.WriteLine(ConsoleColor.Green, $"已读取配置文件：{ConfigPath}");
            return config;
        }
        catch (Exception ex)
        {
            Utility.WriteLine(ConsoleColor.Red, $"读取配置文件失败：{ex.Message}");
            return new AppConfig();
        }
    }

    private AppConfig LoadFromEnvironment()
    {
        var user = Environment.GetEnvironmentVariable("APP_USER");
        var password = Environment.GetEnvironmentVariable("APP_PASSWORD");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new AppConfig
        {
            Accounts =
            [
                new AccountConfig
                {
                    Name = Environment.GetEnvironmentVariable("APP_NAME"),
                    User = user,
                    Password = password,
                    DeviceCode = Environment.GetEnvironmentVariable("DEVICECODE")
                }
            ]
        };
    }

    private void Save(AppConfig config)
    {
        var json = SerializeConfigFile(config);
        File.WriteAllText(ConfigPath, json);
        SetOwnerOnlyFilePermissions(ConfigPath);
    }

    private string SerializeConfigFile(AppConfig config)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(config, AppJsonSerializerContext.Default.AppConfig);
        var key = GetConfigEncryptionKey();
        var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcmTagSize];

        using var aes = new AesGcm(key, AesGcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(EncryptedConfigFormat));
        CryptographicOperations.ZeroMemory(plaintext);

        var encrypted = new EncryptedConfigFile
        {
            Format = EncryptedConfigFormat,
            Algorithm = "AES-256-GCM",
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        return JsonSerializer.Serialize(encrypted, AppJsonSerializerContext.Default.EncryptedConfigFile);
    }

    private AppConfig DeserializeConfigFile(string json)
    {
        try
        {
            var encrypted = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.EncryptedConfigFile);
            if (IsEncryptedConfig(encrypted))
            {
                _loadedPlaintextConfig = false;
                return DecryptConfig(encrypted);
            }
        }
        catch (JsonException)
        {
        }

        _loadedPlaintextConfig = true;
        return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppConfig);
    }

    private AppConfig DecryptConfig(EncryptedConfigFile encrypted)
    {
        var key = GetConfigEncryptionKey();
        var nonce = Convert.FromBase64String(encrypted.Nonce);
        var tag = Convert.FromBase64String(encrypted.Tag);
        var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, AesGcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(EncryptedConfigFormat));

        try
        {
            return JsonSerializer.Deserialize(plaintext, AppJsonSerializerContext.Default.AppConfig);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static bool IsEncryptedConfig(EncryptedConfigFile encrypted)
    {
        return encrypted != null &&
               string.Equals(encrypted.Format, EncryptedConfigFormat, StringComparison.Ordinal) &&
               string.Equals(encrypted.Algorithm, "AES-256-GCM", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(encrypted.Nonce) &&
               !string.IsNullOrWhiteSpace(encrypted.Tag) &&
               !string.IsNullOrWhiteSpace(encrypted.Ciphertext);
    }

    private byte[] GetConfigEncryptionKey()
    {
        var configuredKey = Environment.GetEnvironmentVariable(ConfigEncryptionKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            return DeriveKey(configuredKey);
        }

        var keyPath = Path.Combine(DataDir, LocalConfigKeyFileName);
        if (File.Exists(keyPath))
        {
            return Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyPath, Convert.ToBase64String(key));
        SetOwnerOnlyFilePermissions(keyPath);
        Utility.WriteLine(ConsoleColor.Yellow, $"Created local config encryption key: {keyPath}");
        return key;
    }

    private static byte[] DeriveKey(string value)
    {
        var trimmed = value.Trim();
        try
        {
            var decoded = Convert.FromBase64String(trimmed);
            if (decoded.Length == 32)
            {
                return decoded;
            }
        }
        catch (FormatException)
        {
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
    }

    private static void SetOwnerOnlyFilePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Utility.WriteLine(ConsoleColor.Yellow, $"Could not restrict file permissions for {path}: {ex.Message}");
        }
    }

    private static AccountConfig FindExistingAccount(AppConfig config, string name, string user)
    {
        return config.Accounts.FirstOrDefault(account =>
            (!string.IsNullOrWhiteSpace(user) && string.Equals(account.User, user, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(name) && string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, AppJsonSerializerContext.Default.AppConfig);
        return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppConfig) ?? new AppConfig();
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length).Select(s => s[RandomNumberGenerator.GetInt32(s.Length)]).ToArray());
    }

    private static string SafeName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "default" : value;
        var builder = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private static string FirstNotEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string GetDataDir()
    {
        var dataDir = Environment.GetEnvironmentVariable("CTYUN_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            return dataDir;
        }

        return IsRunningInContainer() ? "/app/data" : AppContext.BaseDirectory;
    }

    private static bool IsRunningInContainer() => File.Exists("/.dockerenv");
}
