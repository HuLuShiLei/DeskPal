using DeskPal.Client.Core.Classification;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// With the user's consent, reports today's unrecognized desktop apps (process name, product name, minutes) so the
/// builtin rules can learn them. Never window titles, sites or times of use.
/// </summary>
public sealed class UsageReporter(ApiClient api, AppSettings settings, UnclassifiedUsage usage, ILogger<UsageReporter> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(2);
    private static readonly TimeSpan MinUsage = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (settings.ShareUsage != true)
            {
                continue;
            }

            var apps = usage.Top(MinUsage, 50)
                .Select(a => new UnknownAppItem(a.Kind, a.Pattern, a.DisplayName, (int)a.Today.TotalMinutes))
                .ToList();
            if (apps.Count == 0)
            {
                continue;
            }

            try
            {
                await api.ReportUsageAsync(new UsageReportRequest(apps));
                logger.LogInformation("Reported {Count} unrecognized apps", apps.Count);
            }
            catch (ApiException ex)
            {
                logger.LogWarning("Reporting usage failed ({Status})", ex.Status);
            }
        }
    }
}
