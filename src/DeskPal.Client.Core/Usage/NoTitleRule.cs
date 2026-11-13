using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Usage;

public enum NoTitleKind
{
    /// <summary>The app name shown on the status page, e.g. 招商银行.</summary>
    App,

    /// <summary>The process name without ".exe".</summary>
    Process,

    /// <summary>A site and its subdomains. Browsers only expose the host, not the path.</summary>
    Domain,

    /// <summary>Any window, page or media title containing the text.</summary>
    Title,
}

/// <summary>Keeps titles out of the usage stats. A matching segment keeps its category and app or site name.</summary>
public sealed record NoTitleRule(NoTitleKind Kind, string Pattern)
{
    public const int MaxLength = 100;

    /// <returns>Null when nothing usable is left, e.g. "https://" for a site.</returns>
    public static NoTitleRule? Create(NoTitleKind kind, string? text)
    {
        var pattern = kind switch
        {
            NoTitleKind.Process => RulePattern.Normalize(RuleKind.Process, text),
            NoTitleKind.Domain => RulePattern.Normalize(RuleKind.Domain, text).TrimStart('*', '.'),
            _ => (text ?? "").Trim(),
        };
        return Enum.IsDefined(kind) && pattern.Length is > 0 and <= MaxLength ? new NoTitleRule(kind, pattern) : null;
    }

    /// <param name="domain">Host of the page in front, normalized; null when no browser is in front.</param>
    public bool Matches(string? app, string? process, string? domain, string? title) => Kind switch
    {
        NoTitleKind.App => string.Equals(app, Pattern, StringComparison.OrdinalIgnoreCase),
        NoTitleKind.Process => process is not null && RulePattern.Normalize(RuleKind.Process, process) == Pattern,
        NoTitleKind.Domain => domain is not null && (domain == Pattern || domain.EndsWith("." + Pattern, StringComparison.Ordinal)),
        NoTitleKind.Title => title?.Contains(Pattern, StringComparison.OrdinalIgnoreCase) == true,
        _ => false,
    };

    public bool SameAs(NoTitleRule other) => Kind == other.Kind && string.Equals(Pattern, other.Pattern, StringComparison.OrdinalIgnoreCase);
}