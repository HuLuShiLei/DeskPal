using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskPal.Client.Collectors;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Services;
using DeskPal.Client.ViewModels;
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
    private MainWindow? _main;
    private MenuItem? _betaMenu;
    private MenuItem? _restartMenu;
    private CancellationTokenSource? _updates;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(false, "DeskPal.Client.SingleInstance");
        if (!_singleInstance.WaitOne(TimeSpan.FromSeconds(3)))
        {
            MessageBox.Show("DeskPal 已经在运行，请从任务栏右下角的托盘图标打开。", "DeskPal");
            _singleInstance = null;
            Shutdown();
            return;
        }

        var store = new SettingsStore();
        var settings = store.Load();
        var api = new ApiClient(settings);
        if (!await EnsureLoggedInAsync(settings, store, api))
        {
            Shutdown();
            return;
        }

        _host = BuildHost(settings, store, api);
        var services = _host.Services;
        var rooms = services.GetRequiredService<RoomState>();
        var hub = services.GetRequiredService<HubService>();
        var updates = services.GetRequiredService<UpdateService>();
        updates.Exit = restart => ExitAsync(applyUpdate: true, restart: restart);
        updates.Checked += result => Dispatcher.BeginInvoke(() => _tray?.ShowNotification("DeskPal", result.Message));
        updates.Changed += () => Dispatcher.BeginInvoke(SyncUpdateMenu);

        _tray = CreateTray(updates);
        rooms.Notify += (title, message) => Dispatcher.BeginInvoke(() => _tray.ShowNotification(title, message));
        hub.Received += rooms.Handle;

        LoadCachedBuiltinRules(services);
        await _host.StartAsync();
        hub.Start();
        _ = RefreshBuiltinRulesAsync(services);
        _ = RefreshRoomsAsync(rooms);
        _updates = new CancellationTokenSource();
        _ = updates.RunPeriodicAsync(_updates.Token);

        _main = services.GetRequiredService<MainWindow>();
        _main.WindowStartupLocation = WindowStartupLocation.Manual;
        if (!e.Args.Contains("--minimized"))
        {
            _main.ShowAndActivate();
        }
    }

    private static async Task<bool> EnsureLoggedInAsync(AppSettings settings, SettingsStore store, ApiClient api)
    {
        if (settings.GetToken() is not null)
        {
            try
            {
                var me = await api.MeAsync();
                settings.DisplayName = me.DisplayName;
                store.Save(settings);
                return true;
            }
            catch (ApiException ex) when (ex.Status == 0)
            {
                return true;
            }
            catch (ApiException)
            {
                settings.SetToken(null);
                store.Save(settings);
            }
        }

        return new LoginWindow(settings, store, api).ShowDialog() == true;
    }

    private static IHost BuildHost(AppSettings settings, SettingsStore store, ApiClient api)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        var s = builder.Services;
        s.AddSingleton(settings);
        s.AddSingleton(store);
        s.AddSingleton(api);
        s.AddSingleton<ActivityBus>();
        s.AddSingleton<LayeredClassifier>();
        s.AddSingleton(new PersonalRuleStore(Path.Combine(SettingsStore.Directory, "personal-rules.json")));
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
        s.AddHostedService<MicCollector>();
        s.AddHostedService<MediaCollector>();
        s.AddSingleton<UpdateService>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<MainWindow>();
        return builder.Build();
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
                _tray?.ShowNotification("DeskPal", $"加载 Room 失败：{ex.Message}");
                return;
            }
        }
    }

    private TaskbarIcon CreateTray(UpdateService updates)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开 DeskPal" };
        open.Click += (_, _) => _main?.ShowAndActivate();
        var check = new MenuItem { Header = "检查更新" };
        check.Click += async (_, _) => await UpdateInteraction.CheckAsync(updates, updates.Exit!, quietWhenCurrent: false);
        _betaMenu = new MenuItem { Header = "加入测试版", IsCheckable = true, IsChecked = updates.ReceiveBeta };
        _betaMenu.Click += async (_, _) =>
        {
            if (updates.ReceiveBeta == (_betaMenu.IsChecked == true))
            {
                return;
            }

            updates.ReceiveBeta = _betaMenu.IsChecked == true;
            await UpdateInteraction.CheckAsync(updates, updates.Exit!, quietWhenCurrent: true);
        };
        _restartMenu = new MenuItem { Header = "立即重启并更新", IsEnabled = updates.UpdateReady };
        _restartMenu.Click += async (_, _) =>
        {
            if (updates.Exit is not null)
            {
                await updates.Exit(true);
            }
        };
        var logout = new MenuItem { Header = "退出登录" };
        logout.Click += async (_, _) => await LogoutAsync();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(check);
        menu.Items.Add(_betaMenu);
        menu.Items.Add(_restartMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(logout);
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
            if (_main is { IsVisible: true })
            {
                _main.Hide();
            }
            else
            {
                _main?.ShowAndActivate();
            }
        };
        tray.ForceCreate(false);
        return tray;
    }

    private void SyncUpdateMenu()
    {
        var updates = _host?.Services.GetService<UpdateService>();
        if (updates is null || _betaMenu is null || _restartMenu is null)
        {
            return;
        }

        _restartMenu.IsEnabled = updates.UpdateReady;
        if (_betaMenu.IsChecked != updates.ReceiveBeta)
        {
            _betaMenu.IsChecked = updates.ReceiveBeta;
        }
    }

    private async Task LogoutAsync()
    {
        var services = _host!.Services;
        var settings = services.GetRequiredService<AppSettings>();
        settings.SetToken(null);
        services.GetRequiredService<SettingsStore>().Save(settings);
        await ExitAsync(applyUpdate: false);
        Process.Start(Environment.ProcessPath!);
    }

    private async Task ExitAsync(bool applyUpdate = true, bool restart = false)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        var updates = _host?.Services.GetService<UpdateService>();

        if (_main is not null)
        {
            _main.AllowClose = true;
            _main.Close();
        }

        if (_host is not null)
        {
            await _host.Services.GetRequiredService<HubService>().DisposeAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
            _host = null;
        }

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