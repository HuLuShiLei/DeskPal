using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Screen lock and unlock. GNOME and KDE announce it on the session bus with ActiveChanged of their screen saver
/// (org.gnome.ScreenSaver, org.freedesktop.ScreenSaver). Lockers that only talk to systemd-logind are covered by
/// reading the session's LockedHint every few seconds.
/// </summary>
public sealed class SessionCollector(SignalSink bus, ILogger<SessionCollector> logger) : BackgroundService
{
    private static readonly (string Name, string Path)[] ScreenSavers =
    [
        ("org.gnome.ScreenSaver", "/org/gnome/ScreenSaver"),
        ("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver"),
    ];

    private bool? _last;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = new List<IDisposable>();
        var signals = false;
        try
        {
            var connection = await Bus.SessionAsync();
            foreach (var (name, path) in ScreenSavers)
            {
                subscriptions.Add(await connection.AddMatchAsync(
                    new MatchRule { Type = MessageType.Signal, Interface = name, Member = "ActiveChanged" },
                    static (Message m, object? _) => m.GetBodyReader().ReadBool(),
                    (Notification<bool> notification) =>
                    {
                        if (notification.HasValue)
                        {
                            Publish(notification.Value);
                        }
                    },
                    emitOnCapturedContext: false,
                    ObserverFlags.None));
                try
                {
                    Publish(await Bus.CallAsync(connection, name, path, name, "GetActive", static (Message m, object? _) => m.GetBodyReader().ReadBool()));
                    signals = true;
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Watching the screen saver on the session bus failed");
        }

        logger.LogInformation("Screen lock comes from {Source}", signals ? "the screen saver signals" : "logind");
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!signals && await LogindLockedAsync() is { } locked)
                {
                    Publish(locked);
                }
            }
        }
        finally
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private void Publish(bool locked)
    {
        if (_last != locked)
        {
            _last = locked;
            bus.Publish(new LockSignal(locked));
        }
    }

    private static async Task<bool?> LogindLockedAsync()
    {
        try
        {
            var value = await Bus.GetPropertyAsync(DBusConnection.System, "org.freedesktop.login1", "/org/freedesktop/login1/session/auto", "org.freedesktop.login1.Session", "LockedHint");
            return value.GetBool();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
