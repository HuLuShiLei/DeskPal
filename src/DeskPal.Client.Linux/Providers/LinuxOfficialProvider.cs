using DeskPal.Client.Core.Providers;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Platform;
using DeskPal.Client.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// The plugin that comes with DeskPal on Linux. Window: Xlib on X11, the DeskPal GNOME Shell extension on GNOME with
/// Wayland; on other Wayland desktops it does not claim the window, so a user plugin can supply it. Site: the DeskPal
/// browser extension through <see cref="BrowserBridge"/>. Idle, lock and media: the usual desktop services.
/// Its buttons install the GNOME extension and hand out the browser extension.
/// </summary>
public sealed class LinuxOfficialProvider : OfficialProvider
{
    public const string ProviderId = "official:linux";

    private static readonly IReadOnlySet<ProviderCapability> Everything = ProviderCapabilities.All.ToHashSet();
    private static readonly IReadOnlySet<ProviderCapability> AllButWindow = ProviderCapabilities.All.Where(c => c != ProviderCapability.Foreground).ToHashSet();

    private readonly LinuxStatus _status;
    private readonly IServiceProvider _services;
    private readonly ILogger<LinuxOfficialProvider> _logger;

    public LinuxOfficialProvider(IServiceProvider services, LinuxStatus status, ILogger<LinuxOfficialProvider> logger)
        : base(services, ProviderId, "Linux", status.Session.Kind == "other" ? AllButWindow : Everything,
            [typeof(LinuxWindowSource), typeof(BrowserBridge), typeof(Collectors.IdleCollector), typeof(Collectors.SessionCollector), typeof(Collectors.MediaCollector)])
    {
        _services = services;
        _status = status;
        _logger = logger;
        status.Changed += RaiseChanged;
    }

    protected override void OnStopped() => _status.Reset();

    public override IReadOnlyList<PluginNote> Notes
    {
        get
        {
            var notes = new List<PluginNote>(base.Notes);
            switch (_status.Window)
            {
                case "ok":
                    notes.Add(new PluginNote(_status.Session.Kind == "gnome" ? "linux.gnome" : "linux.x11"));
                    break;
                case "needsExtension":
                    notes.Add(new PluginNote("linux.needsExtension", null, "warn"));
                    break;
                case "needsRelogin":
                    notes.Add(new PluginNote("linux.needsRelogin", null, "warn"));
                    break;
                case "unavailable":
                    notes.Add(new PluginNote("linux.noWindowSource", _status.Session.Desktop.Length > 0 ? _status.Session.Desktop : "Wayland", "warn"));
                    break;
            }

            if (_status.BridgeError is { } error)
            {
                notes.Add(new PluginNote("linux.bridgeFailed", error, "warn"));
            }
            else if (_status.Browsers.Count > 0)
            {
                notes.Add(new PluginNote("linux.browserConnected", string.Join(", ", _status.Browsers)));
            }
            else
            {
                notes.Add(new PluginNote("linux.browserMissing"));
            }

            return notes;
        }
    }

    public override IReadOnlyList<string> Actions =>
        _status.Session.Kind == "gnome" && _status.Window == "needsExtension"
            ? ["linux.installGnomeExtension", "linux.browserExtension"]
            : ["linux.browserExtension"];

    public override async Task<CommandResult> RunActionAsync(string action)
    {
        switch (action)
        {
            case "linux.installGnomeExtension":
                try
                {
                    await GnomeExtension.InstallAsync();
                    _logger.LogInformation("GNOME extension installed");
                    _status.SetWindow("needsRelogin");
                    return new CommandResult(true, Data: new { message = Strings.Plugin_GnomeExtensionInstalled });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Installing the GNOME extension failed");
                    return CommandResult.Fail(ex.Message);
                }

            case "linux.browserExtension":
                try
                {
                    var folder = BrowserExtensionFiles.Export();
                    _services.GetRequiredService<IClientPlatform>().OpenExternal(folder);
                    return new CommandResult(true, Data: new { message = Strings.Plugin_BrowserExtensionReady, folder });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Copying the browser extension failed");
                    return CommandResult.Fail(ex.Message);
                }

            default:
                return await base.RunActionAsync(action);
        }
    }
}

/// <summary>
/// The browser extension ships inside the app (browser-extension/). An AppImage is mounted at a different temporary
/// path on every start, and a browser loads an unpacked extension from where it was picked, so it is copied to a
/// lasting place first: ~/.local/share/DeskPal/browser-extension.
/// </summary>
internal static class BrowserExtensionFiles
{
    public static string Bundled => Path.Combine(AppContext.BaseDirectory, "browser-extension");

    public static string Exported => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data
            ? data
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "DeskPal",
        "browser-extension");

    /// <summary>Copies the bundled extension over the exported one and returns its folder.</summary>
    public static string Export()
    {
        if (!Directory.Exists(Bundled))
        {
            throw new IOException($"{Bundled} is missing.");
        }

        Directory.CreateDirectory(Exported);
        foreach (var file in Directory.EnumerateFiles(Bundled, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Exported, Path.GetRelativePath(Bundled, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        return Exported;
    }
}
