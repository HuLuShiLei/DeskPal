using System.Globalization;
using DeskPal.Client.Core.Localization;
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
            PresenceState.Offline => Strings.State_Offline,
            PresenceState.Paused => Strings.State_Paused,
            // Older clients may still send a manual status.
            PresenceState.Custom => snapshot.CustomText ?? Strings.State_Custom,
            PresenceState.Locked => Strings.State_Locked,
            PresenceState.Away => Strings.State_Away,
            PresenceState.Working or PresenceState.Meeting => Strings.State_Working,
            PresenceState.Communicating => Strings.State_Chatting,
            PresenceState.Other => app is null ? Strings.State_Working : UiLanguage.Format(Strings.State_UsingApp, app),
            PresenceState.Slacking => SlackingLabel(snapshot.Category),
            PresenceState.Browsing => Strings.State_Browsing,
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
        ActivityCategory.Video => Strings.Relax_Video,
        ActivityCategory.Music => Strings.Relax_Music,
        ActivityCategory.Shopping => Strings.Relax_Shopping,
        ActivityCategory.Social => Strings.Relax_Social,
        ActivityCategory.News => Strings.Relax_News,
        ActivityCategory.Game => Strings.Relax_Game,
        ActivityCategory.Reading => Strings.Relax_Reading,
        _ => Strings.Relax_Other,
    };

    public static string CategoryName(ActivityCategory category) => category switch
    {
        ActivityCategory.Work or ActivityCategory.Meeting => Strings.Category_Work,
        ActivityCategory.Communication => Strings.Category_Communication,
        ActivityCategory.Video => Strings.Category_Video,
        ActivityCategory.Music => Strings.Category_Music,
        ActivityCategory.Shopping => Strings.Category_Shopping,
        ActivityCategory.Social => Strings.Category_Social,
        ActivityCategory.News => Strings.Category_News,
        ActivityCategory.Game => Strings.Category_Game,
        ActivityCategory.Reading => Strings.Category_Reading,
        ActivityCategory.Other => Strings.Category_Other,
        ActivityCategory.Browsing => Strings.Category_Browsing,
        _ => Strings.Category_None,
    };

    public static string PrivacyLevelName(PrivacyLevel level) => level switch
    {
        PrivacyLevel.StateOnly => Strings.Privacy_StateOnly,
        PrivacyLevel.Category => Strings.Privacy_Category,
        PrivacyLevel.Detail => Strings.Privacy_Detail,
        _ => level.ToString(),
    };

    public static string Duration(DateTimeOffset since, DateTimeOffset now)
    {
        var span = now - since;
        return span < TimeSpan.FromMinutes(1) ? Strings.Duration_JustNow : Duration((long)span.TotalSeconds);
    }

    /// <summary>Whole minutes and hours, e.g. "2 小时 5 分钟". Below a minute it says so.</summary>
    public static string Duration(long seconds) => seconds switch
    {
        <= 0 => UiLanguage.Format(Strings.Duration_Minutes, 0),
        < 60 => Strings.Duration_UnderMinute,
        < 3600 => UiLanguage.Format(Strings.Duration_Minutes, seconds / 60),
        _ => seconds % 3600 / 60 == 0
            ? UiLanguage.Format(Strings.Duration_Hours, seconds / 3600)
            : UiLanguage.Format(Strings.Duration_HoursMinutes, seconds / 3600, seconds % 3600 / 60),
    };

    public static string Days(int days) => UiLanguage.Format(Strings.Duration_Days, days);

    /// <summary>"11 月 13 日", with the year when it is not this year.</summary>
    public static string Date(DateOnly date, DateOnly today) =>
        UiLanguage.Format(date.Year == today.Year ? Strings.Date_MonthDay : Strings.Date_YearMonthDay, date);

    /// <summary>"11 月 13 日 星期五".</summary>
    public static string DateWithWeekday(DateOnly date, DateOnly today) =>
        UiLanguage.Format(Strings.Date_WithWeekday, Date(date, today), Weekday(date.DayOfWeek));

    /// <summary>Short weekday name: "五" or "Fri".</summary>
    public static string Weekday(DayOfWeek day) => Strings.Weekdays.Split(',')[(int)day];

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
