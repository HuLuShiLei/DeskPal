using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskPal.Client.Collectors;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Diagnostics;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Updates;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using DeskPal.Client.Views;
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

    private const string SignedOutArg = "--signed-out";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logs = new ClientLogFiles(AppPaths.LogDirectory);
        RegisterCrashHandlers(_logs);
        _singleInstance = new Mutex(false, "DeskPal.Client.SingleInstance");
        if (!_singleInstance.WaitOne(TimeSpan.FromSeconds(3)))
        {
            _logs.Write("INF", "DeskPal", "Already running");
            MessageBox.Show("DeskPal 已经在运行，请从任务栏右下角的托盘图标打开。", "DeskPal");
            _singleInstance = null;
            _logs.Dispose();
            Shutdown();
            return;
        }

        _logs.Write("INF", "DeskPal", "Starting");
        var store = new SettingsStore();
        var settings = store.Load();
        var api = new ApiClient(settings);
        var notice = e.Args.Contains(SignedOutArg) ? "账号已在其他电脑登录，请重新登录。" : null;
        if (!await EnsureLoggedInAsync(settings, store, api, _logs, notice))
        {
            _logs.Write("INF", "DeskPal", "Stopped before the panel opened");
            _logs.Dispose();
            Shutdown();
            return;
        }

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

        var personalRules = services.GetRequiredService<PersonalRules>();

        _tray = CreateTray(updates);
        rooms.Notify += (title, message) => Dispatcher.BeginInvoke(() => _tray.ShowNotification(title, message));
        hub.Received += rooms.Handle;
        hub.Received += personalRules.Handle;
        hub.Received += services.GetRequiredService<StatsExchange>().Handle;
        hub.Connected += personalRules.RefreshAsync;

        LoadCachedBuiltinRules(services);
        personalRules.LoadCached();
        await _host.StartAsync();
        hub.Start();
        _ = RefreshBuiltinRulesAsync(services);
        _ = RefreshRoomsAsync(rooms);
        _updates = new CancellationTokenSource();
        _ = updates.RunPeriodicAsync(_updates.Token);

        _panel = services.GetRequiredService<PanelWindow>();
        _cards = services.GetRequiredService<MemberCards>();
        if (!WebUi.RuntimeAvailable())
        {
            _ = Dispatcher.BeginInvoke(() => WebUi.ShowMissingRuntime("面板无法显示。状态同步不受影响。"));
        }
        else if (!e.Args.Contains("--minimized"))
        {
            _panel.ShowAndActivate();
        }
    }

    private static async Task<bool> EnsureLoggedInAsync(AppSettings settings, SettingsStore store, ApiClient api, ClientLogFiles logs, string? notice)
    {
        if (settings.GetToken() is not null)
        {
            try
            {
                var me = await api.MeAsync();
                settings.DisplayName = me.DisplayName;
                settings.ShareUsage = me.ShareUsage;
                store.Save(settings);
                logs.Write("INF", "DeskPal", "Saved session restored");
                return true;
            }
            catch (ApiException ex) when (ex.Status == 0)
            {
                logs.Write("INF", "DeskPal", "Saved session kept because the server was unreachable");
                return true;
            }
            catch (ApiException ex)
            {
                logs.Write("WRN", "DeskPal", $"Saved session rejected ({ex.Status})");
                if (ex.Status == 401)
                {
                    notice ??= "登录已失效，请重新登录。";
                }

                settings.SetToken(null);
                store.Save(settings);
            }
        }

        if (!WebUi.RuntimeAvailable())
        {
            WebUi.ShowMissingRuntime("无法显示登录页面。");
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
        s.AddSingleton(logs);
        s.AddSingleton(settings);
        s.AddSingleton(store);
        s.AddSingleton(api);
        s.AddSingleton<ActivityBus>();
        s.AddSingleton<LayeredClassifier>();
        s.AddSingleton(new PersonalRuleStore(Path.Combine(SettingsStore.Directory, "personal-rules.json")));
        s.AddSingleton<UnclassifiedUsage>();
        s.AddSingleton<UsageService>();
        s.AddSingleton<StatsExchange>();
        s.AddSingleton<PersonalRules>();
        s.AddSingleton<BuiltinRuleCache>();
        s.AddSingleton<ForegroundState>();
        s.AddSingleton<HubService>();
        s.AddSingleton<RoomState>();
        s.AddSingleton<PresenceCoordinator>();
        s.AddHostedService(sp => sp.GetRequiredService<PresenceCoordinator>());
        s.AddHostedService<ForegroundCollector>();
        s.AddHostedService<BrowserDomainCollector>();
        s.AddHostedService<IdleCollector>();
        s.AddHostedService<SessionCollector>();
        s.AddHostedService<MediaCollector>();
        s.AddHostedService<UsageReporter>();
        s.AddSingleton<UpdateService>();
        s.AddSingleton(sp => new WhatsNewStore(
            ReadChangelog(),
            Path.Combine(SettingsStore.Directory, "changelog-seen.json"),
            sp.GetRequiredService<UpdateService>().VersionLabel));
        s.AddSingleton<PinnedMembers>();
        s.AddSingleton<PanelBridge>();
        s.AddSingleton<PanelWindow>();
        s.AddSingleton<MemberCards>();
        return builder.Build();
    }

    private static string? ReadChangelog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Changelog.FileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void LoadCachedBuiltinRules(IServiceProvider services)
    {
        var cached = services.GetRequiredService<BuiltinRuleCache>().Load();
        services.GetRequiredService<LayeredClassifier>().SetBuiltin(new RuleSet(cached.Rules));
    }

    private static async Task RefreshBuiltinRulesAsync(IServiceProvider services)
    {
        var cache = services.GetRequiredService<BuiltinRuleCache>();
        var cached = cache.Load();
        try
        {
            var response = await services.GetRequiredService<ApiClient>().GetBuiltinRulesAsync(cached.Version);
            if (response.Rules is not null)
            {
                cache.Save(new DeskPal.Shared.Api.RuleSetDto(response.Version, response.Rules));
                services.GetRequiredService<LayeredClassifier>().SetBuiltin(new RuleSet(response.Rules));
                services.GetRequiredService<PresenceCoordinator>().Reevaluate();
            }
        }
        catch (ApiException ex)
        {
            services.GetRequiredService<ILogger<App>>().LogWarning(ex, "Refreshing builtin rules failed");
        }
    }

    private async Task RefreshRoomsAsync(RoomState rooms)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await rooms.RefreshAsync();
                return;
            }
            catch (ApiException) when (attempt < 20)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * (attempt + 1))));
            }
            catch (ApiException ex)
            {
                _tray?.ShowNotification("DeskPal", $"加载房间失败：{ex.Message}");
                return;
            }
        }
    }

    private TaskbarIcon CreateTray(UpdateService updates)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开 DeskPal" };
        open.Click += (_, _) => _panel?.ShowAndActivate();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        var tray = new TaskbarIcon
        {
            ToolTipText = $"DeskPal {updates.VersionLabel}",
            NoLeftClickDelay = true,
            ContextMenu = menu,
            IconSource = new GeneratedIconSource
            {
                Text = "D",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x4F, 0x7D, 0xF3)),
                FontSize = 40,
            },
        };
        tray.TrayLeftMouseUp += (_, _) =>
        {
            if (_panel is { IsVisible: true })
            {
                _panel.Hide();
            }
            else
            {
                _panel?.ShowAndActivate();
            }
        };
        tray.ForceCreate(false);
        return tray;
    }

    private Task LogoutAsync() => RestartSignedOutAsync(null);

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
