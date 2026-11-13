using System.Globalization;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

/// <param name="Headline">State line, e.g. "工作中" or "正在使用 XMind".</param>
/// <param name="App">App name, null when the headline already contains it.</param>
/// <param name="Detail">Window or media title, shortened for display.</param>
/// <param name="Tone">Color of the status dot: work, chat, relax, browse, other, away, offline.</param>
public sealed record PresenceView(string Headline, string? App, string? Detail, string Tone = "offline");

public static class PresenceFormatter
{
    public const int DetailMaxLength = 36;

    /// <summary>Categories a user can pick when correcting a classification or setting privacy. Meeting and the retired leisure categories are no longer produced. Browsing is listed before Other.</summary>
    public static IReadOnlyList<ActivityCategory> SelectableCategories { get; } =
    [
        ActivityCategory.Work,
        ActivityCategory.Communication,
        ActivityCategory.Video,
        ActivityCategory.Game,
        ActivityCategory.Browsing,
        ActivityCategory.Other,
    ];

    public static PresenceView Format(PresenceSnapshot snapshot)
    {
        var app = snapshot.AppDisplayName is { Length: > 0 } name ? name : null;
        var state = snapshot.State == PresenceState.Slacking && snapshot.Category.IsRetired()
            ? snapshot.Category == ActivityCategory.Music ? PresenceState.Other : PresenceState.Browsing
            : snapshot.State;
        var headline = state switch
        {
            PresenceState.Offline => "离线",
            PresenceState.Paused => "暂停分享",
            // Older clients may still send a manual status.
            PresenceState.Custom => snapshot.CustomText ?? "自定义状态",
            PresenceState.Locked => "已锁屏",
            PresenceState.Away => "暂时离开",
            PresenceState.Working or PresenceState.Meeting => "工作中",
            PresenceState.Communicating => "在聊天",
            PresenceState.Other => app is null ? "工作中" : $"正在使用 {app}",
            PresenceState.Slacking => SlackingLabel(snapshot.Category),
            PresenceState.Browsing => "正在浏览网页",
            _ => snapshot.State.ToString(),
        };

        var tone = state switch
        {
            PresenceState.Working or PresenceState.Meeting => "work",
            PresenceState.Communicating => "chat",
            PresenceState.Slacking => "relax",
            PresenceState.Browsing => "browse",
            PresenceState.Other or PresenceState.Custom => "other",
            PresenceState.Away or PresenceState.Locked or PresenceState.Paused => "away",
            _ => "offline",
        };

        return new PresenceView(headline, state == PresenceState.Other ? null : app, OmitDuplicate(app, Shorten(snapshot.Detail)), tone);
    }

    public static string OneLine(PresenceView view) =>
        string.Join(" · ", new[] { view.Headline, view.App, view.Detail }.Where(part => !string.IsNullOrEmpty(part)));

    public static string SlackingLabel(ActivityCategory category) => category switch
    {
        ActivityCategory.Video => "在看视频",
        ActivityCategory.Music => "在听音乐",
        ActivityCategory.Shopping => "在购物",
        ActivityCategory.Social => "在看社交网站",
        ActivityCategory.News => "在看新闻",
        ActivityCategory.Game => "在玩游戏",
        ActivityCategory.Reading => "在看小说",
        _ => "在放松",
    };

    public static string CategoryName(ActivityCategory category) => category switch
    {
        ActivityCategory.Work or ActivityCategory.Meeting => "工作",
        ActivityCategory.Communication => "聊天",
        ActivityCategory.Video => "视频",
        ActivityCategory.Music => "音乐",
        ActivityCategory.Shopping => "购物",
        ActivityCategory.Social => "社交网站",
        ActivityCategory.News => "新闻",
        ActivityCategory.Game => "游戏",
        ActivityCategory.Reading => "阅读",
        ActivityCategory.Other => "其他",
        ActivityCategory.Browsing => "浏览网页",
        _ => "无",
    };

    public static string PrivacyLevelName(PrivacyLevel level) => level switch
    {
        PrivacyLevel.StateOnly => "只显示状态",
        PrivacyLevel.Category => "显示类别和应用",
        PrivacyLevel.Detail => "显示详情（标题）",
        _ => level.ToString(),
    };

    public static string Duration(DateTimeOffset since, DateTimeOffset now)
    {
        var span = now - since;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} 分钟";
        }

        return span.Minutes == 0 ? $"{(int)span.TotalHours} 小时" : $"{(int)span.TotalHours} 小时 {span.Minutes} 分钟";
    }

    /// <summary>The window title is not shown again when it is the same as the app name.</summary>
    private static string? OmitDuplicate(string? app, string? detail) =>
        detail is not null && app is not null && string.Equals(detail, app.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : detail;

    private static string? Shorten(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var info = new StringInfo(detail.Trim());
        return info.LengthInTextElements <= DetailMaxLength
            ? info.String
            : info.SubstringByTextElements(0, DetailMaxLength - 1) + "…";
    }
}
