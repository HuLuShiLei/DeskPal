using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Classification;

public sealed class RuleSet
{
    private readonly Dictionary<string, RuleDto> _processes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuleDto> _domains = new(StringComparer.Ordinal);
    private readonly List<(string Keyword, RuleDto Rule)> _titleKeywords = new();

    public static readonly RuleSet Empty = new([]);

    /// <param name="rules">Earlier rules win when two rules have the same kind and pattern.</param>
    public RuleSet(IEnumerable<RuleDto> rules)
    {
        foreach (var rule in rules)
        {
            var pattern = RulePattern.Normalize(rule.Kind, rule.Pattern);
            if (pattern.Length == 0)
            {
                continue;
            }

            switch (rule.Kind)
            {
                case RuleKind.Process:
                    _processes.TryAdd(pattern, rule);
                    break;
                case RuleKind.Domain:
                    _domains.TryAdd(pattern, rule);
                    break;
                case RuleKind.TitleKeyword:
                    if (_titleKeywords.All(k => k.Keyword != pattern))
                    {
                        _titleKeywords.Add((pattern, rule));
                    }

                    break;
            }
        }
    }

    public int Count => _processes.Count + _domains.Count + _titleKeywords.Count;

    /// <summary>Browser: domain, then title keyword, then process. Other apps: process only.</summary>
    public RuleDto? Match(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground;
        if (foreground is null)
        {
            return null;
        }

        if (foreground.IsBrowser)
        {
            if (snapshot.BrowserDomain is { Length: > 0 } domain && MatchDomain(domain) is { } byDomain)
            {
                return byDomain;
            }

            if (MatchTitle(foreground.WindowTitle) is { } byTitle)
            {
                return byTitle;
            }
        }

        return MatchProcess(foreground.ProcessName);
    }

    public RuleDto? MatchProcess(string process) => _processes.GetValueOrDefault(RulePattern.Normalize(RuleKind.Process, process));

    /// <summary>First title keyword contained in the title, ignoring case.</summary>
    public RuleDto? MatchTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var lower = title.ToLowerInvariant();
        foreach (var (keyword, rule) in _titleKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>Most specific suffix wins: for "live.bilibili.com" try the full host first, then "bilibili.com".</summary>
    public RuleDto? MatchDomain(string host)
    {
        var current = RulePattern.NormalizeHost(host);
        while (current.Length > 0)
        {
            if (_domains.TryGetValue(current, out var rule))
            {
                return rule;
            }

            var dot = current.IndexOf('.');
            if (dot < 0)
            {
                return null;
            }

            current = current[(dot + 1)..];
        }

        return null;
    }
}