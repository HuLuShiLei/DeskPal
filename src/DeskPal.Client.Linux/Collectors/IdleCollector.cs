using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using DeskPal.Client.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Only reads how long there has been no input; keys and pointer positions are never read, and /dev/input is not
/// touched. GNOME answers through org.gnome.Mutter.IdleMonitor, X11 through the screen saver extension. Elsewhere
/// the session's IdleHint from systemd-logind is used, which the desktop sets after its own idle time.
/// </summary>
public sealed class IdleCollector(ActivityBus bus, AppSettings settings, ILogger<IdleCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var x11 = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ? null : X11.Open();
        var source = "";
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        bool? last = null;
        do
        {
            bool idle;
            string used;
            var limit = TimeSpan.FromMinutes(settings.IdleMinutes);
            if (await MutterIdleAsync() is { } mutter)
            {
                (idle, used) = (mutter >= limit, "mutter");
            }
            else if (x11?.IdleTime() is { } x)
            {
                (idle, used) = (x >= limit, "x11");
            }
            else
            {
                (idle, used) = (await LogindIdleHintAsync() == true, "logind");
            }

            if (used != source)
            {
                source = used;
                logger.LogInformation("Idle time comes from {Source}", source);
            }

            if (idle != last)
            {
                last = idle;
                bus.Publish(new IdleSignal(idle));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task<TimeSpan?> MutterIdleAsync()
    {
        try
        {
            var connection = await Bus.SessionAsync();
            var milliseconds = await Bus.CallAsync(
                connection,
                "org.gnome.Mutter.IdleMonitor",
                "/org/gnome/Mutter/IdleMonitor/Core",
                "org.gnome.Mutter.IdleMonitor",
                "GetIdletime",
                static (Message m, object? _) => m.GetBodyReader().ReadUInt64());
            return TimeSpan.FromMilliseconds(milliseconds);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<bool?> LogindIdleHintAsync()
    {
        try
        {
            var system = DBusConnection.System;
            var value = await Bus.GetPropertyAsync(system, "org.freedesktop.login1", "/org/freedesktop/login1/session/auto", "org.freedesktop.login1.Session", "IdleHint");
            return value.GetBool();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
