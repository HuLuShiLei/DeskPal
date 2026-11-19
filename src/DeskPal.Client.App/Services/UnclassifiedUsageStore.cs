using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// Saves today's unrecognized-app minutes for the signed-in account every minute and on exit, and restores them on start.
/// Without it a restart counts from zero and the next report lowers the minutes on the server.
/// </summary>
public sealed class UnclassifiedUsageStore(AppSettings settings, UnclassifiedUsage usage, ILogger<UnclassifiedUsageStore> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private sealed record SavedUsage(DateOnly Day, List<UnclassifiedApp> Apps);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Load();
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        Save();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Save();
        }
    }

    private string? FilePath => settings.UserId is { } userId ? Path.Combine(SettingsStore.Directory, $"unclassified-{userId}.json") : null;

    private void Load()
    {
        if (FilePath is not { } path || !File.Exists(path))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedUsage>(File.ReadAllText(path), DeskPalJson.Options);
            if (saved is not null && saved.Day == DateOnly.FromDateTime(DateTime.Now))
            {
                usage.Restore(saved.Day, saved.Apps);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Reading saved unrecognized apps failed");
        }
    }

    private void Save()
    {
        var (day, apps) = usage.Export();
        if (FilePath is not { } path || day is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SavedUsage(day.Value, apps.ToList()), DeskPalJson.Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Saving unrecognized apps failed");
        }
    }
}
