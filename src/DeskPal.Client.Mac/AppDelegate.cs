using System.Globalization;
using AppKit;
using DeskPal.Client.Collectors;
using DeskPal.Client.Core.Diagnostics;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Native;
using DeskPal.Client.Platform;
using DeskPal.Client.Providers;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObjCRuntime;

namespace DeskPal.Client;

/// <summary>
/// Startup and shutdown of the macOS client, the counterpart of the Windows App: login, background services,
/// the menu bar icon, the panel and the member cards.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    private const string SignedOutArg = "--signed-out";
    private readonly string[] _args;
    private FileStream? _singleInstance;
    private IHost? _host;
    private NSStatusItem? _statusItem;
    private NSMenu? _statusMenu;
    private NSMenuItem? _openItem;
    private NSMenuItem? _quitItem;
    private PanelController? _panel;
    private MemberCards? _cards;
    private CancellationTokenSource? _updates;
    private ClientLogFiles? _logs;
    private bool _exiting;
    private bool _openRequested;

    public AppDelegate(string[] args)
    {
        _args = args;
    }

    public override async void DidFinishLaunching(NSNotification notification)
    {
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            _logs?.Write("ERR", "DeskPal", ex.ToString());
            await new AlertPrompt().AskAsync(PromptKind.Warning, ex.Message);
            NSApplication.SharedApplication.Terminate(this);
        }
    }

    /// <summary>Cmd+Q and the system's Quit go through the same exit as the menu bar menu.</summary>
    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        if (_exiting || _host is null)
        {
            return NSApplicationTerminateReply.Now;
        }

        _ = ExitAsync();
        return NSApplicationTerminateReply.Cancel;
    }

    /// <summary>Opening the app again (Finder, Spotlight, Launchpad) shows the panel.</summary>
    public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
    {
        _panel?.ShowAndActivate();
        return true;
    }

    private async Task StartAsync()
    {
        // TimeZoneInfo.Local keeps the zone the process started in until the cache is cleared.
        NSNotificationCenter.DefaultCenter.AddObserver(NSSystemClockDidChangeNotification, _ => TimeZoneInfo.ClearCachedData());
        NSNotificationCenter.DefaultCenter.AddObserver(NSTimeZoneDidChangeNotification, _ => TimeZoneInfo.ClearCachedData());
        _logs = new ClientLogFiles(AppPaths.LogDirectory);
        RegisterCrashHandlers(_logs);
        var store = new SettingsStore();
        var settings = store.Load();
        UiLanguage.Apply(UiLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture));
        BuildMainMenu();

        _singleInstance = await LockSingleInstanceAsync();
        if (_singleInstance is null)
        {
            _logs.Write("INF", "DeskPal", "Already running");
            await new AlertPrompt().AskAsync(PromptKind.Info, Strings.App_AlreadyRunningMac);
            _logs.Dispose();
            NSApplication.SharedApplication.Terminate(this);
            return;
        }

        _logs.Write("INF", "DeskPal", "Starting");
        var api = new ApiClient(settings);
        var notice = new Ref<string?>(_args.Contains(SignedOutArg) ? Strings.Login_SignedOutElsewhere : null);
        if (!await ClientStartup.RestoreSessionAsync(settings, store, api, _logs, notice)
            && !await new LoginController(settings, store, api, notice.Value).ShowAsync())
        {
            _logs.Write("INF", "DeskPal", "Stopped before the panel opened");
            _logs.Dispose();
            _logs = null;
            _singleInstance.Dispose();
            _singleInstance = null;
            _exiting = true;
            NSApplication.SharedApplication.Terminate(this);
            return;
        }

        UiLanguage.Apply(UiLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture));

        // Window titles and full screen need Accessibility. macOS shows its own dialog the first time, and again
        // after an unsigned update, which macOS treats as a different app.
        if (!MacNative.AccessibilityTrusted(prompt: false))
        {
            _logs.Write("INF", "DeskPal", "Accessibility not allowed; asking");
            MacNative.AccessibilityTrusted(prompt: true);
        }

        _host = BuildHost(settings, store, api, _logs);
        var services = _host.Services;
        services.GetRequiredService<ILogger<AppDelegate>>().LogInformation("Ready version {Version}", services.GetRequiredService<UpdateService>().VersionLabel);
        var rooms = services.GetRequiredService<RoomState>();
        var hub = services.GetRequiredService<HubService>();
        var updates = services.GetRequiredService<UpdateService>();
        var bridge = services.GetRequiredService<PanelBridge>();
        updates.Exit = restart => MainThread.InvokeAsync(async () =>
        {
            await ExitAsync(applyUpdate: true, restart: restart);
            return true;
        });
        bridge.Logout = () => MainThread.InvokeAsync(async () =>
        {
            await LogoutAsync();
            return true;
        });
        hub.SessionEnded += OnSessionEnded;
        api.Unauthorized += OnSessionEnded;

        CreateStatusItem(updates, bridge);
        rooms.Notify += MacPlatform.Notify;
        ClientStartup.Wire(services);
        await _host.StartAsync();
        hub.Start();
        _ = ClientStartup.RefreshBuiltinRulesAsync(services);
        _ = RefreshRoomsAsync(rooms);
        _updates = new CancellationTokenSource();
        _ = updates.RunPeriodicAsync(_updates.Token);

        _panel = services.GetRequiredService<PanelController>();
        _panel.QuitRequested += () => _ = ExitAsync();
        _cards = services.GetRequiredService<MemberCards>();
        if (_openRequested || !_args.Contains("--minimized"))
        {
            _panel.ShowAndActivate();
        }
    }

    /// <summary>
    /// An exclusive lock on a file next to the settings; .NET takes it with flock, so it is released when the process
    /// ends however it ends. A relaunch right after exit may find it still held for a moment, so it retries briefly.
    /// </summary>
    private static async Task<FileStream?> LockSingleInstanceAsync()
    {
        Directory.CreateDirectory(SettingsStore.Directory);
        var path = Path.Combine(SettingsStore.Directory, "instance.lock");
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }

        return null;
    }

    private static NSString NSSystemClockDidChangeNotification => new("NSSystemClockDidChangeNotification");

    private static NSString NSTimeZoneDidChangeNotification => new("NSSystemTimeZoneDidChangeNotification");

    private static void RegisterCrashHandlers(ClientLogFiles logs)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                logs.Write("ERR", "Unhandled", ex.ToString());
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => logs.Write("ERR", "Unobserved", args.Exception.ToString());
        Runtime.MarshalManagedException += (_, args) => logs.Write("ERR", "Unhandled", args.Exception.ToString());
    }

    private static IHost BuildHost(AppSettings settings, SettingsStore store, ApiClient api, ClientLogFiles logs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ClientFileLoggerProvider(logs));

        var s = builder.Services;
        s.AddDeskPalClient(settings, store, api, logs, new MacPlatform());
        s.AddSingleton<ForegroundState>();

        // The collectors are the official macOS plugin: listed in Settings with the user's plugins, and their facts
        // reach the presence pipeline only for what no plugin above them supplies.
        s.AddOfficialProvider(
            "official:macos",
            "macOS",
            ProviderCapabilities.All.ToHashSet(),
            typeof(ForegroundCollector),
            typeof(BrowserDomainCollector),
            typeof(IdleCollector),
            typeof(SessionCollector),
            typeof(MediaCollector));
        s.AddSingleton<PanelController>();
        s.AddSingleton<MemberCards>();
        return builder.Build();
    }

    /// <summary>
    /// The menu bar icon. Left click opens or hides the panel; right click (or Control-click) shows Open and Quit,
    /// like the Windows tray icon.
    /// </summary>
    private void CreateStatusItem(UpdateService updates, PanelBridge bridge)
    {
        var item = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Square);
        var button = item.Button;
        var image = NSImage.GetSystemSymbol("person.2.fill", "DeskPal");
        if (image is not null)
        {
            image.Template = true;
            button.Image = image;
        }
        else
        {
            button.Title = "DP";
        }

        button.ToolTip = $"DeskPal {updates.VersionLabel}";
        button.SendActionOn(NSEventType.LeftMouseUp | NSEventType.RightMouseUp);
        button.Activated += (_, _) => OnStatusItemClick();

        _openItem = new NSMenuItem(Strings.Tray_Open, (_, _) => _panel?.ShowAndActivate());
        _quitItem = new NSMenuItem(Strings.Tray_Exit, (_, _) => _ = ExitAsync());
        _statusMenu = new NSMenu();
        _statusMenu.AddItem(_openItem);
        _statusMenu.AddItem(NSMenuItem.SeparatorItem);
        _statusMenu.AddItem(_quitItem);
        bridge.LanguageChanged += () => MainThread.Post(() =>
        {
            _openItem.Title = Strings.Tray_Open;
            _quitItem.Title = Strings.Tray_Exit;
        });
        _statusItem = item;
    }

    private void OnStatusItemClick()
    {
        var current = NSApplication.SharedApplication.CurrentEvent;
        var secondary = current is not null
            && (current.Type == NSEventType.RightMouseUp || current.ModifierFlags.HasFlag(NSEventModifierMask.ControlKeyMask));
        if (secondary && _statusItem is not null && _statusMenu is not null)
        {
            // Attach the menu only for this click, so a left click keeps toggling the panel.
            _statusItem.Menu = _statusMenu;
            _statusItem.Button.PerformClick(null!);
            _statusItem.Menu = null!;
            return;
        }

        if (_panel is null)
        {
            _openRequested = true;
        }
        else if (_panel.IsVisible && !_panel.JustShown)
        {
            _panel.Hide();
        }
        else
        {
            _panel.ShowAndActivate();
        }
    }

    /// <summary>
    /// The menu bar stays hidden for a menu bar app, but its key equivalents still work: without an Edit menu,
    /// Cmd+C, Cmd+V and Cmd+A do nothing in the page's text fields.
    /// </summary>
    private static void BuildMainMenu()
    {
        var main = new NSMenu();
        var appMenu = new NSMenu();
        appMenu.AddItem(new NSMenuItem("Hide DeskPal", new Selector("hide:"), "h"));
        appMenu.AddItem(new NSMenuItem("Quit DeskPal", new Selector("terminate:"), "q"));
        main.AddItem(new NSMenuItem("DeskPal") { Submenu = appMenu });

        var edit = new NSMenu("Edit");
        edit.AddItem(new NSMenuItem("Undo", new Selector("undo:"), "z"));
        edit.AddItem(new NSMenuItem("Redo", new Selector("redo:"), "Z"));
        edit.AddItem(NSMenuItem.SeparatorItem);
        edit.AddItem(new NSMenuItem("Cut", new Selector("cut:"), "x"));
        edit.AddItem(new NSMenuItem("Copy", new Selector("copy:"), "c"));
        edit.AddItem(new NSMenuItem("Paste", new Selector("paste:"), "v"));
        edit.AddItem(new NSMenuItem("Select All", new Selector("selectAll:"), "a"));
        main.AddItem(new NSMenuItem("Edit") { Submenu = edit });

        var window = new NSMenu("Window");
        window.AddItem(new NSMenuItem("Close", new Selector("performClose:"), "w"));
        window.AddItem(new NSMenuItem("Minimize", new Selector("performMiniaturize:"), "m"));
        main.AddItem(new NSMenuItem("Window") { Submenu = window });
        NSApplication.SharedApplication.MainMenu = main;
    }

    private async Task RefreshRoomsAsync(RoomState rooms)
    {
        if (await ClientStartup.RefreshRoomsAsync(rooms) is { } ex)
        {
            MacPlatform.Notify("DeskPal", UiLanguage.Format(Strings.Tray_LoadRoomsFailed, ex.Message));
        }
    }

    private async Task LogoutAsync()
    {
        if (_host is not null)
        {
            await ClientStartup.SignOutServicesAsync(_host.Services);
        }

        await RestartSignedOutAsync(null);
    }

    private void OnSessionEnded() => MainThread.Post(async () =>
    {
        if (_exiting || _host is null)
        {
            return;
        }

        _logs?.Write("INF", "DeskPal", "Signed out because the account signed in elsewhere");
        await RestartSignedOutAsync(SignedOutArg);
    });

    private async Task RestartSignedOutAsync(string? arg)
    {
        var services = _host!.Services;
        var settings = services.GetRequiredService<AppSettings>();
        settings.SetToken(null);
        services.GetRequiredService<SettingsStore>().Save(settings);
        await ExitAsync(applyUpdate: false, relaunchArg: arg ?? "");
    }

    /// <param name="relaunchArg">Not null: start a new copy with this argument once this one has let go of its lock.</param>
    private async Task ExitAsync(bool applyUpdate = true, bool restart = false, string? relaunchArg = null)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _logs?.Write("INF", "DeskPal", restart ? "Restarting" : "Exiting");
        var updates = _host?.Services.GetService<UpdateService>();

        _cards?.CloseAll();
        _panel?.CloseForExit();

        if (_host is not null)
        {
            await _host.Services.GetRequiredService<HubService>().DisposeAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
            _host = null;
        }

        _logs?.Dispose();
        _logs = null;

        if (_statusItem is not null)
        {
            NSStatusBar.SystemStatusBar.RemoveStatusItem(_statusItem);
        }

        _singleInstance?.Dispose();
        _singleInstance = null;
        _updates?.Cancel();
        if (relaunchArg is not null)
        {
            MacPlatform.Relaunch(relaunchArg.Length == 0 ? null : relaunchArg);
        }
        else if (applyUpdate && updates is not null && updates.ApplyReady(restart, includeDowngrade: restart))
        {
            return;
        }

        NSApplication.SharedApplication.Terminate(this);
    }
}
