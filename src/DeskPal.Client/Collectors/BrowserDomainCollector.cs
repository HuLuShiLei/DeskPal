using System.Windows.Automation;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Reads the address bar of the foreground browser through UI Automation and publishes only the host.
/// When it fails, classification falls back to the window title.
/// </summary>
public sealed class BrowserDomainCollector(ActivityBus bus, ForegroundState foreground, ILogger<BrowserDomainCollector> logger) : BackgroundService
{
    private IntPtr _cachedHwnd;
    private AutomationElement? _cachedAddressBar;
    private Task<string?>? _inflight;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        IntPtr lastHwnd = IntPtr.Zero;
        string? lastDomain = null;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = foreground.Current;
            if (!current.IsBrowser)
            {
                lastHwnd = IntPtr.Zero;
                lastDomain = null;
                continue;
            }

            if (_inflight is { IsCompleted: false })
            {
                continue;
            }

            var readTask = _inflight = Task.Run(() => ReadDomain(current.Hwnd), stoppingToken);
            var finished = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(3), stoppingToken));
            var domain = finished == readTask ? await readTask : null;
            if (current.Hwnd != lastHwnd || domain != lastDomain)
            {
                lastHwnd = current.Hwnd;
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