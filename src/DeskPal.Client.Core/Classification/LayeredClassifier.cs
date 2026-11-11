using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Classification;

public enum RuleLayer
{
    Personal,
    Room,
    Builtin,
    Heuristic,
    Fallback,
    Browser,
}

public sealed record ClassificationResult(ActivityCategory Category, string? AppDisplayName, RuleLayer Layer);

/// <summary>
/// Desktop apps: personal, room, builtin, heuristic, then Other.
/// A browser checks personal and room rules only; otherwise it is Browsing, named by domain.
/// </summary>
public sealed class LayeredClassifier
{
    private volatile RuleSet _personal = RuleSet.Empty;
    private volatile RuleSet _room = RuleSet.Empty;
    private volatile RuleSet _builtin = RuleSet.Empty;

    public void SetPersonal(RuleSet rules) => _personal = rules;

    public void SetRoom(RuleSet rules) => _room = rules;

    public void SetBuiltin(RuleSet rules) => _builtin = rules;

    public ClassificationResult Classify(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground;
        if (foreground is null)
        {
            return new ClassificationResult(ActivityCategory.Other, null, RuleLayer.Fallback);
        }

        if (foreground.IsBrowser)
        {
            return ClassifyBrowser(snapshot);
        }

        foreach (var (layer, rules) in new[] { (RuleLayer.Personal, _personal), (RuleLayer.Room, _room), (RuleLayer.Builtin, _builtin) })
        {
            if (rules.Match(snapshot) is { } rule)
            {
                return new ClassificationResult(rule.Category, rule.Name ?? DefaultName(snapshot), layer);
            }
        }

        if (Heuristic(snapshot) is { } category)
        {
            return new ClassificationResult(category, DefaultName(snapshot), RuleLayer.Heuristic);
        }

        return new ClassificationResult(ActivityCategory.Other, DefaultName(snapshot), RuleLayer.Fallback);
    }

    /// <summary>Personal and room rules can still reclassify a site. Builtin site rules do not.</summary>
    private ClassificationResult ClassifyBrowser(ActivitySnapshot snapshot)
    {
        foreach (var (layer, rules) in new[] { (RuleLayer.Personal, _personal), (RuleLayer.Room, _room) })
        {
            if (rules.Match(snapshot) is { } rule)
            {
                return new ClassificationResult(rule.Category, rule.Name ?? BrowserSubject(snapshot), layer);
            }
        }

        return new ClassificationResult(ActivityCategory.Browsing, BrowserSubject(snapshot), RuleLayer.Browser);
    }

    private static ActivityCategory? Heuristic(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground!;
        if (foreground.ProcessPath?.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ActivityCategory.Game;
        }

        if (foreground.IsFullscreen && snapshot.Media.IsPlaying)
        {
            return ActivityCategory.Video;
        }

        if (snapshot.Media.IsPlaying)
        {
            return ActivityCategory.Music;
        }

        return null;
    }

    /// <summary>Domain only. The browser product name is not the subject of the page.</summary>
    private static string? BrowserSubject(ActivitySnapshot snapshot)
    {
        if (snapshot.BrowserDomain is not { Length: > 0 } domain)
        {
            return null;
        }

        var host = RulePattern.NormalizeHost(domain);
        return host.Length > 0 ? host : null;
    }

    private static string DefaultName(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground!;
        if (foreground.IsBrowser && snapshot.BrowserDomain is { Length: > 0 } domain)
        {
            return RulePattern.NormalizeHost(domain);
        }

        return foreground.AppDisplayName;
    }
}