using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskPal.Client.Core.Usage;
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

    /// <summary>The user chose not to enter any room. Selecting a room, creating one or joining one clears it.</summary>
    public bool NoRoom { get; set; }
    public int IdleMinutes { get; set; } = 5;
    /// <summary>Not saved: older settings files carry 30, which must not override the current default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int StableSeconds { get; set; } = 10;
    public bool Paused { get; set; }

    public bool Topmost { get; set; }

    /// <summary>ask, minimize, or tray. ask shows a choice the next time the close button is used.</summary>
    public string CloseAction { get; set; } = "ask";

    /// <summary>system follows Windows, light and dark are fixed.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>system follows the Windows display language; zh-CN or en is fixed.</summary>
    public string Language { get; set; } = "system";

    /// <summary>Copy of the account's answer on sharing usage data. null means not asked yet.</summary>
    public bool? ShareUsage { get; set; }

    /// <summary>Copy of whether the server lists this account as an admin. Shows the signup invites in Settings.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>null 表示沿用安装渠道：测试版安装包继续接收测试版。</summary>
    public bool? IncludePrerelease { get; set; }

    /// <summary>Identifies this computer in exported usage files, so importing them twice does not count twice.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Days raw usage segments are kept; older days keep only per-day totals.</summary>
    public int UsageRetentionDays { get; set; } = 90;

    /// <summary>Apps, processes, sites and title keywords whose titles are never recorded in the usage stats.</summary>
    public List<NoTitleRule> NoTitleRules { get; set; } = [];

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