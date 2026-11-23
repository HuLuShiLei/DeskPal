using DeskPal.Client.Core.Classification;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// With the user's consent, reports today's unrecognized desktop apps (process name, product name, minutes, and the
/// category guessed for an app in a game library) so the
/// builtin rules can learn them. Never window titles, sites or times of use. While personal data stays off the server,
/// the personal rules for apps (process, category, name) go along for the same purpose.
/// </summary>
public sealed class UsageReporter(ApiClient api, AppSettings settings, UnclassifiedUsage usage, PersonalRules personalRules, ILogger<UsageReporter> logger)
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

            var apps = usage.Top(MinUsage, 50, includeGuessed: true)
                .Select(a => new UnknownAppItem(a.Kind, a.Pattern, a.DisplayName, (int)a.Today.TotalMinutes, a.Guessed))
                .ToList();
            var rules = settings.PersonalSync
                ? null
                : personalRules.Rules.Where(r => r.Kind == RuleKind.Process).Select(r => new SharedRuleItem(r.Pattern, r.Category, r.Name)).ToList();
            if (apps.Count == 0 && rules is null)
            {
                continue;
            }

            try
            {
                await api.ReportUsageAsync(new UsageReportRequest(apps, rules));
                logger.LogInformation("Reported {Count} unrecognized apps", apps.Count);
            }
            catch (ApiException ex)
            {
                logger.LogWarning("Reporting usage failed ({Status})", ex.Status);
            }
        }
    }
}
