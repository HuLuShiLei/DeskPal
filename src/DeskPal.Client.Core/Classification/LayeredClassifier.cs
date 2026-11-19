using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Classification;

public enum RuleLayer
{
    Personal,
    Builtin,
    Heuristic,
    Fallback,
    Browser,
}

/// <param name="BaseCategory">What the activity would be without my personal rules. Stats keep it, so removing a rule reclassifies history.</param>
public sealed record ClassificationResult(ActivityCategory Category, string? AppDisplayName, RuleLayer Layer, ActivityCategory? BaseCategory = null)
{
    public ActivityCategory Default => BaseCategory ?? Category;
}

/// <summary>
/// Desktop apps: personal, builtin, heuristic, then Other.
/// A browser checks personal rules; otherwise it is Video while it plays the video of the open tab, else Browsing, named by domain.
/// Personal rules belong to the account and are the same in every room. Only my own result changes:
/// room members see the category I publish, they never reclassify it with their own rules.
/// Music, Shopping, Social, News and Reading rules are skipped. Background playback is not a category.
/// </summary>
public sealed class LayeredClassifier
{
    /// <summary>
    /// Folders of game libraries and launchers. The launchers themselves live elsewhere or have builtin rules.
    /// Written with \ and matched on Windows and macOS paths alike (/ is compared as \).
    /// </summary>
    private static readonly string[] GameFolders =
    [
        @"\steamapps\", @"\Epic Games\", @"\WeGameApps\", @"\XboxGames\", @"\GOG Galaxy\Games\", @"\Riot Games\",
        @"\HoYoPlay\games\", @"\miHoYo\", @"\Ubisoft Game Launcher\games\", @"\EA Games\", @"\FINAL FANTASY XIV",
    ];

    private volatile RuleSet _personal = RuleSet.Empty;
    private volatile RuleSet _builtin = RuleSet.Empty;

    public void SetPersonal(RuleSet rules) => _personal = rules;

    public void SetBuiltin(RuleSet rules) => _builtin = rules;

    public ClassificationResult Classify(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground;
        if (foreground is null)
        {
            return new ClassificationResult(ActivityCategory.Other, null, RuleLayer.Fallback);
        }

        var standard = foreground.IsBrowser ? ClassifyBrowser(snapshot) : ClassifyApp(snapshot);
        if (_personal.Match(snapshot) is { } rule && !rule.Category.IsRetired())
        {
            var name = rule.Name ?? (foreground.IsBrowser ? BrowserSubject(snapshot) : DefaultName(snapshot));
            return new ClassificationResult(rule.Category, name, RuleLayer.Personal, standard.Category);
        }

        return standard;
    }

    private ClassificationResult ClassifyApp(ActivitySnapshot snapshot)
    {
        if (_builtin.Match(snapshot) is { } rule && !rule.Category.IsRetired())
        {
            return new ClassificationResult(rule.Category, rule.Name ?? DefaultName(snapshot), RuleLayer.Builtin);
        }

        if (Heuristic(snapshot) is { } category)
        {
            return new ClassificationResult(category, DefaultName(snapshot), RuleLayer.Heuristic);
        }

        return new ClassificationResult(ActivityCategory.Other, DefaultName(snapshot), RuleLayer.Fallback);
    }

    /// <summary>Builtin site rules do not apply. Any site playing the video of the open tab counts as Video.</summary>
    private static ClassificationResult ClassifyBrowser(ActivitySnapshot snapshot) =>
        new(PlaysTabVideo(snapshot) ? ActivityCategory.Video : ActivityCategory.Browsing, BrowserSubject(snapshot), RuleLayer.Browser);

    /// <summary>The browser in front plays something, and its media title is part of the tab title (not a tab in the background).</summary>
    private static bool PlaysTabVideo(ActivitySnapshot snapshot)
    {
        if (!MediaSources.PlayingInForeground(snapshot)
            || TitleText.Clean(snapshot.Media.Title) is not { Length: >= 2 } media
            || TitleText.Clean(snapshot.Foreground!.WindowTitle) is not { } tab)
        {
            return false;
        }

        return tab.Contains(media, StringComparison.OrdinalIgnoreCase);
    }

    private static ActivityCategory? Heuristic(ActivitySnapshot snapshot)
    {
        var foreground = snapshot.Foreground!;
        var path = foreground.ProcessPath?.Replace('/', '\\');
        if (path is not null && GameFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase)) && !IsGameTool(path))
        {
            return ActivityCategory.Game;
        }

        if (foreground.IsFullscreen && MediaSources.PlayingInForeground(snapshot))
        {
            return ActivityCategory.Video;
        }

        return null;
    }

    /// <summary>Launchers, updaters and crash reporters live next to the games but are not playing.</summary>
    private static bool IsGameTool(string path)
    {
        var file = Path.GetFileNameWithoutExtension(path[(path.LastIndexOf('\\') + 1)..]);
        return path.Contains(@"\Launcher\", StringComparison.OrdinalIgnoreCase)
            || new[] { "launcher", "helper", "crash", "setup", "update", "install" }.Any(w => file.Contains(w, StringComparison.OrdinalIgnoreCase));
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
