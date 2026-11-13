using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Ui;

/// <summary>
/// Everything the HTML panel shows. Pushed to the page as one JSON message; the page only lays it out.
/// Field names are mirrored in web/src/bridge.ts.
/// </summary>
/// <param name="Locale">zh-CN or en: the language of the text in this state. The page switches its own text to match.</param>
public sealed record PanelState(
    ConnectionView Connection,
    MeView Me,
    IReadOnlyList<RoomItem> Rooms,
    Guid? SelectedRoomId,
    string? EmptyHint,
    IReadOnlyList<MemberItem> Members,
    PrivacyView? Privacy,
    CorrectionView Correction,
    SettingsView Settings,
    IReadOnlyList<MyRuleItem> MyRules,
    IReadOnlyList<UnclassifiedItem> Unclassified,
    WhatsNewView? WhatsNew = null,
    UsageView? Usage = null,
    string Locale = UiLanguage.Chinese);

public sealed record ConnectionView(bool Connected, string Text);

/// <param name="View">What the room currently gets (the committed presence, filtered by my privacy in the selected room); the local presence before the first commit. Unfiltered when there is no room.</param>
/// <param name="Source">How the current app was recognized. Shown on hover.</param>
/// <param name="Pending">A newer local presence waiting to become stable, filtered the same way; null when nothing is waiting.</param>
/// <param name="Meta">How long the current status has lasted.</param>
/// <param name="Full">My own category, app and title without the privacy filter. Shown only to me on hover; null when there is no category.</param>
public sealed record MeView(
    string DisplayName,
    PresenceView View,
    string? Source,
    string? Visible,
    bool Paused,
    PendingView? Pending = null,
    string? Meta = null,
    string? Full = null);

/// <param name="RemainingMs">Time left when the state was built. The page counts down from here.</param>
/// <param name="TotalSeconds">The full stable interval, for the progress bar.</param>
public sealed record PendingView(PresenceView View, int RemainingMs, int TotalSeconds, string? Full = null);

public sealed record RoomItem(Guid Id, string Name, bool IsOwner, int MemberCount, int MaxMembers);

/// <param name="Pinned">Shown as a card on the desktop.</param>
/// <param name="Blocked">I blocked this member: neither of us sees the other's presence. The member is not told.</param>
/// <param name="ShareStats">The member shares usage stats in this room and is not blocked, so "查看统计" is shown.</param>
public sealed record MemberItem(Guid UserId, string Name, bool IsOwner, bool Online, PresenceView View, string Meta, bool Pinned = false, bool Blocked = false, bool ShareStats = false);

public sealed record Option<T>(T Value, string Name);

public sealed record PrivacyRow(ActivityCategory Category, string Name, PrivacyLevel Level);

/// <param name="ShareStats">Room members may view my usage stats, at the detail of the rows.</param>
/// <param name="StatsDays">How many recent days they may view.</param>
public sealed record PrivacyView(
    IReadOnlyList<PrivacyRow> Rows,
    IReadOnlyList<Option<PrivacyLevel>> Levels,
    bool ShareStats = false,
    int StatsDays = PrivacyProfile.DefaultStatsDays,
    IReadOnlyList<Option<int>>? DayOptions = null);

/// <summary>Settings of my own usage stats, shown on the stats page.</summary>
/// <param name="NoTitleRules">Apps, processes, sites and title keywords whose titles are never recorded.</param>
/// <param name="Viewers">Who viewed my stats in the last 7 days, latest first.</param>
public sealed record UsageView(int RetentionDays, IReadOnlyList<Option<int>> RetentionOptions, IReadOnlyList<NoTitleItem> NoTitleRules, IReadOnlyList<StatsViewerItem> Viewers);

public sealed record NoTitleItem(NoTitleKind Kind, string Pattern, string Label);

public sealed record StatsViewerItem(string Name, string Room, string When);

/// <param name="Target">App or site the correction applies to; null when nothing is recognized yet.</param>
/// <param name="Personal">The current result comes from my own rule, so it can be reset to the default.</param>
public sealed record CorrectionView(string? Target, ActivityCategory Current, IReadOnlyList<Option<ActivityCategory>> Options, bool Personal);

/// <summary>One of my personal rules, listed in the settings.</summary>
public sealed record MyRuleItem(RuleKind Kind, string Pattern, string Name, string KindName, ActivityCategory Category, string CategoryName);

/// <summary>An app used today that no rule recognized. Shown on the status page so the user can classify it.</summary>
public sealed record UnclassifiedItem(RuleKind Kind, string Pattern, string Name, string Today);

/// <summary>Shown once after an update: the changelog items this computer has not seen yet.</summary>
public sealed record WhatsNewView(string Title, string Summary, IReadOnlyList<WhatsNewSection> Sections);

public sealed record WhatsNewSection(string Version, IReadOnlyList<string> Items);

public sealed record SettingsView(
    bool AutoStart,
    bool Topmost,
    bool ReceiveBeta,
    bool UpdateReady,
    string Version,
    string ServerUrl,
    string UserName,
    string? UpdateActivity,
    bool? ShareUsage = null,
    string Theme = "system",
    string CloseAction = "ask",
    string Language = UiLanguage.FollowSystem);
