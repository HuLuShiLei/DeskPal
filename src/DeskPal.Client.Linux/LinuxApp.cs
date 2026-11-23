using System.Diagnostics;
using System.Globalization;
using DeskPal.Client.Collectors;
using DeskPal.Client.Core.Diagnostics;
using DeskPal.Client.Platform;
using DeskPal.Client.Providers;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client;

/// <summary>
/// Startup and shutdown of the Linux client, the counterpart of the Windows App and the macOS AppDelegate: login,
/// background services, the tray icon and the panel.
/// GApplication keeps one instance per session: starting DeskPal again only asks the running one to show its
/// panel, which is also how the panel is opened on desktops that show no tray icons.
/// </summary>
public sealed class LinuxApp
{
    public const string ApplicationId = "io.github.hulushilei.DeskPal";
    private const string SignedOutArg = "--signed-out";
    private readonly string[] _args;
    private readonly Gtk.Application _application;
    private IHost? _host;
    private TrayIcon? _tray;
    private PanelController? _panel;
    private CancellationTokenSource? _updates;
    private ClientLogFiles? _logs;
    private UiTimer? _timeZone;
    private bool _starting;
    private bool _exiting;
    private bool _openRequested;

    public LinuxApp(string[] args)
    {
        _args = args;
        _application = Gtk.Application.New(ApplicationId, Gio.ApplicationFlags.DefaultFlags);
        _application.OnStartup += (_, _) => OnStartup();
        _application.OnActivate += (_, _) => OnActivate();
    }

    /// <summary>DeskPal's own options are read from <see cref="_args"/>; GApplication gets none, it would reject them.</summary>
    /// <remarks>
    /// The synchronization context is installed here and not in OnStartup: an async method puts the caller's
    /// context back when it returns, so one set inside it would be gone after the first await.
    /// </remarks>
    public int Run()
    {
        MainThread.Init();
        return _application.Run([]);
    }

    /// <summary>Only in the first instance.</summary>
    private async void OnStartup()
    {
        _application.Hold();
        _starting = true;
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            _logs?.Write("ERR", "DeskPal", ex.ToString());
            await new AlertPrompt().AskAsync(PromptKind.Warning, ex.Message);
            Quit();
        }
        finally
        {
            _starting = false;
        }
    }

    /// <summary>The first start, and every later start of DeskPal while this instance runs.</summary>
    private void OnActivate()
    {
        if (_panel is not null)
        {
            _panel.ShowAndActivate();
        }
        else if (!_starting || _host is not null)
        {
            _openRequested = true;
        }
    }

    private async Task StartAsync()
    {
        // TimeZoneInfo.Local keeps the zone the process started in until the cache is cleared.
        _timeZone = UiTimer.Every(TimeSpan.FromMinutes(10), TimeZoneInfo.ClearCachedData);
        _logs = new ClientLogFiles(AppPaths.LogDirectory);
        RegisterCrashHandlers(_logs);
        var store = new SettingsStore();
        var settings = store.Load();
        UiLanguage.Apply(UiLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture));

        _logs.Write("INF", "DeskPal", "Starting");
        LauncherEntry.Ensure();
        var api = new ApiClient(settings);
        var notice = new Ref<string?>(_args.Contains(SignedOutArg) ? Strings.Login_SignedOutElsewhere : null);
        if (!await ClientStartup.RestoreSessionAsync(settings, store, api, _logs, notice)
            && !await new LoginController(_application, settings, store, api, notice.Value).ShowAsync())
        {
            _logs.Write("INF", "DeskPal", "Stopped before the panel opened");
            _logs.Dispose();
            _logs = null;
            _exiting = true;
            Quit();
            return;
        }

        UiLanguage.Apply(UiLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture));

        _host = BuildHost(settings, store, api, _logs, _application);
        var services = _host.Services;
        var logger = services.GetRequiredService<ILogger<LinuxApp>>();
        logger.LogInformation(
            "Ready version {Version} on {Desktop} ({Session})",
            services.GetRequiredService<UpdateService>().VersionLabel,
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"));
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
        // SIGTERM (logout, kill) reaches the host's lifetime first; the GTK loop has to end with it.
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(
            () => MainThread.Post(() => _ = ExitAsync(applyUpdate: false)));
        hub.SessionEnded += OnSessionEnded;
        api.Unauthorized += OnSessionEnded;

        _tray = new TrayIcon($"DeskPal {updates.VersionLabel}", logger);
        _tray.Activated += TogglePanel;
        _tray.QuitRequested += () => _ = ExitAsync();
        bridge.LanguageChanged += () => MainThread.Post(() => _tray?.RefreshMenu());
        await _tray.StartAsync();

        rooms.Notify += LinuxPlatform.Notify;
        ClientStartup.Wire(services);
        await _host.StartAsync();
        hub.Start();
        _ = ClientStartup.RefreshBuiltinRulesAsync(services);
        _ = RefreshRoomsAsync(rooms);
        _updates = new CancellationTokenSource();
        _ = updates.RunPeriodicAsync(_updates.Token);

        _panel = services.GetRequiredService<PanelController>();
        _panel.QuitRequested += () => _ = ExitAsync();

        // Without a tray icon a hidden start would leave nothing to click, so the panel opens anyway.
        if (_openRequested || !_args.Contains("--minimized") || !_tray.Registered)
        {
            _panel.ShowAndActivate();
        }
    }

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
    }

    private static IHost BuildHost(AppSettings settings, SettingsStore store, ApiClient api, ClientLogFiles logs, Gtk.Application application)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ClientFileLoggerProvider(logs));

        var s = builder.Services;
        s.AddDeskPalClient(settings, store, api, logs, new LinuxPlatform());
        s.AddSingleton(application);
        s.AddSingleton<ForegroundService>();
        s.AddSingleton<IProviderControl>(sp => sp.GetRequiredService<ForegroundService>());
        s.AddHostedService(sp => sp.GetRequiredService<ForegroundService>());
        s.AddHostedService<IdleCollector>();
        s.AddHostedService<SessionCollector>();
        s.AddHostedService<MediaCollector>();
        s.AddSingleton<PanelController>();
        return builder.Build();
    }

    private void TogglePanel()
    {
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

    private static async Task RefreshRoomsAsync(RoomState rooms)
    {
        if (await ClientStartup.RefreshRoomsAsync(rooms) is { } ex)
        {
            LinuxPlatform.Notify("DeskPal", UiLanguage.Format(Strings.Tray_LoadRoomsFailed, ex.Message));
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

    /// <param name="relaunchArg">Not null: start a new copy with this argument once this one has gone.</param>
    private async Task ExitAsync(bool applyUpdate = true, bool restart = false, string? relaunchArg = null)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _logs?.Write("INF", "DeskPal", restart ? "Restarting" : "Exiting");
        var updates = _host?.Services.GetService<UpdateService>();

        _panel?.CloseForExit();
        _tray?.Stop();
        _timeZone?.Stop();

        if (_host is not null)
        {
            await _host.Services.GetRequiredService<HubService>().DisposeAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
            _host = null;
        }

        _logs?.Dispose();
        _logs = null;
        _updates?.Cancel();
        if (relaunchArg is not null)
        {
            RelaunchAfterExit(relaunchArg.Length == 0 ? null : relaunchArg);
        }
        else if (applyUpdate && updates is not null && updates.ApplyReady(restart, includeDowngrade: restart))
        {
            return;
        }

        Quit();
    }

    /// <summary>
    /// The new copy must start after this one has given up its place on the session bus; started earlier it would
    /// only hand over to this one and exit. A shell waits for this process to end first.
    /// </summary>
    private static void RelaunchAfterExit(string? arg)
    {
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; shift; exec \"$@\"");
        info.ArgumentList.Add("deskpal-relaunch");
        info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(LinuxPlatform.LaunchPath);
        if (arg is not null)
        {
            info.ArgumentList.Add(arg);
        }

        Process.Start(info)?.Dispose();
    }

    private void Quit()
    {
        _exiting = true;
        _application.Quit();
    }
}
