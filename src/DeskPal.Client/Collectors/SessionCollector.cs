using DeskPal.Client.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace DeskPal.Client.Collectors;

public sealed class SessionCollector(ActivityBus bus) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        return Task.CompletedTask;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                bus.Publish(new LockSignal(true));
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                bus.Publish(new LockSignal(false));
                break;
        }
    }
}