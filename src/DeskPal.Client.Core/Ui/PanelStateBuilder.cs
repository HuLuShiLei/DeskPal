using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Ui;

public enum ConnectionStatus
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
}

/// <summary>Inputs collected from the running services. Kept as plain data so the panel text can be unit tested.</summary>
public sealed record PanelInput
{
    public required Guid MyUserId { get; init; }
    public required string DisplayName { get; init; }
    public required ConnectionStatus Connection { get; init; }
    public required IReadOnlyList<RoomDto> Rooms { get; init; }
    public RoomDto? SelectedRoom { get; init; }
    public required Func<Guid, Guid, PresenceSnapshot?> PresenceOf { get; init; }
    public required Func<Guid, PrivacyProfile> PrivacyOf { get; init; }
    public LocalPresence? Local { get; init; }
    public ClassificationResult? Classification { get; init; }
    public CommittedPresence? Committed { get; init; }

    /// <summary>When the pending local presence will be committed; null when nothing is waiting.</summary>
    public DateTimeOffset? PendingDeadline { get; init; }

    public TimeSpan StableFor { get; init; } = TimeSpan.FromSeconds(10);
    public bool Paused { get; init; }
    public string? CorrectionTarget { get; init; }
    public IReadOnlyList<UserRuleDto> MyRules { get; init; } = [];
    public IReadOnlyList<UnclassifiedApp> Unclassified { get; init; } = [];
    public required SettingsView Settings { get; init; }
    public WhatsNewView? WhatsNew { get; init; }
    public IReadOnlyCollection<Guid> Pinned { get; init; } = [];
    public required DateTimeOffset Now { get; init; }

    /// <summary>Days raw usage segments are kept; null hides the usage section.</summary>
    public int? UsageRetentionDays { get; init; }
    public IReadOnlyList<NoTitleRule> NoTitleRules { get; init; } = [];
    public IReadOnlyList<StatsViewRecord> StatsViews { get; init; } = [];
    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Local;
}

public static class PanelStateBuilder
{
    public static PanelState Build(PanelInput input)
    {
        var room = input.SelectedRoom;
        var members = room is null ? [] : Members(input, room);
        string? emptyHint = room is not null
            ? members.Count == 0 ? Strings.Panel_NoMembers : null
            : Strings.Panel_NoRoom;

        return new PanelState(
            Connection(input.Connection),
            Me(input, room),
            input.Rooms.Select(r => new RoomItem(r.Id, r.Name, r.OwnerId == input.MyUserId, r.Members.Count, r.MaxMembers)).ToList(),
            room?.Id,
            emptyHint,
            members,
            room is null ? null : Privacy(input.PrivacyOf(room.Id)),
            new CorrectionView(
                input.CorrectionTarget,
                input.Classification?.Category ?? ActivityCategory.Other,
                PresenceFormatter.SelectableCategories.Select(c => new Option<ActivityCategory>(c, PresenceFormatter.CategoryName(c))).ToList(),
                input.Classification?.Layer == RuleLayer.Personal),
            input.Settings,
            input.MyRules
                .OrderByDescending(r => r.UpdatedAt)
                .Select(r => new MyRuleItem(r.Kind, r.Pattern, r.Name ?? r.Pattern, KindName(r.Kind), r.Category, PresenceFormatter.CategoryName(r.Category)))
                .ToList(),
            input.Unclassified
                .Select(a => new UnclassifiedItem(a.Kind, a.Pattern, a.DisplayName, UiLanguage.Format(Strings.Panel_UsedToday, PresenceFormatter.Duration(input.Now - a.Today, input.Now))))
                .ToList(),
            input.WhatsNew,
            input.UsageRetentionDays is { } retention ? Usage(input, retention) : null,
            UiLanguage.Current);
    }

    public static IReadOnlyList<int> RetentionOptions { get; } = [30, 90, 180, 365];

    private static string NoTitleLabel(NoTitleRule rule) => UiLanguage.Format(rule.Kind switch
    {
        NoTitleKind.App => Strings.NoTitle_App,
        NoTitleKind.Process => Strings.NoTitle_Process,
        NoTitleKind.Domain => Strings.NoTitle_Domain,
        _ => Strings.NoTitle_Title,
    }, rule.Pattern);

    private static UsageView Usage(PanelInput input, int retention) => new(
        retention,
        RetentionOptions.Select(d => new Option<int>(d, PresenceFormatter.Days(d))).ToList(),
        input.NoTitleRules.Select(r => new NoTitleItem(r.Kind, r.Pattern, NoTitleLabel(r))).ToList(),
        input.StatsViews
            .Select(v => new StatsViewerItem(v.ViewerName, v.RoomName, When(v.At, input.Now, input.Zone)))
            .ToList());

    private static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        var date = DateOnly.FromDateTime(local.Date);
        var day = date == today ? Strings.Day_Today : date == today.AddDays(-1) ? Strings.Day_Yesterday : UiLanguage.Format(Strings.Date_MonthDay, date);
        return $"{day} {local:HH:mm}";
    }

    private static string KindName(RuleKind kind) => kind switch
    {
        RuleKind.Process => Strings.RuleKind_Process,
        RuleKind.Domain => Strings.RuleKind_Domain,
        _ => Strings.RuleKind_TitleKeyword,
    };

    private static ConnectionView Connection(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => new(true, Strings.Connection_Connected),
        ConnectionStatus.Connecting => new(false, Strings.Connection_Connecting),
        ConnectionStatus.Reconnecting => new(false, Strings.Connection_Reconnecting),
        _ => new(false, Strings.Connection_Disconnected),
    };

    private static List<MemberItem> Members(PanelInput input, RoomDto room) => room.Members
        .Where(m => m.UserId != input.MyUserId)
        .Select(m =>
        {
            if (room.Blocked?.Contains(m.UserId) == true)
            {
                return new MemberItem(m.UserId, m.DisplayName, m.Role == RoomRole.Owner, false, new PresenceView(Strings.State_Blocked, null, null), "", Blocked: true);
            }

            var snapshot = input.PresenceOf(room.Id, m.UserId);
            var view = snapshot is null ? new PresenceView(Strings.State_Offline, null, null) : PresenceFormatter.Format(snapshot);
            var meta = snapshot is null || snapshot.State == PresenceState.Offline ? "" : Lasted(snapshot.Since, input.Now);
            return new MemberItem(
                m.UserId,
                m.DisplayName,
                m.Role == RoomRole.Owner,
                snapshot is not null && snapshot.State != PresenceState.Offline,
                view,
                meta,
                input.Pinned.Contains(m.UserId),
                ShareStats: m.ShareStats);
        })
        .ToList();

    private static MeView Me(PanelInput input, RoomDto? room)
    {
        var local = input.Local;
        if (local is null)
        {
            return new MeView(input.DisplayName, new PresenceView(Strings.State_Detecting, null, null), null, null, input.Paused);
        }

        var privacy = room is null ? null : input.PrivacyOf(room.Id);
        PresenceView Shown(LocalPresence presence) => privacy is null
            ? PresenceFormatter.Format(Unfiltered(presence))
            : PresenceFormatter.Format(PrivacyFilter.Apply(new CommittedPresence(presence, input.Now), privacy));

        var localView = Shown(local);
        var current = input.Committed;
        var waiting = current is not null && current.Presence.Key != local.Key;
        var shownPresence = waiting ? current!.Presence : local;
        var view = Shown(shownPresence);
        PendingView? pending = waiting && input.PendingDeadline is { } deadline
            ? new PendingView(
                localView,
                Math.Max(0, (int)(deadline - input.Now).TotalMilliseconds),
                (int)Math.Round(input.StableFor.TotalSeconds),
                Full(local))
            : null;

        var layer = input.Classification?.Layer switch
        {
            RuleLayer.Personal => Strings.Source_Personal,
            RuleLayer.Builtin => Strings.Source_Builtin,
            RuleLayer.Heuristic => Strings.Source_Heuristic,
            RuleLayer.Browser => null,
            _ => Strings.Source_Unknown,
        };
        string? source = local.Category == ActivityCategory.None || layer is null
            ? null
            : input.CorrectionTarget is { } target ? UiLanguage.Format(Strings.Source_WithTarget, target, layer) : UiLanguage.Format(Strings.Source_Plain, layer);

        string? visible = null;
        if (room is not null && input.Committed is { } committed)
        {
            var filtered = PrivacyFilter.Apply(committed, input.PrivacyOf(room.Id));
            visible = PresenceFormatter.OneLine(PresenceFormatter.Format(filtered));
        }

        return new MeView(
            input.DisplayName,
            view,
            source,
            visible,
            input.Paused,
            pending,
            current is null ? null : Lasted(current.Since, input.Now),
            Full(shownPresence));
    }

    /// <summary>My own status without the privacy filter. Never sent; the panel shows it only on hover.</summary>
    private static string? Full(LocalPresence presence)
    {
        if (presence.Category == ActivityCategory.None)
        {
            return null;
        }

        var lines = new List<string> { UiLanguage.Format(Strings.Full_Category, PresenceFormatter.CategoryName(presence.Category)) };
        if (!string.IsNullOrWhiteSpace(presence.AppDisplayName))
        {
            lines.Add(UiLanguage.Format(presence.Category == ActivityCategory.Browsing ? Strings.Full_Site : Strings.Full_App, AppNames.Localize(presence.AppDisplayName)));
        }

        if (!string.IsNullOrWhiteSpace(presence.Detail)
            && !string.Equals(presence.Detail.Trim(), presence.AppDisplayName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(UiLanguage.Format(Strings.Full_Title, presence.Detail));
        }

        return string.Join("\n", lines);
    }

    private static PresenceSnapshot Unfiltered(LocalPresence local) => new()
    {
        State = local.State,
        Category = local.Category,
        AppDisplayName = local.AppDisplayName,
        Detail = local.Detail,
    };

    private static string Lasted(DateTimeOffset since, DateTimeOffset now) =>
        now - since < TimeSpan.FromMinutes(1) ? Strings.Lasted_JustStarted : UiLanguage.Format(Strings.Lasted_For, PresenceFormatter.Duration(since, now));

    private static PrivacyView Privacy(PrivacyProfile profile) => new(
        PresenceFormatter.SelectableCategories
            .Select(c => new PrivacyRow(c, PresenceFormatter.CategoryName(c), profile.LevelFor(c)))
            .ToList(),
        Enum.GetValues<PrivacyLevel>().Select(l => new Option<PrivacyLevel>(l, PresenceFormatter.PrivacyLevelName(l))).ToList(),
        profile.ShareStats,
        profile.StatsDays,
        PrivacyProfile.StatsDayOptions.Select(d => new Option<int>(d, PresenceFormatter.Days(d))).ToList());
}
