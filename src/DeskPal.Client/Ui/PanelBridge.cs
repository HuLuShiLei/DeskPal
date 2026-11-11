using System.Text.Json;
using System.Windows;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Ui;
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
    private readonly PersonalRuleStore _personalRules;

    public PanelBridge(
        RoomState rooms,
        PresenceCoordinator presence,
        HubService hub,
        ApiClient api,
        AppSettings settings,
        SettingsStore store,
        UpdateService updates,
        PersonalRuleStore personalRules)
    {
        _rooms = rooms;
        _presence = presence;
        _hub = hub;
        _api = api;
        _settings = settings;
        _store = store;
        _updates = updates;
        _personalRules = personalRules;
        rooms.Changed += RaiseChanged;
        presence.LocalChanged += RaiseChanged;
        hub.StateChanged += _ => RaiseChanged();
        updates.Changed += RaiseChanged;
    }

    /// <summary>Raised from any thread when the state may have changed.</summary>
    public event Action? Changed;

    public event Action<bool>? TopmostChanged;

    /// <summary>Where update confirmations are shown. Set by the panel window.</summary>
    public IUserPrompt Prompt { get; set; } = MessageBoxPrompt.Instance;

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
        Paused = _presence.Paused,
        ManualText = _presence.ManualText,
        CorrectionTarget = CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName)?.DisplayName,
        Settings = new SettingsView(
            AutoStart.IsEnabled(),
            _settings.Topmost,
            _updates.ReceiveBeta,
            _updates.UpdateReady,
            _updates.VersionLabel,
            _settings.ServerUrl,
            _settings.UserName ?? ""),
        Now = DateTimeOffset.UtcNow,
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
                _rooms.Select(c.Guid("roomId"));
                return CommandResult.Success;

            case "room.create":
            {
                var name = c.String("name").Trim();
                if (name.Length == 0)
                {
                    return CommandResult.Fail("请输入 Room 名称");
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
                    message = $"邀请码已复制到剪贴板，{invite.ExpiresAt.LocalDateTime:M 月 d 日 HH:mm} 前有效。重新生成后旧邀请码失效。",
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

            case "room.transfer":
                await _api.TransferOwnerAsync(c.Guid("roomId"), c.Guid("userId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "room.leave":
                await _api.LeaveRoomAsync(c.Guid("roomId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "room.dissolve":
                await _api.DissolveRoomAsync(c.Guid("roomId"));
                await _rooms.RefreshAsync();
                return CommandResult.Success;

            case "member.poke":
                return await _rooms.PokeAsync(c.Guid("roomId"), c.Guid("userId"), c.OptionalString("text"))
                    ? CommandResult.Success
                    : CommandResult.Fail("没有发出去：当前未连接服务器。");

            case "presence.pause":
                _presence.SetPaused(c.Bool("paused"));
                return CommandResult.Success;

            case "presence.manual":
                _presence.SetManualText(c.OptionalString("text"));
                return CommandResult.Success;

            case "presence.correct":
                return await CorrectAsync(c);

            case "settings.autoStart":
                AutoStart.Set(c.Bool("enabled"));
                return CommandResult.Success;

            case "settings.topmost":
                _settings.Topmost = c.Bool("enabled");
                _store.Save(_settings);
                TopmostChanged?.Invoke(_settings.Topmost);
                return CommandResult.Success;

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

            case "update.restart":
                if (_updates.Exit is { } exit)
                {
                    await exit(true);
                }

                return CommandResult.Success;

            default:
                return CommandResult.Fail($"不支持的操作：{c.Type}");
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

        var rule = new RuleDto(target.Kind, target.Pattern, category, target.DisplayName);
        _personalRules.Upsert(rule);
        _presence.ReloadPersonalRules();
        if (c.Bool("shareToRoom") && _rooms.SelectedRoom is { } room)
        {
            await _api.UpsertRoomRuleAsync(room.Id, new UpsertRoomRuleRequest(rule.Kind, rule.Pattern, rule.Category, rule.Name));
            await _rooms.ReloadRoomRulesAsync();
        }

        return CommandResult.Success;
    }

    private void RaiseChanged() => Changed?.Invoke();
}