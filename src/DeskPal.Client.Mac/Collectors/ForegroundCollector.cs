using AppKit;
using CoreGraphics;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using DeskPal.Client.Platform;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Polls the frontmost app once per second. The app comes from NSWorkspace and needs no permission. The window
/// title and whether the window fills its screen come from Accessibility; without that permission they are empty.
/// The process name published is the bundle identifier (com.tencent.xinWeChat), so rules work across languages
/// and versions; apps without one use their executable name.
/// </summary>
public sealed class ForegroundCollector(ActivityBus bus, ForegroundState state, ILogger<ForegroundCollector> logger) : BackgroundService
{
    private readonly int _ownProcessId = Environment.ProcessId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        ForegroundSignal? last = null;
        do
        {
            try
            {
                var signal = Read();
                if (signal is not null && signal != last)
                {
                    last = signal;
                    bus.Publish(signal);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Reading the frontmost app failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private ForegroundSignal? Read()
    {
        var front = MainThread.Invoke(() =>
        {
            var app = NSWorkspace.SharedWorkspace.FrontmostApplication;
            if (app is null)
            {
                return null;
            }

            var screens = NSScreen.Screens.Select(s => s.Frame).ToArray();
            return new Front(app.ProcessIdentifier, app.BundleIdentifier, app.LocalizedName, app.ExecutableUrl?.Path, screens);
        });
        if (front is null || front.Pid == _ownProcessId)
        {
            return null;
        }

        var executable = front.ExecutablePath is { } path ? Path.GetFileName(path) : null;
        var processName = front.BundleId is { Length: > 0 } bundle ? bundle : executable ?? "unknown";
        var displayName = front.Name is { Length: > 0 } name ? name : executable ?? processName;
        var (title, fullscreen) = ReadWindow(front);
        var isBrowser = Browsers.IsBrowser(processName);
        state.Set(front.Pid, isBrowser, processName, title);
        return new ForegroundSignal(processName, front.ExecutablePath, displayName, title, fullscreen, isBrowser);
    }

    private static (string Title, bool Fullscreen) ReadWindow(Front front)
    {
        var app = MacNative.AXUIElementCreateApplication(front.Pid);
        if (app == IntPtr.Zero)
        {
            return ("", false);
        }

        try
        {
            MacNative.SetTimeout(app, 0.5f);
            var window = MacNative.Copy(app, "AXFocusedWindow");
            if (window == IntPtr.Zero)
            {
                return ("", false);
            }

            try
            {
                var title = MacNative.CopyString(window, "AXTitle")?.Trim() ?? "";
                var fullscreen = MacNative.CopyBool(window, "AXFullScreen") == true || FillsScreen(MacNative.CopyFrame(window), front.Screens);
                return (title, fullscreen);
            }
            finally
            {
                MacNative.CFRelease(window);
            }
        }
        finally
        {
            MacNative.CFRelease(app);
        }
    }

    /// <summary>
    /// Borderless full-screen games do not use the system full-screen mode. AX frames have their origin at the top
    /// left of the main screen; NSScreen frames at the bottom left.
    /// </summary>
    private static bool FillsScreen(CGRect? frame, CGRect[] screens)
    {
        if (frame is not { } window || screens.Length == 0)
        {
            return false;
        }

        var mainHeight = screens[0].Height;
        foreach (var screen in screens)
        {
            var top = mainHeight - screen.Y - screen.Height;
            if (Math.Abs(window.X - screen.X) < 1 && Math.Abs(window.Y - top) < 1
                && Math.Abs(window.Width - screen.Width) < 1 && Math.Abs(window.Height - screen.Height) < 1)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Front(int Pid, string? BundleId, string? Name, string? ExecutablePath, CGRect[] Screens);
}

/// <summary>The current frontmost app, shared with collectors that inspect it further.</summary>
public sealed class ForegroundState
{
    private volatile Snapshot _current = new(0, false, "", "");

    public Snapshot Current => _current;

    public void Set(int pid, bool isBrowser, string processName, string title) => _current = new Snapshot(pid, isBrowser, processName, title);

    public sealed record Snapshot(int Pid, bool IsBrowser, string ProcessName, string Title);
}
