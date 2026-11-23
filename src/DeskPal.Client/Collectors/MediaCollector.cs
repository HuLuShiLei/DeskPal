using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Reads what the system media controls show (song or video title), the same info as the volume flyout.
/// When several apps have a session, the one of the foreground program wins over the one Windows shows.
/// </summary>
public sealed class MediaCollector(SignalSink bus, ForegroundState foreground, ILogger<MediaCollector> logger) : BackgroundService
{
    private static readonly MediaSignal Stopped = new(false, null, null, null);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "System media controls are not available");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        MediaSignal? last = null;
        do
        {
            var signal = await ReadAsync(manager);
            if (signal != last)
            {
                last = signal;
                bus.Publish(signal);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<MediaSignal> ReadAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var process = foreground.Current.ProcessName;
            var session = manager.GetSessions().FirstOrDefault(s => IsPlaying(s) && MediaSources.IsFrom(s.SourceAppUserModelId, process))
                ?? manager.GetCurrentSession();
            if (session is null || !IsPlaying(session))
            {
                return Stopped;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            return new MediaSignal(true, Blank(props?.Title), Blank(props?.Artist), session.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Reading media session failed");
            return Stopped;
        }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
