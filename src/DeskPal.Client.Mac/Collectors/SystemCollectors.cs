using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using DeskPal.Client.Services;
using Foundation;
using Microsoft.Extensions.Hosting;

namespace DeskPal.Client.Collectors;

/// <summary>Only reads the time since the last input. Keys and mouse positions are never recorded. No permission needed.</summary>
public sealed class IdleCollector(ActivityBus bus, AppSettings settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        bool? last = null;
        do
        {
            var idle = MacNative.IdleTime() >= TimeSpan.FromMinutes(settings.IdleMinutes);
            if (idle != last)
            {
                last = idle;
                bus.Publish(new IdleSignal(idle));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Screen lock and unlock, from the notifications the login window posts. No permission needed.</summary>
public sealed class SessionCollector(ActivityBus bus) : IHostedService
{
    private NSObject? _locked;
    private NSObject? _unlocked;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var center = NSDistributedNotificationCenter.DefaultCenter;
        _locked = center.AddObserver(new NSString("com.apple.screenIsLocked"), _ => bus.Publish(new LockSignal(true)));
        _unlocked = center.AddObserver(new NSString("com.apple.screenIsUnlocked"), _ => bus.Publish(new LockSignal(false)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var center = NSDistributedNotificationCenter.DefaultCenter;
        if (_locked is not null)
        {
            center.RemoveObserver(_locked);
        }

        if (_unlocked is not null)
        {
            center.RemoveObserver(_unlocked);
        }

        return Task.CompletedTask;
    }
}
