using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Ui;

namespace DeskPal.Client.Core.Updates;

/// <summary>One changelog entry. Seen-state uses <see cref="Zh"/>. <see cref="Text"/> follows the interface language.</summary>
public sealed record ChangelogItem(string Zh, string En)
{
    public string Text => UiLanguage.Current == UiLanguage.English && En.Length > 0 ? En : Zh;
}

/// <summary>One "## version" section of CHANGELOG.md.</summary>
public sealed record ChangelogSection(string Version, IReadOnlyList<ChangelogItem> Items)
{
    public ChangelogSection(string version, IReadOnlyList<string> items)
        : this(version, items.Select(text => new ChangelogItem(text, "")).ToArray())
    {
    }
}

/// <summary>
/// CHANGELOG.md in the repository root, copied next to DeskPal.exe.
/// Sections are "## X.Y.Z", newest first. An item is a "- " line; an indented "en:" line is its English text.
/// Other indented lines continue the line above. Test builds have no section of their own: their items sit under the next stable version.
/// </summary>
public static class Changelog
{
    public const string FileName = "CHANGELOG.md";

    public static IReadOnlyList<ChangelogSection> Parse(string markdown)
    {
        var sections = new List<ChangelogSection>();
        string? version = null;
        List<Draft>? items = null;

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
                    items.Add(new Draft { Zh = text });
                }
            }
            else if (items is { Count: > 0 } && raw.Length > 0 && char.IsWhiteSpace(raw[0]) && line.Trim().Length > 0)
            {
                var draft = items[^1];
                var text = line.Trim();
                if (text.StartsWith("en:", StringComparison.Ordinal))
                {
                    var english = text[3..].Trim();
                    draft.En = draft.En.Length == 0 ? english : draft.En + " " + english;
                    draft.InEnglish = true;
                }
                else if (draft.InEnglish)
                {
                    draft.En = draft.En + " " + text;
                }
                else
                {
                    draft.Zh = draft.Zh + " " + text;
                }
            }
        }

        Flush();
        return sections;

        void Flush()
        {
            if (version is { Length: > 0 } && items is { Count: > 0 })
            {
                sections.Add(new ChangelogSection(version, items.Select(item => new ChangelogItem(item.Zh, item.En)).ToArray()));
            }
        }
    }

    /// <summary>Chinese text of every item. This is what a computer remembers as already shown.</summary>
    public static IEnumerable<string> AllItems(IEnumerable<ChangelogSection> sections) =>
        sections.SelectMany(section => section.Items.Select(item => item.Zh));

    /// <summary>
    /// What to show after an update: items not seen on this computer, grouped by version, in the current interface language.
    /// Null when nothing is new. Items are matched by the Chinese text, so an English edit or a move to another version does not show the item again.
    /// </summary>
    public static WhatsNewView? WhatsNew(IReadOnlyList<ChangelogSection> sections, IReadOnlySet<string> seen, string? previousVersion, string currentVersion)
    {
        var shown = new HashSet<string>(seen, StringComparer.Ordinal);
        var unseen = new List<WhatsNewSection>();
        foreach (var section in sections)
        {
            var items = new List<string>();
            foreach (var item in section.Items)
            {
                if (shown.Add(item.Zh))
                {
                    items.Add(item.Text);
                }
            }

            if (items.Count > 0)
            {
                unseen.Add(new WhatsNewSection(section.Version, items));
            }
        }

        if (unseen.Count == 0)
        {
            return null;
        }

        var count = unseen.Sum(section => section.Items.Count);
        var summary = string.IsNullOrEmpty(previousVersion) || previousVersion == currentVersion
            ? UiLanguage.Format(Strings.WhatsNew_Count, count)
            : UiLanguage.Format(Strings.WhatsNew_CountSince, previousVersion, count);
        return new WhatsNewView(UiLanguage.Format(Strings.WhatsNew_Title, currentVersion), summary, unseen);
    }

    private sealed class Draft
    {
        public string Zh { get; set; } = "";
        public string En { get; set; } = "";
        public bool InEnglish { get; set; }
    }
}
