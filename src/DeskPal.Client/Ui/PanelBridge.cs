using System.IO;
using System.Text.Json;
using System.Windows;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Updates;
using DeskPal.Client.Core.Usage;
using DeskPal.Client.Services;
using DeskPal.Client.Views;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.SignalR.Client;

namespace DeskPal.Client.Ui;

/// <summary>Builds the panel state from the running services and carries out the page's commands.</summary>
public sealed class PanelBridge
{
    private readonly RoomState _rooms;
    private readonly PresenceCoordinator _presence;
    private readonly HubService _hub;
    private readonly ApiClient _api;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly UpdateService _updates;
    private readonly PersonalRules _personalRules;
    private readonly UnclassifiedUsage _unclassified;
    private readonly WhatsNewStore _whatsNew;
    private readonly PinnedMembers _pins;
    private readonly UsageService _usage;
    private readonly StatsExchange _stats;

    public PanelBridge(
        RoomState rooms,
        PresenceCoordinator presence,
        HubService hub,
        ApiClient api,
        AppSettings settings,
        SettingsStore store,
        UpdateService updates,
        PersonalRules personalRules,
        UnclassifiedUsage unclassified,
        WhatsNewStore whatsNew,
        PinnedMembers pins,
        UsageService usage,
        StatsExchange stats)
    {
        _usage = usage;
        _stats = stats;
        usage.Changed += RaiseChanged;
        _rooms = rooms;
        _presence = presence;
        _hub = hub;
        _api = api;
        _settings = settings;
        _store = store;
        _updates = updates;
        _personalRules = personalRules;
        _unclassified = unclassified;
        _whatsNew = whatsNew;
        _pins = pins;
        pins.Changed += RaiseChanged;
        personalRules.Changed += RaiseChanged;
        rooms.Changed += RaiseChanged;
        presence.LocalChanged += RaiseChanged;
        hub.StateChanged += _ => RaiseChanged();
        updates.Changed += RaiseChanged;
    }

    /// <summary>Raised from any thread when the state may have changed.</summary>
    public event Action? Changed;

    public event Action<bool>? TopmostChanged;

    public event Action<string>? ThemeChanged;

    /// <summary>Where update confirmations are shown. Set by the panel window.</summary>
    public IUserPrompt Prompt { get; set; } = MessageBoxPrompt.Instance;

    /// <summary>由 App 赋值。清除登录状态并重新启动，回到登录页。</summary>
    public Func<Task>? Logout { get; set; }

    public PanelState BuildState() => PanelStateBuilder.Build(new PanelInput
    {
        MyUserId = _rooms.MyUserId,
        DisplayName = _settings.DisplayName ?? _settings.UserName ?? "",
        Connection = _hub.State switch
        {
            HubConnectionState.Connected => ConnectionStatus.Connected,
            HubConnectionState.Connecting => ConnectionStatus.Connecting,
            HubConnectionState.Reconnecting => ConnectionStatus.Reconnecting,
            _ => ConnectionStatus.Disconnected,
        },
        Rooms = _rooms.Rooms,
        SelectedRoom = _rooms.SelectedRoom,
        PresenceOf = _rooms.GetPresence,
        PrivacyOf = _rooms.GetPrivacy,
        Local = _presence.Local,
        Classification = _presence.Classification,
        Committed = _presence.Committed,
        PendingDeadline = _presence.PendingDeadline,
        StableFor = _presence.StableFor,
        Paused = _presence.Paused,
        CorrectionTarget = CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName)?.DisplayName,
        MyRules = _personalRules.Rules,
        Unclassified = _unclassified.Top(TimeSpan.FromMinutes(5), 5),
        Settings = new SettingsView(
            AutoStart.IsEnabled(),
            _settings.Topmost,
            _updates.ReceiveBeta,
            _updates.UpdateReady,
            _updates.VersionLabel,
            _settings.ServerUrl,
            _settings.UserName ?? "",
            _updates.ActivityText,
            _settings.ShareUsage,
            NormalizeTheme(_settings.Theme),
            NormalizeCloseAction(_settings.CloseAction)),
        WhatsNew = _whatsNew.Pending,
        Pinned = _pins.Users,
        Now = DateTimeOffset.UtcNow,
        UsageRetentionDays = _settings.UsageRetentionDays,
        NoTitleRules = _settings.NoTitleRules,
        StatsViews = _usage.RecentViews,
    });

    /// <summary>Runs on the UI thread.</summary>
    public async Task<CommandResult> HandleAsync(WebCommand command)
    {
        try
        {
            var result = await DispatchAsync(command);
            RaiseChanged();
            return result;
        }
        catch (ApiException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
        catch (FormatException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private async Task<CommandResult> DispatchAsync(WebCommand c)
    {
        switch (c.Type)
        {
            case "room.select":
                _rooms.Select(c.OptionalString("roomId") is null ? null : c.Guid("roomId"));
                return CommandResult.Success;

            case "room.create":
            {
                var name = c.String("name").Trim();
                if (name.Length == 0)
                {
                    return CommandResult.Fail("请输入房间名称");
                }

                var room = await _api.CreateRoomAsync(name);
                _rooms.Select(room.Id);
                await _rooms.RefreshAsync();
                return CommandResult.Success;
            }

            case "room.join":
            {
                var code = c.String("code").Trim();
                if (code.Length == 0)
                {
                    return CommandResult.Fail("请输入邀请码");
                }

                var room = await _api.JoinRoomAsync(code);
                _rooms.Select(room.Id);
                await _rooms.RefreshAsync();
                return CommandResult.Success;
            }

            case "room.rename":
            {
                var roomId = c.Guid("roomId");
                var name = c.String("name").Trim();
                if (_rooms.Rooms.FirstOrDefault(r => r.Id == roomId)?.Name == name)
                {
                    return CommandResult.Success;
                }

                await _api.RenameRoomAsync(roomId, name);
                await _rooms.RefreshAsync();
                return CommandResult.Success;
            }

            case "room.invite":
            {
                var invite = await _api.CreateInviteAsync(c.Guid("roomId"));
                Clipboard.SetText(invite.Code);
                return new CommandResult(true, Data: new
                {
                    code = invite.Code,
                    message = $"邀请码已复制到剪贴板，{invite.ExpiresAt.LocalDateTime:M 月 d 日 HH:mm} 前有效。重新生成后旧邀请码失效，被踢出的人可以用新邀请码加入。",
                });
            }

            case "room.privacy":
            {
                var profile = new PrivacyProfile();
                if (c.Args.TryGetProperty("levels", out var levels) && levels.ValueKind == JsonValueKind.Object)
                {
                    foreach (var item in levels.EnumerateObject())
                    {
                        if (Enum.TryParse<ActivityCategory>(item.Name, out var category) &&
                            Enum.TryParse<PrivacyLevel>(item.Value.GetString(), out var level))
                        {
                            profile.Levels[category] = level;
                        }
                    }
                }

                await _rooms.SavePrivacyAsync(c.Guid("roomId"), profile);
                return CommandResult.Success;
            }

            case "room.shareStats":
            {
                var days = c.Args.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : PrivacyProfile.DefaultStatsDays;
                if (!PrivacyProfile.StatsDayOptions.Contains(days))
                {
                    return CommandResult.Fail("可查看天数只能是 1、3、7 或 30 天");
                }

                await _rooms.SaveStatsSharingAsync(c.Guid("roomId"), c.Bool("enabled"), days);
                return CommandResult.Success;
            }

            case "stats.query":
                return await QueryStatsAsync(c);

            case "stats.export":
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出使用统计",
                    FileName = $"DeskPal-使用统计-{DateTime.Now:yyyyMMdd}.json",
                    Filter = "使用统计 (*.json)|*.json",
                };
                if (dialog.ShowDialog() != true)
                {
                    return CommandResult.Success;
                }

                await Task.Run(() => _usage.Export(dialog.FileName));
                return new CommandResult(true, Data: new { message = $"已导出到 {dialog.FileName}" });
            }

            case "stats.import":
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "导入使用统计", Filter = "使用统计 (*.json)|*.json" };
                if (dialog.ShowDialog() != true)
                {
                    return CommandResult.Success;
                }

                try
                {
                    var result = await Task.Run(() => _usage.Import(dialog.FileName));
                    var message = result.Segments + result.Rollups == 0
                        ? "没有新的记录，这些数据已经导入过。"
                        : $"已导入 {result.Segments} 段记录、{result.Rollups} 条按天汇总。";
                    return new CommandResult(true, Data: new { message });
                }
                catch (InvalidDataException ex)
                {
                    return CommandResult.Fail(ex.Message);
                }
            }

            case "stats.clear":
                await Task.Run(_usage.Clear);
                return CommandResult.Success;

            case "stats.retention":
            {
                var days = c.Args.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0;
                if (!PanelStateBuilder.RetentionOptions.Contains(days))
                {
                    return CommandResult.Fail("保留天数不正确");
                }

                await Task.Run(() => _usage.SetRetention(days));
                return CommandResult.Success;
            }

            case "stats.noTitle.add":
            {
                if (!Enum.TryParse<NoTitleKind>(c.String("kind"), out var kind) || !Enum.IsDefined(kind))
                {
                    return CommandResult.Fail("类型不正确");
                }

                if (NoTitleRule.Create(kind, c.String("pattern")) is not { } rule)
                {
                    return CommandResult.Fail(kind == NoTitleKind.Domain ? "网址不正确，例如 zhihu.com" : $"请输入 1 到 {NoTitleRule.MaxLength} 个字符");
                }

                if (!_settings.NoTitleRules.Any(r => r.SameAs(rule)))
                {
                    _usage.SetNoTitleRules([.. _settings.NoTitleRules, rule]);
                }

                return CommandResult.Success;
            }

            case "stats.noTitle.remove":
            {
                if (Enum.TryParse<NoTitleKind>(c.String("kind"), out var kind))
                {
                    var rule = new NoTitleRule(kind, c.String("pattern"));
                    _usage.SetNoTitleRules(_settings.NoTitleRules.Where(r => !r.SameAs(rule)).ToList());
                }
                return CommandResult.Success;
            }

            case "room.transfer":
                await _api.TransferOwnerAsync(c.Guid("roomId"), c.Guid("userId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "room.leave":
                await _api.LeaveRoomAsync(c.Guid("roomId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "room.kick":
                await _api.KickMemberAsync(c.Guid("roomId"), c.Guid("userId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "room.dissolve":
                await _api.DissolveRoomAsync(c.Guid("roomId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "member.pin":
            {
                var userId = c.Guid("userId");
                if (_rooms.SelectedRoom is not { } room || room.Members.All(m => m.UserId != userId))
                {
                    return CommandResult.Fail("该成员不在当前房间");
                }

                if (room.Blocked?.Contains(userId) == true)
                {
                    return CommandResult.Fail("已屏蔽该成员，取消屏蔽后才能置顶");
                }

                _pins.Pin(room.Id, userId);
                return CommandResult.Success;
            }

            case "member.block":
                await _api.BlockMemberAsync(c.Guid("roomId"), c.Guid("userId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "member.unblock":
                await _api.UnblockMemberAsync(c.Guid("roomId"), c.Guid("userId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "member.unpin":
                _pins.Unpin(c.Guid("userId"));
                return CommandResult.Success;

            case "presence.pause":
                _presence.SetPaused(c.Bool("paused"));
                return CommandResult.Success;

            case "presence.correct":
                return await CorrectAsync(c);

            case "presence.resetCorrection":
            {
                if (CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName) is not { } target)
                {
                    return CommandResult.Fail("还没有识别到当前使用的应用。");
                }

                await _personalRules.DeleteAsync(target.Kind, target.Pattern);
                return CommandResult.Success;
            }

            case "rules.set":
            {
                if (!Enum.TryParse<RuleKind>(c.String("kind"), out var kind) || !Enum.TryParse<ActivityCategory>(c.String("category"), out var category))
                {
                    return CommandResult.Fail("请选择类别");
                }

                await _personalRules.SetAsync(kind, c.String("pattern"), category, c.OptionalString("name"));
                return CommandResult.Success;
            }

            case "rules.delete":
            {
                if (!Enum.TryParse<RuleKind>(c.String("kind"), out var kind))
                {
                    return CommandResult.Fail("规则类型不正确");
                }

                await _personalRules.DeleteAsync(kind, c.String("pattern"));
                return CommandResult.Success;
            }

            case "me.rename":
            {
                var name = c.String("name").Trim();
                if (name.Length is < 1 or > 20)
                {
                    return CommandResult.Fail("显示名称需要 1 到 20 个字符");
                }

                if (name != _settings.DisplayName)
                {
                    var me = await _api.UpdateDisplayNameAsync(name);
                    _settings.DisplayName = me.DisplayName;
                    _store.Save(_settings);
                    await _rooms.RefreshAsync();
                }

                return CommandResult.Success;
            }

            case "settings.shareUsage":
            {
                var enabled = c.Bool("enabled");
                await _api.SetSharingAsync(enabled);
                _settings.ShareUsage = enabled;
                _store.Save(_settings);
                return CommandResult.Success;
            }

            case "settings.autoStart":
                AutoStart.Set(c.Bool("enabled"));
                return CommandResult.Success;

            case "settings.topmost":
                _settings.Topmost = c.Bool("enabled");
                _store.Save(_settings);
                TopmostChanged?.Invoke(_settings.Topmost);
                return CommandResult.Success;

            case "settings.closeAction":
            {
                var action = NormalizeCloseAction(c.String("action"));
                if (action != c.String("action"))
                {
                    return CommandResult.Fail("关闭选项不正确");
                }

                _settings.CloseAction = action;
                _store.Save(_settings);
                RaiseChanged();
                return CommandResult.Success;
            }

            case "settings.theme":
            {
                var theme = NormalizeTheme(c.String("theme"));
                if (theme != c.String("theme"))
                {
                    return CommandResult.Fail("外观选项不正确");
                }

                _settings.Theme = theme;
                _store.Save(_settings);
                ThemeChanged?.Invoke(theme);
                return CommandResult.Success;
            }

            case "update.check":
                await UpdateInteraction.CheckAsync(_updates, _updates.Exit!, quietWhenCurrent: false, Prompt);
                return CommandResult.Success;

            case "update.beta":
            {
                var enabled = c.Bool("enabled");
                if (_updates.ReceiveBeta != enabled)
                {
                    _updates.ReceiveBeta = enabled;
                    await UpdateInteraction.CheckAsync(_updates, _updates.Exit!, quietWhenCurrent: true, Prompt);
                }

                return CommandResult.Success;
            }

            case "changelog.seen":
                _whatsNew.Acknowledge();
                return CommandResult.Success;

            case "update.restart":
                if (_updates.Exit is { } exit)
                {
                    await exit(true);
                }

                return CommandResult.Success;

            case "auth.logout":
                if (Logout is null)
                {
                    return CommandResult.Fail("现在不能退出登录。");
                }

                await Logout();
                return CommandResult.Success;

            default:
                return CommandResult.Fail($"不支持的操作：{c.Type}");
        }
    }

    private async Task<CommandResult> QueryStatsAsync(WebCommand c)
    {
        if (!Enum.TryParse<StatsScale>(c.String("scale"), out var scale) || !Enum.TryParse<StatsGranularity>(c.String("granularity"), out var granularity))
        {
            return CommandResult.Fail("统计范围不正确");
        }

        var today = UsageService.Today;
        var anchor = DateOnly.TryParseExact(c.String("date"), "yyyy-MM-dd", out var date) && date <= today ? date : today;
        var range = StatsRange.Of(scale, anchor);
        var previous = StatsRange.Previous(scale, anchor);
        var to = range.To > today ? today : range.To;

        if (c.OptionalString("userId") is null)
        {
            var mine = await Task.Run(() => _usage.Report(range.From, to, (previous.From, previous.To)));
            return new CommandResult(true, Data: StatsViewBuilder.Build(mine, scale, granularity, anchor, today));
        }

        var userId = c.Guid("userId");
        if (_rooms.SelectedRoom is not { } room || room.Members.FirstOrDefault(m => m.UserId == userId) is not { } member)
        {
            return CommandResult.Fail("该成员不在当前房间");
        }

        if (room.Blocked?.Contains(userId) == true)
        {
            return CommandResult.Fail("已屏蔽该成员，取消屏蔽后才能查看");
        }

        if (!member.ShareStats)
        {
            return CommandResult.Fail($"{member.DisplayName} 没有在这个房间公开使用统计");
        }

        var first = today.AddDays(1 - member.StatsDays);
        var from = range.From < first ? first : range.From;
        if (from > to)
        {
            return CommandResult.Fail($"{member.DisplayName} 只公开了最近 {member.StatsDays} 天的统计");
        }

        try
        {
            var report = await _stats.RequestAsync(room.Id, userId, from, to, (previous.From, previous.To));
            return new CommandResult(true, Data: StatsViewBuilder.Build(report, scale, granularity, anchor, today, member.DisplayName));
        }
        catch (StatsUnavailableException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private async Task<CommandResult> CorrectAsync(WebCommand c)
    {
        var target = CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName);
        if (target is null)
        {
            return CommandResult.Fail("还没有识别到当前使用的应用。切换到要纠正的应用，再打开面板。");
        }

        if (!Enum.TryParse<ActivityCategory>(c.String("category"), out var category))
        {
            return CommandResult.Fail("请选择类别");
        }

        await _personalRules.SetAsync(target.Kind, target.Pattern, category, target.DisplayName);
        return CommandResult.Success;
    }

    private static string NormalizeTheme(string? theme) => theme is "light" or "dark" or "system" ? theme : "system";

    private static string NormalizeCloseAction(string? action) => action is "minimize" or "tray" or "ask" ? action : "ask";

    private void RaiseChanged() => Changed?.Invoke();
}
