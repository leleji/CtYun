using System.Text.Json.Serialization;

namespace CtYun.Models;

public sealed class StatusResponse
{
    [JsonPropertyName("configured")]
    public bool Configured { get; set; }

    [JsonPropertyName("dataDir")]
    public string DataDir { get; set; }

    [JsonPropertyName("configPath")]
    public string ConfigPath { get; set; }

    [JsonPropertyName("keepAliveSeconds")]
    public int KeepAliveSeconds { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("accounts")]
    public List<AccountRuntimeState> Accounts { get; set; } = [];

    [JsonPropertyName("events")]
    public List<ServiceEvent> Events { get; set; } = [];
}

public sealed class AccountRuntimeState
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("user")]
    public string User { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("desktopCount")]
    public int DesktopCount { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("desktops")]
    public List<DesktopRuntimeState> Desktops { get; set; } = [];
}

public sealed class DesktopRuntimeState
{
    [JsonPropertyName("desktopId")]
    public string DesktopId { get; set; }

    [JsonPropertyName("desktopCode")]
    public string DesktopCode { get; set; }

    [JsonPropertyName("desktopName")]
    public string DesktopName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ServiceEvent
{
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}

public sealed class AccountActionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("bondedDevice")]
    public bool BondedDevice { get; set; }

    [JsonPropertyName("smsSent")]
    public bool SmsSent { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}

public sealed class BindDeviceRequest
{
    [JsonPropertyName("account")]
    public AccountConfig Account { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }
}

public sealed class ApiMessage
{
    public ApiMessage(string message)
    {
        Message = message;
    }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}

public sealed class AdminCredentialFile
{
    [JsonPropertyName("passwordSalt")]
    public string PasswordSalt { get; set; }

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; }

    [JsonPropertyName("mustChangePassword")]
    public bool MustChangePassword { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AdminAuthStatusResponse
{
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }

    [JsonPropertyName("mustChangePassword")]
    public bool MustChangePassword { get; set; }
}

public sealed class AdminLoginRequest
{
    [JsonPropertyName("password")]
    public string Password { get; set; }
}

public sealed class AdminChangePasswordRequest
{
    [JsonPropertyName("currentPassword")]
    public string CurrentPassword { get; set; }

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; set; }
}

public sealed class EncryptedConfigFile
{
    [JsonPropertyName("format")]
    public string Format { get; set; }

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; }

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; }

    [JsonPropertyName("tag")]
    public string Tag { get; set; }

    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; }
}
