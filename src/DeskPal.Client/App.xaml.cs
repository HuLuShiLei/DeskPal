using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DeskPal.Client.Collectors;
using DeskPal.Client.Core.Diagnostics;
using DeskPal.Client.Native;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private IHost? _host;
    private TaskbarIcon? _tray;
    private PanelWindow? _panel;
    private MemberCards? _cards;
    private CancellationTokenSource? _updates;
    private ClientLogFiles? _logs;
    private bool _exiting;
    private bool _openRequested;
    private long _lastTrayClick;

    private const string SignedOutArg = "--signed-out";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // TimeZoneInfo.Local and DateTime.Now keep the time zone the process started in until the cache is cleared.
        Microsoft.Win32.SystemEvents.TimeChanged += (_, _) => TimeZoneInfo.ClearCachedData();
        _logs = new ClientLogFiles(AppPaths.LogDirectory);
        RegisterCrashHandlers(_logs);
        var store = new SettingsStore();
        var settings = store.Load();
        UiLanguage.Apply(UiLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture));
        _singleInstance = new Mutex(false, "DeskPal.Client.SingleInstance");
        if (!_singleInstance.WaitOne(TimeSpan.FromSeconds(3)))
        {
            _logs.Write("INF", "DeskPal", "Already running");
            MessageBox.Show(Strings.App_AlreadyRunning, "DeskPal");
            _singleInstance = null;
            _logs.Dispose();
            Shutdown();
            return;
        }

        _logs.Write("INF", "DeskPal", "Starting");
        var api = new ApiClient(settings);
        var notice = e.Args.Contains(SignedOutArg) ? Strings.Login_SignedOutElsewhere : null;
        if (!await EnsureLoggedInAsync(settings, store, api, _logs, notice))
        {
            _logs.Write("INF", "DeskPal", "Stopped before the panel opened");
            _logs.Dispose();
            Shutdown();
            return;
        }

        UiLanguage.Apply(UiLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture));
        _host = BuildHost(settings, store, api, _logs);
        var services = _host.Services;
        services.GetRequiredService<ILogger<App>>().LogInformation("Ready version {Version}", services.GetRequiredService<UpdateService>().VersionLabel);
        var rooms = services.GetRequiredService<RoomState>();
        var hub = services.GetRequiredService<HubService>();
        var updates = services.GetRequiredService<UpdateService>();
        updates.Exit = restart => ExitAsync(applyUpdate: true, restart: restart);
        services.GetRequiredService<PanelBridge>().Logout = LogoutAsync;
        hub.SessionEnded += OnSessionEnded;
        api.Unauthorized += OnSessionEnded;

        _tray = CreateTray(updates, services.GetRequiredService<PanelBridge>());
        rooms.Notify += (title, message) => Dispatcher.BeginInvoke(() => _tray.ShowNotification(title, message));
        ClientStartup.Wire(services);
        await _host.StartAsync();
        hub.Start();
        _ = ClientStartup.RefreshBuiltinRulesAsync(services);
        _ = RefreshRoomsAsync(rooms);
        _updates = new CancellationTokenSource();
        _ = updates.RunPeriodicAsync(_updates.Token);

        _panel = services.GetRequiredService<PanelWindow>();
        _panel.QuitRequested += async (_, _) => await ExitAsync();
        _cards = services.GetRequiredService<MemberCards>();
        if (!WebUi.RuntimeAvailable())
        {
            _ = Dispatcher.BeginInvoke(() => WebUi.ShowMissingRuntime(Strings.Runtime_PanelUnavailable));
        }
        else if (_openRequested || !e.Args.Contains("--minimized"))
        {
            _panel.ShowAndActivate();
        }
    }

    private static async Task<bool> EnsureLoggedInAsync(AppSettings settings, SettingsStore store, ApiClient api, ClientLogFiles logs, string? notice)
    {
        var reason = new Ref<string?>(notice);
        if (await ClientStartup.RestoreSessionAsync(settings, store, api, logs, reason))
        {
            return true;
        }

        notice = reason.Value;
        if (!WebUi.RuntimeAvailable())
        {
            WebUi.ShowMissingRuntime(Strings.Runtime_LoginUnavailable);
            return false;
        }

        return new LoginWindow(settings, store, api, notice).ShowDialog() == true;
    }

    private static void RegisterCrashHandlers(ClientLogFiles logs)
    {
        Current.DispatcherUnhandledException += (_, args) => logs.Write("ERR", "Unhandled", args.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                logs.Write("ERR", "Unhandled", ex.ToString());
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => logs.Write("ERR", "Unobserved", args.Exception.ToString());
    }

    private static IHost BuildHost(AppSettings settings, SettingsStore store, ApiClient api, ClientLogFiles logs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(new ClientFileLoggerProvider(logs));

        var s = builder.Services;
        s.AddDeskPalClient(settings, store, api, logs, new WindowsPlatform());
        s.AddSingleton<ForegroundState>();
        s.AddHostedService<ForegroundCollector>();
        s.AddHostedService<BrowserDomainCollector>();
        s.AddHostedService<IdleCollector>();
        s.AddHostedService<SessionCollector>();
        s.AddHostedService<MediaCollector>();
        s.AddSingleton<PanelWindow>();
        s.AddSingleton<MemberCards>();
        return builder.Build();
    }

    private async Task RefreshRoomsAsync(RoomState rooms)
    {
        if (await ClientStartup.RefreshRoomsAsync(rooms) is { } ex)
        {
            _tray?.ShowNotification("DeskPal", UiLanguage.Format(Strings.Tray_LoadRoomsFailed, ex.Message));
        }
    }

    private TaskbarIcon CreateTray(UpdateService updates, PanelBridge bridge)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = Strings.Tray_Open };
        open.Click += (_, _) => _panel?.ShowAndActivate();
        var exit = new MenuItem { Header = Strings.Tray_Exit };
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        bridge.LanguageChanged += () => Dispatcher.BeginInvoke(() =>
        {
            open.Header = Strings.Tray_Open;
            exit.Header = Strings.Tray_Exit;
        });

        var tray = new TaskbarIcon
        {
            ToolTipText = $"DeskPal {updates.VersionLabel}",
            NoLeftClickDelay = true,
            ContextMenu = menu,
            Icon = LoadTrayIcon(),
        };
        tray.TrayLeftMouseUp += (_, _) => OnTrayClick();
        tray.ForceCreate(false);
        return tray;
    }

    /// <summary>
    /// Each click of a double-click raises its own mouse up. Only the first one counts, so a double-click opens the
    /// panel instead of opening and hiding it. Clicks during startup open the panel once it exists.
    /// </summary>
    private void OnTrayClick()
    {
        var now = Environment.TickCount64;
        if (now - _lastTrayClick < NativeMethods.GetDoubleClickTime())
        {
            return;
        }

        _lastTrayClick = now;
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

    // 按系统小图标尺寸取 ico 里最接近的一张，避免缩放高 DPI 时托盘图标发虚。
    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/DeskPal.ico")).Stream;
        var size = NativeMethods.GetSystemMetrics(NativeMethods.SmCxSmIcon);
        return new System.Drawing.Icon(stream, size, size);
    }

    private async Task LogoutAsync()
    {
        if (_host is not null)
        {
            await ClientStartup.SignOutServicesAsync(_host.Services);
        }

        await RestartSignedOutAsync(null);
    }

    private void OnSessionEnded() => Dispatcher.BeginInvoke(async () =>
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
        await ExitAsync(applyUpdate: false);
        Process.Start(Environment.ProcessPath!, arg ?? "");
    }

    private async Task ExitAsync(bool applyUpdate = true, bool restart = false)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _logs?.Write("INF", "DeskPal", restart ? "Restarting" : "Exiting");
        var updates = _host?.Services.GetService<UpdateService>();

        _cards?.CloseAll();
        if (_panel is not null)
        {
            _panel.AllowClose = true;
            _panel.Close();
        }

        if (_host is not null)
        {
            await _host.Services.GetRequiredService<HubService>().DisposeAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
            _host = null;
        }

        _logs?.Dispose();
        _logs = null;

        _tray?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance = null;
        if (applyUpdate && updates is not null && updates.ApplyReady(restart, includeDowngrade: restart))
        {
            return;
        }

        _updates?.Cancel();
        Shutdown();
    }
}
