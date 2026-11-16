using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

public static class PresenceResolver
{
    private static readonly string[] BrowserTitleSuffixes =
    [
        " - Google Chrome", " - Microsoft Edge", " - Mozilla Firefox", " - 360安全浏览器", " - QQ浏览器", " - 搜狗高速浏览器",
    ];

    /// <summary>
    /// Priority: paused, locked, idle, then the classified foreground activity.
    /// Watching a video is not idle: the program in front plays it, or a video player is full screen
    /// (players that do not report to the system media controls).
    /// </summary>
    public static LocalPresence Resolve(ActivitySnapshot snapshot, ClassificationResult classification, bool paused)
    {
        if (paused)
        {
            return new LocalPresence(PresenceState.Paused, ActivityCategory.None, null, null);
        }

        if (snapshot.IsLocked)
        {
            return new LocalPresence(PresenceState.Locked, ActivityCategory.None, null, null);
        }

        var watching = classification.Category == ActivityCategory.Video
            && (MediaSources.PlayingInForeground(snapshot) || snapshot.Foreground?.IsFullscreen == true);
        if (snapshot.IsIdle && !watching)
        {
            return new LocalPresence(PresenceState.Away, ActivityCategory.None, null, null);
        }

        return new LocalPresence(
            classification.Category.ToState(),
            classification.Category,
            classification.AppDisplayName,
            BuildDetail(snapshot, classification.Category));
    }

    /// <summary>The media title only when the program in front plays it; music in the background is not what I am looking at.</summary>
    private static string? BuildDetail(ActivitySnapshot snapshot, ActivityCategory category)
    {
        if (category.IsSlacking() && MediaSources.PlayingInForeground(snapshot) && TitleText.Clean(snapshot.Media.Title) is { } title)
        {
            return string.IsNullOrWhiteSpace(snapshot.Media.Artist) ? title : $"{title} - {snapshot.Media.Artist.Trim()}";
        }

        var windowTitle = snapshot.Foreground?.WindowTitle;
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        foreach (var suffix in BrowserTitleSuffixes)
        {
            if (windowTitle.EndsWith(suffix, StringComparison.Ordinal))
            {
                windowTitle = windowTitle[..^suffix.Length];
                break;
            }
        }

        return TitleText.Clean(windowTitle);
    }
}
