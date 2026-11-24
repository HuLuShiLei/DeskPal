using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Updates;
using DeskPal.Client.Core.Usage;
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

    public static readonly RoomDto OwnedRoom = new(MyRoom, "测试房间", Alice, 20, Now.AddDays(-3),
    [
        new RoomMemberDto(Alice, "Alice", RoomRole.Owner, Now.AddDays(-3)),
        new RoomMemberDto(Bob, "Bob", RoomRole.Member, Now.AddDays(-2), ShareStats: true, StatsDays: 7),
        new RoomMemberDto(Carol, "Carol", RoomRole.Member, Now.AddDays(-1)),
    ]);

    public static readonly RoomDto GuestRoom = new(TeamRoom, "同事房间", Dave, 20, Now.AddDays(-5),
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
        UserName: "alice",
        UpdateActivity: null,
        ShareUsage: true,
        Theme: "system",
        Personal: new PersonalView(AccountSync: false),
        Plugins: new PluginsView(
            [
                new PluginView("user:qq-chat.ps1", "QQ 会话", "0.1", Official: false, Enabled: true,
                    ["detail"], ["detail"], "running", [], [], ["QQ"]),
                new PluginView("user:vscode-title.ps1", "VS Code 标题", "0.2", Official: false, Enabled: true,
                    ["foreground"], ["foreground"], "running", [], [], []),
                new PluginView("user:spotify.py", "spotify.py", null, Official: false, Enabled: false,
                    ["foreground", "domain"], [], "disabled", [], [], []),
                new PluginView("user:broken.cmd", "broken.cmd", null, Official: false, Enabled: true,
                    ["media"], [], "failed", [new PluginNote("plugin.error", "exit code 1", "warn")], [], []),
                new PluginView("official:windows", "Windows", null, Official: true, Enabled: true,
                    ["foreground", "domain", "idle", "lock", "media"], ["domain", "idle", "lock", "media"], "running", [], [], []),
            ],
            [],
            @"C:\Users\alice\AppData\Roaming\DeskPal\plugins"));

    /// <summary>The list on a Linux desktop the built-in plugin cannot read windows on; open with ?mock=no-room&amp;platform=linux.</summary>
    private static readonly PluginsView LinuxPlugins = new(
        [
            new PluginView("user:hyprland.sh", "Hyprland 窗口", "0.3", Official: false, Enabled: true,
                ["foreground"], ["foreground"], "running", [], [], []),
            new PluginView("user:telegram-chat.py", "Telegram 会话", "0.1", Official: false, Enabled: true,
                ["detail"], [], "starting", [], [], ["telegram-desktop", "Telegram"]),
            new PluginView("official:linux", "Linux", null, Official: true, Enabled: true,
                ["domain", "idle", "lock", "media"], ["domain", "idle", "lock", "media"], "running",
                [
                    new PluginNote("linux.noWindowSource", "Hyprland", "info"),
                    new PluginNote("linux.browserMissing", null, "warn"),
                ],
                ["linux.browserExtension"], []),
        ],
        [],
        "/home/alice/.config/DeskPal/plugins");

    /// <summary>Every plugin turned off: nothing supplies anything.</summary>
    private static readonly PluginsView NoPlugins = new(
        [
            new PluginView("official:windows", "Windows", null, Official: true, Enabled: false,
                ["foreground", "domain", "idle", "lock", "media"], [], "disabled", [], [], []),
        ],
        ["foreground", "domain", "idle", "lock", "media"],
        @"C:\Users\alice\AppData\Roaming\DeskPal\plugins");

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
                Client = ClientKinds.Mac,
            },
        };
        var local = new LocalPresence(PresenceState.Working, ActivityCategory.Work, "Cursor", "plan.md - idea1 - Cursor");
        var earlier = new LocalPresence(PresenceState.Other, ActivityCategory.Other, "Cursor", "roadmap.md - idea1 - Cursor");
        return new PanelInput
        {
            MyUserId = Alice,
            DisplayName = "Alice",
            Connection = ConnectionStatus.Connected,
            Rooms = [OwnedRoom, GuestRoom],
            SelectedRoom = OwnedRoom,
            PresenceOf = (room, user) => presence.GetValueOrDefault((room, user)),
            PrivacyOf = _ => new PrivacyProfile { ShareStats = true, StatsDays = 7 },
            Local = local,
            Classification = new ClassificationResult(ActivityCategory.Work, "Cursor", RuleLayer.Builtin),
            Committed = new CommittedPresence(earlier, Now.AddMinutes(-1)),
            PendingDeadline = Now.AddSeconds(7),
            StableFor = TimeSpan.FromSeconds(10),
            CorrectionTarget = "Cursor",
            CorrectionTitle = "plan.md - idea1 - Cursor",
            MyRules =
            [
                new UserRuleDto(RuleKind.AppTitle, "qq/项目组", ActivityCategory.Work, "QQ", Now.AddHours(-1)),
                new UserRuleDto(RuleKind.Process, "wechat", ActivityCategory.Work, "微信", Now.AddDays(-1)),
                new UserRuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Video, "哔哩哔哩", Now.AddHours(-2)),
            ],
            Unclassified =
            [
                new UnclassifiedApp(RuleKind.Process, "xmind", "XMind", TimeSpan.FromMinutes(42)),
                new UnclassifiedApp(RuleKind.Process, "snipaste", "Snipaste", TimeSpan.FromMinutes(7)),
            ],
            Settings = Settings with { IsAdmin = true },
            Pinned = [Bob],
            Now = Now,
            ShowUsage = true,
            SyncAllowed = true,
            NoTitleRules = [new(NoTitleKind.App, "招商银行"), new(NoTitleKind.Domain, "cmbchina.com"), new(NoTitleKind.Title, "病历")],
            StatsViews =
            [
                new StatsViewRecord(MyRoom, "测试房间", Bob, "Bob", Now.AddHours(-2)),
                new StatsViewRecord(TeamRoom, "同事房间", Dave, "Dave", Now.AddDays(-1).AddHours(-3)),
            ],
            Zone = StatsSamples.Zone,
            Client = ClientKinds.Windows,
        };
    }

    public static PanelInput NoRoom()
    {
        var local = new LocalPresence(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "理工生陷入云吸猫 (64)");
        return Default() with
        {
            Rooms = [],
            SelectedRoom = null,
            Local = local,
            Classification = new ClassificationResult(ActivityCategory.Communication, "QQ", RuleLayer.Personal),
            Committed = new CommittedPresence(local, Now.AddMinutes(-3)),
            PendingDeadline = null,
            CorrectionTarget = "QQ",
            CorrectionTitle = "Project team",
            CorrectionKeyword = "project",
            MyRules = [new UserRuleDto(RuleKind.Process, "qq", ActivityCategory.Communication, "QQ", Now.AddDays(-3))],
            Unclassified = [],
            Settings = Settings with { ShareUsage = null, Plugins = LinuxPlugins },
        };
    }

    public static PanelInput Guest()
    {
        var paused = new LocalPresence(PresenceState.Paused, ActivityCategory.None, null, null);
        var offline = new PresenceSnapshot { State = PresenceState.Offline, Since = Now.AddHours(-2) };
        return Default() with
        {
            Connection = ConnectionStatus.Reconnecting,
            SelectedRoom = GuestRoom,
            PresenceOf = (room, user) => room == TeamRoom && user == Dave ? offline : null,
            Local = paused,
            Classification = null,
            Committed = new CommittedPresence(paused, Now.AddMinutes(-5)),
            PendingDeadline = null,
            Paused = true,
            CorrectionTarget = null,
            MyRules = [],
            Unclassified = [],
            Settings = Settings with { AutoStart = false, Topmost = true, ReceiveBeta = false, UpdateReady = true, Version = "0.1.0", ShareUsage = false, Plugins = NoPlugins },
        };
    }

    /// <summary>First start after updating from 0.1.0: the changelog dialog is waiting.</summary>
    public static PanelInput Updated() => Default() with
    {
        Settings = Settings with { Version = "0.1.2-dev.18" },
        WhatsNew = Changelog.WhatsNew(
            [
                new ChangelogSection("0.1.2",
                [
                    new ChangelogItem("更新后第一次打开面板时显示更新内容", "The first time you open the panel after an update, it shows what changed"),
                    new ChangelogItem("面板可以拖动边框调整大小", "The panel can be resized by dragging its edges"),
                    new ChangelogItem("设置里可以修改显示名称，允许重名", "You can change your display name. Duplicate names are allowed"),
                ]),
                new ChangelogSection("0.1.1",
                [
                    new ChangelogItem("点关闭时可以选择最小化，或最小化到系统托盘", "Closing can minimize the window, or minimize it to the notification area"),
                ]),
            ],
            new HashSet<string>(),
            "0.1.0",
            "0.1.2-dev.18"),
    };

    public static IReadOnlyDictionary<string, Func<PanelInput>> All { get; } = new Dictionary<string, Func<PanelInput>>
    {
        ["default"] = Default,
        ["no-room"] = NoRoom,
        ["guest"] = Guest,
        ["updated"] = Updated,
    };
}
