using DeskPal.Client.Core.Providers;
using DeskPal.Client.Native;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// Part of the Linux official plugin: reports the foreground window. Xlib on X11 sessions, the DeskPal extension on
/// GNOME with Wayland. Other Wayland desktops have no common way to ask; there the official plugin does not claim the
/// foreground capability and a user plugin has to supply it.
/// </summary>
public sealed class LinuxWindowSource(SignalSink sink, LinuxStatus status, ILogger<LinuxWindowSource> logger) : BackgroundService
{
    private readonly int _ownProcessId = Environment.ProcessId;
    private ProviderForeground? _last;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Window source for session {Kind}, desktop {Desktop}", status.Session.Kind, status.Session.Desktop);
        return status.Session.Kind switch
        {
            "x11" => RunX11Async(stoppingToken),
            "gnome" => RunGnomeAsync(stoppingToken),
            _ => Unavailable(stoppingToken),
        };
    }

    private Task Unavailable(CancellationToken cancellation)
    {
        status.SetWindow("unavailable");
        return Task.Delay(Timeout.Infinite, cancellation);
    }

    private async Task RunX11Async(CancellationToken cancellation)
    {
        using var x11 = X11.Open();
        if (x11 is null)
        {
            await Unavailable(cancellation);
            return;
        }

        status.SetWindow("ok");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                // A window with neither a process id nor a class is the window manager's own (a desktop or a
                // panel); there is no app to name, so it is not reported.
                if (x11.ReadActiveWindow() is { } window && window.Pid != _ownProcessId && (window.Pid > 0 || window.Class.Length > 0))
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
        GnomeExtension.RefreshInstalled();
        var answered = false;
        while (!cancellation.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(1);
            try
            {
                var focused = await GnomeExtension.GetFocusedAsync();
                answered = true;
                status.SetWindow("ok");
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
                // Once it has answered, a gap is the lock screen, not a setup problem.
                if (!answered)
                {
                    status.SetWindow(GnomeExtension.IsInstalled ? "needsRelogin" : "needsExtension");
                }

                logger.LogDebug(ex, "The GNOME extension did not answer");
                delay = TimeSpan.FromSeconds(answered ? 2 : 5);
            }

            await Task.Delay(delay, cancellation);
        }
    }

    private void Publish(ProviderForeground foreground)
    {
        if (foreground == _last)
        {
            return;
        }

        _last = foreground;
        sink.Publish(ProviderProtocol.ToSignal(foreground));
    }
}
