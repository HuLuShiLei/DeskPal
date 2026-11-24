using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Native;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace DeskPal.Client.Collectors;

/// <summary>
/// What is playing, from MPRIS: every player (browsers, VLC, mpv, Spotify) owns a bus name starting with
/// org.mpris.MediaPlayer2 and exposes its playback status, title and artist. The source app is the executable of
/// the process that owns the name, the same word the foreground window uses, so "the foreground app is playing" works.
/// </summary>
public sealed class MediaCollector(SignalSink bus, ILogger<MediaCollector> logger) : BackgroundService
{
    private const string Prefix = "org.mpris.MediaPlayer2.";
    private const string PlayerPath = "/org/mpris/MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private static readonly MediaSignal Stopped = new(false, null, null, null);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        MediaSignal? last = null;
        do
        {
            MediaSignal signal;
            try
            {
                signal = await ReadAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Reading MPRIS failed");
                signal = Stopped;
            }

            if (signal != last)
            {
                last = signal;
                bus.Publish(signal);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task<MediaSignal> ReadAsync()
    {
        var connection = await Bus.SessionAsync();
        var names = await connection.ListServicesAsync();
        foreach (var name in names.Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            try
            {
                var status = await Bus.GetPropertyAsync(connection, name, PlayerPath, PlayerInterface, "PlaybackStatus");
                if (status.GetString() != "Playing")
                {
                    continue;
                }

                string? title = null, artist = null;
                var metadata = await Bus.GetPropertyAsync(connection, name, PlayerPath, PlayerInterface, "Metadata");
                for (var i = 0; i < metadata.Count; i++)
                {
                    var entry = metadata.GetDictionaryEntry(i);
                    switch (entry.Key.GetString())
                    {
                        case "xesam:title" when entry.Value.Type == VariantValueType.String:
                            title = entry.Value.GetString();
                            break;
                        case "xesam:artist" when entry.Value.Type == VariantValueType.Array && entry.Value.Count > 0:
                            artist = entry.Value.GetItem(0).GetString();
                            break;
                    }
                }

                var pid = await Bus.ProcessIdOfAsync(connection, name);
                var source = LinuxProcess.NameOf((int)pid) ?? name[Prefix.Length..].Split('.')[0];
                return new MediaSignal(true, Blank(title), Blank(artist), source);
            }
            catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or InvalidOperationException)
            {
                // A player that left between listing and asking, or one that does not answer.
            }
        }

        return Stopped;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
