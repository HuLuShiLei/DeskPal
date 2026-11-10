using System.Collections.Concurrent;
using System.Diagnostics;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Collectors;

/// <summary>Polls the foreground window once per second. Polling avoids global hooks, which security software tends to flag.</summary>
public sealed class ForegroundCollector(ActivityBus bus, ForegroundState state, ILogger<ForegroundCollector> logger) : BackgroundService
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "360se", "360chrome", "qqbrowser", "sogouexplorer", "2345explorer",
    };

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd" };

    private readonly ConcurrentDictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);
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
                logger.LogDebug(ex, "Reading foreground window failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private ForegroundSignal? Read()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownProcessId)
        {
            return null;
        }

        var windowClass = NativeMethods.GetWindowClass(hwnd);
        var path = NativeMethods.GetProcessPath(pid);
        var processName = path is null ? SafeProcessName(pid) : Path.GetFileNameWithoutExtension(path);
        var title = NativeMethods.GetWindowTitle(hwnd);
        var isBrowser = Browsers.Contains(processName);
        var fullscreen = !ShellClasses.Contains(windowClass) && NativeMethods.IsFullscreen(hwnd);

        state.Set(hwnd, isBrowser);

        var displayName = processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && title.Length > 0
            ? title
            : GetDisplayName(processName, path);
        return new ForegroundSignal(processName, path, displayName, title, fullscreen, isBrowser);
    }

    private string GetDisplayName(string processName, string? path)
    {
        if (path is null)
        {
            return processName;
        }

        return _displayNames.GetOrAdd(path, p =>
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(p).FileDescription?.Trim();
                return string.IsNullOrEmpty(description) ? processName : description;
            }
            catch
            {
                return processName;
            }
        });
    }

    private static string SafeProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }
}

/// <summary>The current foreground window, shared with collectors that inspect it further.</summary>
public sealed class ForegroundState
{
    private volatile Snapshot _current = new(IntPtr.Zero, false);

    public Snapshot Current => _current;

    public void Set(IntPtr hwnd, bool isBrowser) => _current = new Snapshot(hwnd, isBrowser);

    public sealed record Snapshot(IntPtr Hwnd, bool IsBrowser);
}