using System.Windows.Automation;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Providers;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Reads the address bar of the foreground browser through UI Automation and publishes only the host.
/// When it fails, classification falls back to the window title.
/// </summary>
public sealed class BrowserDomainCollector(SignalSink bus, ForegroundState foreground, ILogger<BrowserDomainCollector> logger) : BackgroundService
{
    private IntPtr _cachedHwnd;
    private AutomationElement? _cachedAddressBar;
    private Task<string?>? _inflight;

    /// <summary>
    /// Reads every 2 seconds, and right away when the tab title changes, so a new site is not recorded under the previous one.
    /// After a title change it publishes even an unchanged domain: that tells the snapshot the address is read.
    /// </summary>
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
            var readTask = _inflight = Task.Run(() => ReadDomain(current.Hwnd), stoppingToken);
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

    private string? ReadDomain(IntPtr hwnd)
    {
        try
        {
            if (hwnd != _cachedHwnd || _cachedAddressBar is null)
            {
                _cachedHwnd = hwnd;
                _cachedAddressBar = AutomationElement.FromHandle(hwnd)
                    .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            }

            if (_cachedAddressBar?.GetCurrentPattern(ValuePattern.Pattern) is not ValuePattern pattern)
            {
                return null;
            }

            return ToHost(pattern.Current.Value);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ArgumentException)
        {
            _cachedAddressBar = null;
            logger.LogDebug(ex, "Reading browser address bar failed");
            return null;
        }
    }

    private static string? ToHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' '))
        {
            return null;
        }

        var host = RulePattern.NormalizeHost(value);
        return host == "localhost" || (host.Contains('.') && !host.StartsWith('.')) ? host : null;
    }
}