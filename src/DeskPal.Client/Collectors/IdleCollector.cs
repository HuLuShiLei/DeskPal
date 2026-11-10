using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using DeskPal.Client.Services;
using Microsoft.Extensions.Hosting;

namespace DeskPal.Client.Collectors;

/// <summary>Only reads the timestamp of the last input. Keys and mouse positions are never recorded.</summary>
public sealed class IdleCollector(ActivityBus bus, AppSettings settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        bool? last = null;
        do
        {
            var idle = NativeMethods.GetIdleTime() >= TimeSpan.FromMinutes(settings.IdleMinutes);
            if (idle != last)
            {
                last = idle;
                bus.Publish(new IdleSignal(idle));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}