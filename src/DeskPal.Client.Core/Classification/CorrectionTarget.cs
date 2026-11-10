using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Classification;

/// <summary>What a "这条分错了" correction is recorded against: the browser domain when known, otherwise the process.</summary>
public sealed record CorrectionTarget(RuleKind Kind, string Pattern, string DisplayName)
{
    public static CorrectionTarget? From(ActivitySnapshot snapshot, string? currentDisplayName)
    {
        var foreground = snapshot.Foreground;
        if (foreground is null)
        {
            return null;
        }

        if (foreground.IsBrowser && snapshot.BrowserDomain is { Length: > 0 } domain)
        {
            var host = RulePattern.NormalizeHost(domain);
            return new CorrectionTarget(RuleKind.Domain, host, currentDisplayName ?? host);
        }

        var process = RulePattern.Normalize(RuleKind.Process, foreground.ProcessName);
        return new CorrectionTarget(RuleKind.Process, process, currentDisplayName ?? foreground.AppDisplayName);
    }
}