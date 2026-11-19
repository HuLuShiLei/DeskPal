using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskPal.Client.Platform;
using DeskPal.Shared;
using DeskPal.Shared.Api;

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
    /// <summary>One of <see cref="DeskPal.Client.Core.Presence.SwitchDelay.SecondOptions"/>. Older files carry an unsaved stableSeconds, which is ignored.</summary>
    public int SwitchDelaySeconds { get; set; } = DeskPal.Client.Core.Presence.SwitchDelay.DefaultSeconds;

    /// <summary>
    /// A setting that follows the account was changed while the server could not be reached. The next sign-in uploads
    /// this computer's values instead of taking the account's. See <see cref="PreferencesSync"/>.
    /// </summary>
    public bool PreferencesUnsynced { get; set; }
    public bool Paused { get; set; }

    public bool Topmost { get; set; }

    /// <summary>ask, minimize, tray, or quit. ask shows a choice the next time the close button is used. quit leaves the app.</summary>
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

    /// <summary>github or ghproxy. Only on this computer: whether GitHub is reachable differs per network.</summary>
    public string UpdateSource { get; set; } = DeskPal.Client.Core.Updates.UpdatePolicy.GithubSource;

    /// <summary>Identifies this computer in exported usage files, so importing them twice does not count twice.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Where older versions kept the no-title rules. Moved into the personal data file at start, then left empty.</summary>
    public List<NoTitleRule> NoTitleRules { get; set; } = [];

    /// <summary>Copy of whether the account keeps its personal data on the server. Off until turned on in Settings.</summary>
    public bool PersonalSync { get; set; }

    /// <summary>When the away time or a preference last changed here; decides which computer's values a sync folder keeps.</summary>
    public DateTimeOffset? PreferencesUpdatedAt { get; set; }

    /// <summary>Folder the personal data of the signed-in account syncs through. Only on this computer; cleared on sign-out.</summary>
    public string? PersonalFolder { get; set; }

    /// <summary>Per room, the order the user dragged the other members into. Only affects this computer.</summary>
    public Dictionary<Guid, List<Guid>> MemberOrder { get; set; } = [];

    public string? GetToken()
    {
        if (string.IsNullOrEmpty(ProtectedToken))
        {
            return null;
        }

        try
        {
            var bytes = SecretProtection.UnprotectFromBase64(ProtectedToken);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public void SetToken(string? token) => ProtectedToken = token is null
        ? null
        : SecretProtection.ProtectToBase64(Encoding.UTF8.GetBytes(token));
}

public sealed class SettingsStore
{
    /// <summary>%APPDATA%\DeskPal on Windows, ~/Library/Application Support/DeskPal on macOS.</summary>
    public static readonly string Directory = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskPal");

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