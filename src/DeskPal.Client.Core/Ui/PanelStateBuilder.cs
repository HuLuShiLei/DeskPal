using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
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
}

public static class PanelStateBuilder
{
    public static PanelState Build(PanelInput input)
    {
        var room = input.SelectedRoom;
        var members = room is null ? [] : Members(input, room);
        string? emptyHint = room is not null
            ? members.Count == 0 ? "房间里还没有其他成员。到「房间」页生成邀请码，发给对方。" : null
            : "当前无房间。请创建/选择一个房间，或用邀请码加入。";

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
                .Select(a => new UnclassifiedItem(a.Kind, a.Pattern, a.DisplayName, $"今天 {PresenceFormatter.Duration(input.Now - a.Today, input.Now)}"))
                .ToList(),
            input.WhatsNew);
    }

    private static string KindName(RuleKind kind) => kind switch
    {
        RuleKind.Process => "应用",
        RuleKind.Domain => "网站",
        _ => "标题关键词",
    };

    private static ConnectionView Connection(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => new(true, "已连接"),
        ConnectionStatus.Connecting => new(false, "正在连接服务器…"),
        ConnectionStatus.Reconnecting => new(false, "连接中断，正在重连…"),
        _ => new(false, "未连接服务器，稍后自动重试"),
    };

    private static List<MemberItem> Members(PanelInput input, RoomDto room) => room.Members
        .Where(m => m.UserId != input.MyUserId)
        .Select(m =>
        {
            var snapshot = input.PresenceOf(room.Id, m.UserId);
            var view = snapshot is null ? new PresenceView("离线", null, null) : PresenceFormatter.Format(snapshot);
            var meta = snapshot is null || snapshot.State == PresenceState.Offline ? "" : Lasted(snapshot.Since, input.Now);
            return new MemberItem(
                m.UserId,
                m.DisplayName,
                m.Role == RoomRole.Owner,
                snapshot is not null && snapshot.State != PresenceState.Offline,
                view,
                meta,
                input.Pinned.Contains(m.UserId));
        })
        .ToList();

    private static MeView Me(PanelInput input, RoomDto? room)
    {
        var local = input.Local;
        if (local is null)
        {
            return new MeView(input.DisplayName, new PresenceView("正在识别…", null, null), null, null, input.Paused);
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
            RuleLayer.Personal => "按你设置的分类识别",
            RuleLayer.Builtin => "按默认分类识别",
            RuleLayer.Heuristic => "按窗口特征推测",
            RuleLayer.Browser => null,
            _ => "没有识别出类别，显示为正在使用",
        };
        string? source = local.Category == ActivityCategory.None || layer is null
            ? null
            : input.CorrectionTarget is { } target ? $"「{target}」{layer}。" : $"{layer}。";

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

        var lines = new List<string> { $"类别：{PresenceFormatter.CategoryName(presence.Category)}" };
        if (!string.IsNullOrWhiteSpace(presence.AppDisplayName))
        {
            lines.Add($"{(presence.Category == ActivityCategory.Browsing ? "网站" : "应用")}：{presence.AppDisplayName}");
        }

        if (!string.IsNullOrWhiteSpace(presence.Detail)
            && !string.Equals(presence.Detail.Trim(), presence.AppDisplayName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"标题：{presence.Detail}");
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
        now - since < TimeSpan.FromMinutes(1) ? "刚刚开始" : $"已持续 {PresenceFormatter.Duration(since, now)}";

    private static PrivacyView Privacy(PrivacyProfile profile) => new(
        PresenceFormatter.SelectableCategories
            .Select(c => new PrivacyRow(c, PresenceFormatter.CategoryName(c), profile.LevelFor(c)))
            .ToList(),
        Enum.GetValues<PrivacyLevel>().Select(l => new Option<PrivacyLevel>(l, PresenceFormatter.PrivacyLevelName(l))).ToList());
}
