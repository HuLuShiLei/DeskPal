using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskPal.Shared;

namespace DeskPal.Client.Services;

public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "https://deskpal.hafuhafu.cn:36443";
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? ProtectedToken { get; set; }
    public Guid? SelectedRoomId { get; set; }
    public int IdleMinutes { get; set; } = 5;
    /// <summary>Not saved: older settings files carry 30, which must not override the current default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int StableSeconds { get; set; } = 10;
    public bool Paused { get; set; }
    public string? ManualText { get; set; }

    public bool Topmost { get; set; }

    /// <summary>system follows Windows, light and dark are fixed.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Copy of the account's answer on sharing usage data. null means not asked yet.</summary>
    public bool? ShareUsage { get; set; }

    /// <summary>null 表示沿用安装渠道：测试版安装包继续接收测试版。</summary>
    public bool? IncludePrerelease { get; set; }

    public string? GetToken()
    {
        if (string.IsNullOrEmpty(ProtectedToken))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void SetToken(string? token) => ProtectedToken = token is null
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
}

public sealed class SettingsStore
{
    public static readonly string Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskPal");

    private static readonly string FilePath = Path.Combine(Directory, "settings.json");
    private static readonly JsonSerializerOptions Options = new(DeskPalJson.Options) { WriteIndented = true };
    private readonly Lock _lock = new();

    public AppSettings Load()
    {
        lock (_lock)
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
    }
}