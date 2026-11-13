using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Ui;

namespace DeskPal.Client.Core.Updates;

/// <summary>One "## 版本号" section of CHANGELOG.md.</summary>
public sealed record ChangelogSection(string Version, IReadOnlyList<string> Items);

/// <summary>
/// CHANGELOG.md in the repository root, copied next to DeskPal.exe. Sections are "## X.Y.Z", newest first; items are "- " lines.
/// Test builds have no section of their own: their items sit under the next stable version.
/// </summary>
public static class Changelog
{
    public const string FileName = "CHANGELOG.md";

    public static IReadOnlyList<ChangelogSection> Parse(string markdown)
    {
        var sections = new List<ChangelogSection>();
        string? version = null;
        List<string>? items = null;

        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                var heading = line[3..].Trim();
                var end = heading.IndexOfAny([' ', '\t', '（', '(']);
                version = (end < 0 ? heading : heading[..end]).TrimStart('v', 'V');
                items = [];
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                Flush();
                version = null;
                items = null;
            }
            else if (items is not null && (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)))
            {
                var text = line[2..].Trim();
                if (text.Length > 0)
                {
                    items.Add(text);
                }
            }
            else if (items is { Count: > 0 } && raw.Length > 0 && char.IsWhiteSpace(raw[0]) && line.Trim().Length > 0)
            {
                items[^1] = $"{items[^1]} {line.Trim()}";
            }
        }

        Flush();
        return sections;

        void Flush()
        {
            if (version is { Length: > 0 } && items is { Count: > 0 })
            {
                sections.Add(new ChangelogSection(version, items));
            }
        }
    }

    public static IEnumerable<string> AllItems(IEnumerable<ChangelogSection> sections) => sections.SelectMany(s => s.Items);

    /// <summary>
    /// What to show after an update: the items not seen on this computer, grouped by version.
    /// Null when nothing is new. Items are matched by text, so moving an item to another version does not show it again.
    /// </summary>
    public static WhatsNewView? WhatsNew(IReadOnlyList<ChangelogSection> sections, IReadOnlySet<string> seen, string? previousVersion, string currentVersion)
    {
        var shown = new HashSet<string>(seen, StringComparer.Ordinal);
        var unseen = new List<WhatsNewSection>();
        foreach (var section in sections)
        {
            var items = section.Items.Where(shown.Add).ToList();
            if (items.Count > 0)
            {
                unseen.Add(new WhatsNewSection(section.Version, items));
            }
        }

        if (unseen.Count == 0)
        {
            return null;
        }

        var count = unseen.Sum(s => s.Items.Count);
        var summary = string.IsNullOrEmpty(previousVersion) || previousVersion == currentVersion
            ? UiLanguage.Format(Strings.WhatsNew_Count, count)
            : UiLanguage.Format(Strings.WhatsNew_CountSince, previousVersion, count);
        return new WhatsNewView(UiLanguage.Format(Strings.WhatsNew_Title, currentVersion), summary, unseen);
    }
}