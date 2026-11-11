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
    public bool Paused { get; init; }
    public string? ManualText { get; init; }
    public string? CorrectionTarget { get; init; }
    public required SettingsView Settings { get; init; }
    public required DateTimeOffset Now { get; init; }
}

public static class PanelStateBuilder
{
    public static IReadOnlyList<string> ManualPresets { get; } = ["别打扰", "午休中", "开会中", "有空，来聊", "在路上"];

    public static PanelState Build(PanelInput input)
    {
        var room = input.SelectedRoom;
        var members = room is null ? [] : Members(input, room);
        string? emptyHint = room is null
            ? "还没有 Room。到「Room」页创建 Room，或者用邀请码加入。"
            : members.Count == 0 ? "Room 里还没有其他成员。到「Room」页生成邀请码，发给对方。" : null;

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
                PresenceFormatter.SelectableCategories.Select(c => new Option<ActivityCategory>(c, PresenceFormatter.CategoryName(c))).ToList()),
            input.Settings);
    }

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
            var meta = snapshot is null
                ? "还没有收到状态"
                : snapshot.State == PresenceState.Offline ? "" : $"已持续 {PresenceFormatter.Duration(snapshot.Since, input.Now)}";
            return new MemberItem(
                m.UserId,
                m.DisplayName,
                m.Role == RoomRole.Owner,
                snapshot is not null && snapshot.State != PresenceState.Offline,
                view,
                meta);
        })
        .ToList();

    private static MeView Me(PanelInput input, RoomDto? room)
    {
        var local = input.Local;
        if (local is null)
        {
            return new MeView(input.DisplayName, new PresenceView("正在识别…", null, null), null, null, input.Paused, input.ManualText, ManualPresets);
        }

        var full = new PresenceSnapshot
        {
            State = local.State,
            Category = local.Category,
            AppDisplayName = local.AppDisplayName,
            Detail = local.Detail,
            CustomText = local.CustomText,
        };
        var layer = input.Classification?.Layer switch
        {
            RuleLayer.Personal => "个人规则",
            RuleLayer.Room => "Room 规则",
            RuleLayer.Builtin => "内置规则",
            RuleLayer.Heuristic => "推测",
            _ => "未识别",
        };
        var pending = input.Committed is { } c && c.Presence.Key != local.Key ? "稳定后同步" : null;
        var source = string.Join("，", new[] { local.Category == ActivityCategory.None ? null : layer, pending }.Where(p => p is not null));

        string? visible = null;
        if (room is not null && input.Committed is { } committed)
        {
            var filtered = PrivacyFilter.Apply(committed, input.PrivacyOf(room.Id));
            visible = $"该 Room 成员看到：{PresenceFormatter.OneLine(PresenceFormatter.Format(filtered))}";
        }

        return new MeView(
            input.DisplayName,
            PresenceFormatter.Format(full),
            source.Length == 0 ? null : source,
            visible,
            input.Paused,
            input.ManualText,
            ManualPresets);
    }

    private static PrivacyView Privacy(PrivacyProfile profile) => new(
        PresenceFormatter.SelectableCategories
            .Select(c => new PrivacyRow(c, PresenceFormatter.CategoryName(c), profile.LevelFor(c)))
            .ToList(),
        Enum.GetValues<PrivacyLevel>().Select(l => new Option<PrivacyLevel>(l, PresenceFormatter.PrivacyLevelName(l))).ToList());
}