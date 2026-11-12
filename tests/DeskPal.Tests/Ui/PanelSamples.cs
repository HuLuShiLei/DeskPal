using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Ui;

/// <summary>Fixed inputs for the panel. Also written to web/src/mock/fixtures so the browser mock shows real C# output.</summary>
public static class PanelSamples
{
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 7, 0, 0, TimeSpan.Zero);
    public static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000a11c");
    public static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-000000000b0b");
    public static readonly Guid Carol = Guid.Parse("00000000-0000-0000-0000-0000000ca201");
    public static readonly Guid Dave = Guid.Parse("00000000-0000-0000-0000-00000000da7e");
    public static readonly Guid MyRoom = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid TeamRoom = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    public const string LongTitle = "【4K】猫猫合集：一只橘猫从早到晚的完整一天，附带三只小猫的成长记录和彩蛋";

    public static readonly RoomDto OwnedRoom = new(MyRoom, "测试房间", Alice, 50, Now.AddDays(-3),
    [
        new RoomMemberDto(Alice, "Alice", RoomRole.Owner, Now.AddDays(-3)),
        new RoomMemberDto(Bob, "Bob", RoomRole.Member, Now.AddDays(-2)),
        new RoomMemberDto(Carol, "Carol", RoomRole.Member, Now.AddDays(-1)),
    ]);

    public static readonly RoomDto GuestRoom = new(TeamRoom, "同事房间", Dave, 50, Now.AddDays(-5),
    [
        new RoomMemberDto(Dave, "Dave", RoomRole.Owner, Now.AddDays(-5)),
        new RoomMemberDto(Alice, "Alice", RoomRole.Member, Now.AddDays(-4)),
    ]);

    private static readonly SettingsView Settings = new(
        AutoStart: true,
        Topmost: false,
        ReceiveBeta: true,
        UpdateReady: false,
        Version: "0.1.1-dev.12",
        ServerUrl: "https://deskpal.hafuhafu.cn:36443",
        UserName: "alice",
        UpdateActivity: null,
        ShareUsage: true);

    public static PanelInput Default()
    {
        var presence = new Dictionary<(Guid, Guid), PresenceSnapshot>
        {
            [(MyRoom, Bob)] = new()
            {
                State = PresenceState.Slacking,
                Category = ActivityCategory.Video,
                AppDisplayName = "哔哩哔哩",
                Detail = LongTitle,
                Since = Now.AddMinutes(-12),
            },
        };
        var local = new LocalPresence(PresenceState.Working, ActivityCategory.Work, "Cursor", "plan.md - idea1 - Cursor", null);
        var earlier = new LocalPresence(PresenceState.Other, ActivityCategory.Other, "Cursor", null, null);
        return new PanelInput
        {
            MyUserId = Alice,
            DisplayName = "Alice",
            Connection = ConnectionStatus.Connected,
            Rooms = [OwnedRoom, GuestRoom],
            SelectedRoom = OwnedRoom,
            PresenceOf = (room, user) => presence.GetValueOrDefault((room, user)),
            PrivacyOf = _ => PrivacyProfile.CreateDefault(),
            Local = local,
            Classification = new ClassificationResult(ActivityCategory.Work, "Cursor", RuleLayer.Builtin),
            Committed = new CommittedPresence(earlier, Now.AddMinutes(-1)),
            CorrectionTarget = "Cursor",
            MyRules =
            [
                new UserRuleDto(RuleKind.Process, "wechat", ActivityCategory.Work, "微信", Now.AddDays(-1)),
                new UserRuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Video, "哔哩哔哩", Now.AddHours(-2)),
            ],
            Unclassified =
            [
                new UnclassifiedApp(RuleKind.Process, "xmind", "XMind", TimeSpan.FromMinutes(42)),
                new UnclassifiedApp(RuleKind.Process, "snipaste", "Snipaste", TimeSpan.FromMinutes(7)),
            ],
            Settings = Settings,
            Now = Now,
        };
    }

    public static PanelInput NoRoom()
    {
        var local = new LocalPresence(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "理工生陷入云吸猫 (64)", null);
        return Default() with
        {
            Rooms = [],
            SelectedRoom = null,
            Local = local,
            Classification = new ClassificationResult(ActivityCategory.Communication, "QQ", RuleLayer.Personal),
            Committed = new CommittedPresence(local, Now.AddMinutes(-3)),
            CorrectionTarget = "QQ",
            MyRules = [new UserRuleDto(RuleKind.Process, "qq", ActivityCategory.Communication, "QQ", Now.AddDays(-3))],
            Unclassified = [],
            Settings = Settings with { ShareUsage = null },
        };
    }

    public static PanelInput Guest()
    {
        var paused = new LocalPresence(PresenceState.Paused, ActivityCategory.None, null, null, null);
        var offline = new PresenceSnapshot { State = PresenceState.Offline, Since = Now.AddHours(-2) };
        return Default() with
        {
            Connection = ConnectionStatus.Reconnecting,
            SelectedRoom = GuestRoom,
            PresenceOf = (room, user) => room == TeamRoom && user == Dave ? offline : null,
            Local = paused,
            Classification = null,
            Committed = new CommittedPresence(paused, Now.AddMinutes(-5)),
            Paused = true,
            ManualText = "午休中",
            CorrectionTarget = null,
            MyRules = [],
            Unclassified = [],
            Settings = Settings with { AutoStart = false, Topmost = true, ReceiveBeta = false, UpdateReady = true, Version = "0.1.0", ShareUsage = false },
        };
    }

    public static IReadOnlyDictionary<string, Func<PanelInput>> All { get; } = new Dictionary<string, Func<PanelInput>>
    {
        ["default"] = Default,
        ["no-room"] = NoRoom,
        ["guest"] = Guest,
    };
}
