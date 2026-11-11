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

    /// <summary>Priority: paused, manual status, locked, idle, then the classified foreground activity.</summary>
    public static LocalPresence Resolve(ActivitySnapshot snapshot, ClassificationResult classification, string? manualText, bool paused)
    {
        if (paused)
        {
            return new LocalPresence(PresenceState.Paused, ActivityCategory.None, null, null, null);
        }

        if (!string.IsNullOrWhiteSpace(manualText))
        {
            return new LocalPresence(PresenceState.Custom, ActivityCategory.None, null, null, manualText.Trim());
        }

        if (snapshot.IsLocked)
        {
            return new LocalPresence(PresenceState.Locked, ActivityCategory.None, null, null, null);
        }


        var watchingFullscreen = snapshot.Foreground?.IsFullscreen == true && snapshot.Media.IsPlaying;
        if (snapshot.IsIdle && !watchingFullscreen)
        {
            return new LocalPresence(PresenceState.Away, ActivityCategory.None, null, null, null);
        }

        return new LocalPresence(
            classification.Category.ToState(),
            classification.Category,
            classification.AppDisplayName,
            BuildDetail(snapshot, classification.Category),
            null);
    }

    private static string? BuildDetail(ActivitySnapshot snapshot, ActivityCategory category)
    {
        if (category.IsSlacking() && snapshot.Media is { IsPlaying: true, Title: { Length: > 0 } title })
        {
            return string.IsNullOrWhiteSpace(snapshot.Media.Artist) ? title : $"{title} - {snapshot.Media.Artist}";
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
                return windowTitle[..^suffix.Length];
            }
        }

        return windowTitle;
    }
}