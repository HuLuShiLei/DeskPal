using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Ui;

/// <summary>
/// Everything the HTML panel shows. Pushed to the page as one JSON message; the page only lays it out.
/// Field names are mirrored in web/src/bridge.ts.
/// </summary>
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
    IReadOnlyList<UnclassifiedItem> Unclassified);

public sealed record ConnectionView(bool Connected, string Text);

public sealed record MeView(
    string DisplayName,
    PresenceView View,
    string? Source,
    string? Visible,
    bool Paused,
    string? ManualText,
    IReadOnlyList<string> ManualPresets);

public sealed record RoomItem(Guid Id, string Name, bool IsOwner, int MemberCount, int MaxMembers);

public sealed record MemberItem(Guid UserId, string Name, bool IsOwner, bool Online, PresenceView View, string Meta);

public sealed record Option<T>(T Value, string Name);

public sealed record PrivacyRow(ActivityCategory Category, string Name, PrivacyLevel Level);

public sealed record PrivacyView(IReadOnlyList<PrivacyRow> Rows, IReadOnlyList<Option<PrivacyLevel>> Levels);

/// <param name="Target">App or site the correction applies to; null when nothing is recognized yet.</param>
/// <param name="Personal">The current result comes from my own rule, so it can be reset to the default.</param>
public sealed record CorrectionView(string? Target, ActivityCategory Current, IReadOnlyList<Option<ActivityCategory>> Options, bool Personal);

/// <summary>One of my personal rules, listed in the settings.</summary>
public sealed record MyRuleItem(RuleKind Kind, string Pattern, string Name, string KindName, ActivityCategory Category, string CategoryName);

/// <summary>An app used today that no rule recognized. Shown on the status page so the user can classify it.</summary>
public sealed record UnclassifiedItem(RuleKind Kind, string Pattern, string Name, string Today);

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
    string Theme = "system");
