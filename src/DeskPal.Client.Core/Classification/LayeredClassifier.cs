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
}

public sealed record ClassificationResult(ActivityCategory Category, string? AppDisplayName, RuleLayer Layer);

/// <summary>Personal overrides, then Room shared rules, then builtin rules, then heuristics, then Other.</summary>
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

    private static ActivityCategory? Heuristic(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground!;
        if (foreground.ProcessPath?.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ActivityCategory.Game;
        }

        if (foreground.IsFullscreen && (foreground.IsBrowser || snapshot.Media.IsPlaying))
        {
            return ActivityCategory.Video;
        }

        if (snapshot.Media.IsPlaying)
        {
            return ActivityCategory.Music;
        }

        return null;
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