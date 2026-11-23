using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Native;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// Decides where the foreground window comes from on Linux and keeps that source running.
/// A plugin chosen in Settings wins. Otherwise the built-in detection is used: Xlib on X11 sessions, the DeskPal
/// extension on GNOME with Wayland. Other Wayland desktops have no common way to ask, so they need a plugin.
/// Every source produces the same <see cref="ProviderForeground"/> a plugin line does.
/// </summary>
public sealed class ForegroundService(ActivityBus bus, AppSettings settings, SettingsStore store, ILogger<ForegroundService> logger)
    : BackgroundService, IProviderControl
{
    private readonly Lock _lock = new();
    private readonly int _ownProcessId = Environment.ProcessId;
    private CancellationTokenSource? _current;
    private string _source = "none";
    private string _status = "starting";
    private string? _detail;
    private ForegroundSignal? _last;

    public event Action? Changed;

    public string Folder
    {
        get
        {
            var folder = Path.Combine(SettingsStore.Directory, "providers");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public ProviderView View => new(settings.Provider, Plugins(), _source, _status, _detail);

    public CommandResult Select(string? plugin)
    {
        if (plugin is not null && !Plugins().Contains(plugin))
        {
            return CommandResult.Fail(UiLanguage.Format(Strings.Provider_NotFound, plugin));
        }

        settings.Provider = plugin;
        store.Save(settings);
        logger.LogInformation("Provider set to {Provider}", plugin ?? "(built-in)");
        Restart();
        return CommandResult.Success;
    }

    public async Task<CommandResult> InstallHelperAsync()
    {
        try
        {
            await GnomeExtension.InstallAsync();
            logger.LogInformation("GNOME extension installed");
            Restart();
            return new CommandResult(true, Data: new { message = Strings.Provider_ExtensionInstalled });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Installing the GNOME extension failed");
            return CommandResult.Fail(ex.Message);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GnomeExtension.RefreshInstalled();
        while (!stoppingToken.IsCancellationRequested)
        {
            using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_lock)
            {
                _current = run;
            }

            try
            {
                await RunSelectedAsync(run.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The foreground source stopped");
                Set(_source, "failed", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }

            lock (_lock)
            {
                _current = null;
            }
        }
    }

    private void Restart()
    {
        lock (_lock)
        {
            _current?.Cancel();
        }
    }

    private Task RunSelectedAsync(CancellationToken cancellation)
    {
        _last = null;

        // Whatever the previous source said about the browser no longer holds. The built-in sources do not read the
        // browser's address; a plugin may.
        bus.Publish(new BrowserDomainSignal(null));
        if (settings.Provider is { Length: > 0 } plugin)
        {
            return RunPluginAsync(plugin, cancellation);
        }

        var wayland = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        logger.LogInformation("Built-in foreground detection: session {Session}, desktop {Desktop}", wayland ? "wayland" : "x11", desktop);
        if (!wayland)
        {
            return RunX11Async(cancellation);
        }

        if (desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
        {
            return RunGnomeAsync(cancellation);
        }

        Set("none", "needsPlugin", desktop.Length == 0 ? null : desktop);
        return Task.Delay(Timeout.Infinite, cancellation);
    }

    private async Task RunPluginAsync(string plugin, CancellationToken cancellation)
    {
        var path = Path.Combine(Folder, plugin);
        if (!Plugins().Contains(plugin))
        {
            Set("plugin", "failed", UiLanguage.Format(Strings.Provider_NotFound, plugin));
            await Task.Delay(Timeout.Infinite, cancellation);
        }

        var provider = new ExternalProvider(path, bus, logger);
        provider.Changed += () => Set(
            "plugin",
            provider.Delivering ? "ok" : provider.Error is null ? "starting" : "failed",
            provider.Delivering ? provider.Hello : provider.Error);
        Set("plugin", "starting", null);
        await provider.RunAsync(cancellation);
    }

    private async Task RunX11Async(CancellationToken cancellation)
    {
        using var x11 = X11.Open();
        if (x11 is null)
        {
            Set("none", "needsPlugin", "X11");
            await Task.Delay(Timeout.Infinite, cancellation);
            return;
        }

        Set("x11", "ok", null);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                if (x11.ReadActiveWindow() is { } window && window.Pid != _ownProcessId)
                {
                    var (process, path) = LinuxProcess.Describe(window.Pid, window.Class);
                    var name = DesktopEntries.NameFor(window.Class, process) ?? (window.Class.Length > 0 ? window.Class : process);
                    Publish(new ProviderForeground(process, name, window.Title, window.Fullscreen, path));
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Reading the active X11 window failed");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellation));
    }

    private async Task RunGnomeAsync(CancellationToken cancellation)
    {
        Set("gnome", "starting", null);
        while (!cancellation.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(1);
            try
            {
                var focused = await GnomeExtension.GetFocusedAsync();
                Set("gnome", "ok", null);
                if (focused is not null && focused.Pid != _ownProcessId)
                {
                    var appId = focused.AppId.Length > 0 ? focused.AppId : focused.WmClass;
                    var (process, path) = LinuxProcess.Describe(focused.Pid, focused.WmClass.Length > 0 ? focused.WmClass : appId);
                    var name = focused.AppName.Length > 0 ? focused.AppName : DesktopEntries.NameFor(appId, focused.WmClass, process) ?? process;
                    Publish(new ProviderForeground(process, name, focused.Title.Trim(), focused.Fullscreen, path));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The extension is missing, turned off, or the screen is locked (the shell unloads extensions then).
                if (_status != "ok")
                {
                    Set("gnome", GnomeExtension.IsInstalled ? "needsRelogin" : "needsExtension", null);
                }

                logger.LogDebug(ex, "The GNOME extension did not answer");
                delay = TimeSpan.FromSeconds(_status == "ok" ? 2 : 5);
            }

            await Task.Delay(delay, cancellation);
        }
    }

    private void Publish(ProviderForeground foreground)
    {
        var signal = ProviderProtocol.ToSignal(foreground);
        if (signal != _last)
        {
            _last = signal;
            bus.Publish(signal);
        }
    }

    private void Set(string source, string status, string? detail)
    {
        if (_source == source && _status == status && _detail == detail)
        {
            return;
        }

        logger.LogInformation("Foreground source {Source}: {Status} {Detail}", source, status, detail);
        _source = source;
        _status = status;
        _detail = detail;
        Changed?.Invoke();
    }

    /// <summary>Executable files directly in the providers folder, hidden files left out.</summary>
    private IReadOnlyList<string> Plugins()
    {
        try
        {
            return Directory.EnumerateFiles(Folder)
                .Where(file => !Path.GetFileName(file).StartsWith('.') && (File.GetUnixFileMode(file) & UnixFileMode.UserExecute) != 0)
                .Select(file => Path.GetFileName(file))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
