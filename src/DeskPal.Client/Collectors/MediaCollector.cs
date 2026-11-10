using DeskPal.Client.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;

namespace DeskPal.Client.Collectors;

/// <summary>Reads what the system media controls show (song or video title), the same info as the volume flyout.</summary>
public sealed class MediaCollector(ActivityBus bus, ILogger<MediaCollector> logger) : BackgroundService
{
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

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
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
            var session = manager.GetCurrentSession();
            if (session is null
                || session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return new MediaSignal(false, null, null, null);
            }

            var props = await session.TryGetMediaPropertiesAsync();
            return new MediaSignal(true, Blank(props?.Title), Blank(props?.Artist), session.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Reading media session failed");
            return new MediaSignal(false, null, null, null);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}