using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

public static class PresenceFormatter
{
    public static string Describe(PresenceSnapshot snapshot)
    {
        var head = snapshot.State switch
        {
            PresenceState.Offline => "离线",
            PresenceState.Paused => "暂停分享",
            PresenceState.Custom => snapshot.CustomText ?? "自定义状态",
            PresenceState.Locked => "离开（已锁屏）",
            PresenceState.Away => "离开座位",
            PresenceState.Working => "忙",
            PresenceState.Meeting => "开会中",
            PresenceState.Communicating => "在沟通",
            PresenceState.Other => snapshot.AppDisplayName is { Length: > 0 } app ? $"正在使用 {app}" : "忙",
            PresenceState.Slacking => SlackingLabel(snapshot.Category),
            _ => snapshot.State.ToString(),
        };

        if (snapshot.State != PresenceState.Other && snapshot.AppDisplayName is { Length: > 0 } name)
        {
            head = $"{head} · {name}";
        }

        return snapshot.Detail is { Length: > 0 } detail ? $"{head}：{detail}" : head;
    }

    public static string SlackingLabel(ActivityCategory category) => category switch
    {
        ActivityCategory.Video => "在看视频",
        ActivityCategory.Music => "在听音乐",
        ActivityCategory.Shopping => "在购物",
        ActivityCategory.Social => "在刷社交网站",
        ActivityCategory.News => "在看新闻",
        ActivityCategory.Game => "在玩游戏",
        ActivityCategory.Reading => "在看小说",
        _ => "摸鱼中",
    };

    public static string CategoryName(ActivityCategory category) => category switch
    {
        ActivityCategory.Work => "工作",
        ActivityCategory.Meeting => "开会",
        ActivityCategory.Communication => "沟通",
        ActivityCategory.Video => "视频",
        ActivityCategory.Music => "音乐",
        ActivityCategory.Shopping => "购物",
        ActivityCategory.Social => "社交",
        ActivityCategory.News => "新闻",
        ActivityCategory.Game => "游戏",
        ActivityCategory.Reading => "阅读",
        ActivityCategory.Other => "其他",
        _ => "无",
    };

    public static string PrivacyLevelName(PrivacyLevel level) => level switch
    {
        PrivacyLevel.StateOnly => "只显示忙 / 摸鱼",
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
}