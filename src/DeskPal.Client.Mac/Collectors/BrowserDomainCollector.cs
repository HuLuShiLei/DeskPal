using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using DeskPal.Client.Providers;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Reads the address of the frontmost browser window through Accessibility and publishes only the host.
/// Safari and Chromium browsers expose the page's URL on their web area (AXURL); otherwise the first text field in
/// the toolbar is taken as the address bar, like the Windows collector does. Same timing as on Windows.
/// </summary>
public sealed class BrowserDomainCollector(SignalSink bus, ForegroundState foreground, ILogger<BrowserDomainCollector> logger) : BackgroundService
{
    private const int MaxDepth = 14;
    private const int MaxNodes = 600;
    private Task<string?>? _inflight;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        ForegroundState.Snapshot? last = null;
        string? lastDomain = null;
        var lastRead = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = foreground.Current;
            if (!current.IsBrowser)
            {
                last = null;
                lastDomain = null;
                continue;
            }

            var changed = current != last;
            if ((!changed && DateTimeOffset.UtcNow - lastRead < TimeSpan.FromSeconds(2)) || _inflight is { IsCompleted: false })
            {
                continue;
            }

            lastRead = DateTimeOffset.UtcNow;
            var readTask = _inflight = Task.Run(() => ReadDomain(current.Pid), stoppingToken);
            var finished = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(3), stoppingToken));
            var domain = finished == readTask ? await readTask : null;
            if (changed || domain != lastDomain)
            {
                last = current;
                lastDomain = domain;
                bus.Publish(new BrowserDomainSignal(domain));
            }
        }
    }

    private string? ReadDomain(int pid)
    {
        var app = MacNative.AXUIElementCreateApplication(pid);
        if (app == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            MacNative.SetTimeout(app, 1f);
            var window = MacNative.Copy(app, "AXFocusedWindow");
            if (window == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Search(window);
            }
            finally
            {
                MacNative.CFRelease(window);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Reading the browser address failed");
            return null;
        }
        finally
        {
            MacNative.CFRelease(app);
        }
    }

    /// <summary>Breadth first, so the toolbar is reached before the page content.</summary>
    private static string? Search(IntPtr window)
    {
        var queue = new Queue<(IntPtr Element, int Depth, bool InToolbar)>();
        var owned = new List<IntPtr>();
        string? addressField = null;
        try
        {
            foreach (var child in MacNative.CopyChildren(window))
            {
                owned.Add(child);
                queue.Enqueue((child, 1, false));
            }

            var visited = 0;
            while (queue.Count > 0 && visited++ < MaxNodes)
            {
                var (element, depth, inToolbar) = queue.Dequeue();
                var role = MacNative.CopyString(element, "AXRole");
                if (role == "AXWebArea" && ToHost(MacNative.CopyUrl(element, "AXURL")) is { } host)
                {
                    return host;
                }

                if (addressField is null && inToolbar && role is "AXTextField" or "AXComboBox")
                {
                    addressField = ToHost(MacNative.CopyString(element, "AXValue"));
                }

                if (depth >= MaxDepth || role == "AXWebArea")
                {
                    continue;
                }

                var toolbar = inToolbar || role == "AXToolbar";
                foreach (var child in MacNative.CopyChildren(element))
                {
                    owned.Add(child);
                    queue.Enqueue((child, depth + 1, toolbar));
                }
            }

            return addressField;
        }
        finally
        {
            foreach (var element in owned)
            {
                MacNative.CFRelease(element);
            }
        }
    }

    private static string? ToHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' ')
            || value.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("favorites:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var host = RulePattern.NormalizeHost(value);
        return host == "localhost" || (host.Contains('.') && !host.StartsWith('.')) ? host : null;
    }
}
