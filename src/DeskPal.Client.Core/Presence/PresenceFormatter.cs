using System.Globalization;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

/// <param name="Headline">State line, e.g. "工作中" or "正在使用 XMind".</param>
/// <param name="App">App name, null when the headline already contains it.</param>
/// <param name="Detail">Window or media title, shortened for display.</param>
public sealed record PresenceView(string Headline, string? App, string? Detail);

public static class PresenceFormatter
{
    public const int DetailMaxLength = 36;

    /// <summary>Categories a user can pick when correcting a classification or setting privacy. Meeting is no longer produced.</summary>
    public static IReadOnlyList<ActivityCategory> SelectableCategories { get; } = Enum.GetValues<ActivityCategory>()
        .Where(c => c is not (ActivityCategory.None or ActivityCategory.Meeting))
        .ToList();

    public static PresenceView Format(PresenceSnapshot snapshot)
    {
        var app = snapshot.AppDisplayName is { Length: > 0 } name ? name : null;
        var headline = snapshot.State switch
        {
            PresenceState.Offline => "离线",
            PresenceState.Paused => "暂停分享",
            PresenceState.Custom => snapshot.CustomText ?? "自定义状态",
            PresenceState.Locked => "已锁屏",
            PresenceState.Away => "暂时离开",
            PresenceState.Working or PresenceState.Meeting => "工作中",
            PresenceState.Communicating => "在聊天",
            PresenceState.Other => app is null ? "工作中" : $"正在使用 {app}",
            PresenceState.Slacking => SlackingLabel(snapshot.Category),
            _ => snapshot.State.ToString(),
        };

        return new PresenceView(headline, snapshot.State == PresenceState.Other ? null : app, Shorten(snapshot.Detail));
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
        ActivityCategory.Video => "放松 · 视频",
        ActivityCategory.Music => "放松 · 音乐",
        ActivityCategory.Shopping => "放松 · 购物",
        ActivityCategory.Social => "放松 · 社交网站",
        ActivityCategory.News => "放松 · 新闻",
        ActivityCategory.Game => "放松 · 游戏",
        ActivityCategory.Reading => "放松 · 阅读",
        ActivityCategory.Other => "其他",
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