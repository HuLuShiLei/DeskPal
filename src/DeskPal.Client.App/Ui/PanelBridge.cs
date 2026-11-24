using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Updates;
using DeskPal.Client.Core.Usage;
using DeskPal.Client.Platform;
using DeskPal.Client.Providers;
using DeskPal.Client.Services;
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
    private readonly StatsSyncService _sync;
    private readonly FolderSyncService _folder;
    private readonly PersonalDataService _personal;
    private readonly IClientPlatform _platform;
    private readonly IProviderControl? _provider;

    /// <summary>UTC offset of each member whose stats I looked at, from their last answer.</summary>
    private readonly ConcurrentDictionary<Guid, int> _peerOffsets = new();

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
        StatsExchange stats,
        StatsSyncService sync,
        FolderSyncService folder,
        PersonalDataService personal,
        IClientPlatform platform,
        IProviderControl? provider = null)
    {
        _platform = platform;
        _provider = provider;
        if (provider is not null)
        {
            provider.Changed += RaiseChanged;
        }

        Prompt = platform.FallbackPrompt;
        _usage = usage;
        _personal = personal;
        personal.Changed += RaiseChanged;
        personal.PreferencesApplied += ApplyPreferences;
        _stats = stats;
        _sync = sync;
        _folder = folder;
        sync.Changed += RaiseChanged;
        folder.Changed += RaiseChanged;
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

    /// <summary>Raised after <see cref="UiLanguage.Apply"/> so native text (tray menu, window titles) can be refreshed.</summary>
    public event Action? LanguageChanged;

    /// <summary>Where update confirmations are shown. Set by the panel window.</summary>
    public IUserPrompt Prompt { get; set; }

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
        CorrectionTarget = AppNames.LocalizeOrNull(CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName)?.DisplayName),
        MyRules = _personalRules.Rules,
        Unclassified = _unclassified.Top(TimeSpan.FromMinutes(5), 5),
        Settings = new SettingsView(
            _platform.AutoStartEnabled,
            _settings.Topmost,
            _updates.ReceiveBeta,
            _updates.UpdateReady,
            _updates.VersionLabel,
            _settings.UserName ?? "",
            _updates.ActivityText,
            _settings.ShareUsage,
            NormalizeTheme(_settings.Theme),
            NormalizeCloseAction(_settings.CloseAction),
            UiLanguage.Normalize(_settings.Language),
            _settings.IsAdmin,
            AccountIdle.Normalize(_settings.IdleMinutes),
            SwitchDelay.Normalize(_settings.SwitchDelaySeconds),
            new PersonalView(_personal.AccountSync, _personal.Folder, _personal.FolderUnavailable),
            _provider?.View),
        WhatsNew = _whatsNew.Pending,
        Pinned = _pins.Users,
        MemberOrder = _rooms.SelectedRoom is { } selected && _settings.MemberOrder.TryGetValue(selected.Id, out var order) ? order : [],
        Now = DateTimeOffset.UtcNow,
        ShowUsage = true,
        NoTitleRules = _personalRules.NoTitle,
        StatsViews = _usage.RecentViews,
        SyncAllowed = _sync.Allowed,
        SyncMode = _sync.Mode,
        SyncError = _sync.Error,
        FolderMode = _folder.Mode,
        FolderPath = _folder.Folder,
        Client = _platform.Name,
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
                    return CommandResult.Fail(Strings.Error_RoomNameRequired);
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
                    return CommandResult.Fail(Strings.Error_InviteCodeRequired);
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

            case "room.inviteGet":
                return new CommandResult(true, Data: new { code = (await _api.GetInviteAsync(c.Guid("roomId"))).Code });

            case "room.inviteCopy":
            {
                var code = (await _api.GetInviteAsync(c.Guid("roomId"))).Code;
                _platform.CopyText(code);
                return new CommandResult(true, Data: new { code });
            }

            case "room.invite":
            {
                var invite = await _api.CreateInviteAsync(c.Guid("roomId"));
                _platform.CopyText(invite.Code);
                return new CommandResult(true, Data: new { code = invite.Code });
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
                    return CommandResult.Fail(Strings.Error_StatsDays);
                }

                await _rooms.SaveStatsSharingAsync(c.Guid("roomId"), c.Bool("enabled"), days);
                return CommandResult.Success;
            }

            case "room.sharePausedStats":
                await _rooms.SavePausedStatsSharingAsync(c.Guid("roomId"), c.Bool("enabled"));
                return CommandResult.Success;

            case "stats.query":
                return await QueryStatsAsync(c);

            case "stats.app":
                return await QueryAppStatsAsync(c);

            case "stats.export":
            {
                var file = await _platform.PickSaveFileAsync(Strings.Export_Title, string.Format(Strings.Export_FileName, DateTime.Now.ToString("yyyyMMdd")));
                if (file is null)
                {
                    return CommandResult.Success;
                }

                await Task.Run(() => _usage.Export(file));
                return new CommandResult(true, Data: new { message = UiLanguage.Format(Strings.Export_Done, file) });
            }

            case "stats.import":
            {
                var file = await _platform.PickOpenFileAsync(Strings.Import_Title);
                if (file is null)
                {
                    return CommandResult.Success;
                }

                try
                {
                    var result = await Task.Run(() => _usage.Import(file));
                    var message = result.Segments + result.Rollups == 0
                        ? Strings.Import_NothingNew
                        : UiLanguage.Format(Strings.Import_Done, result.Segments, result.Rollups);
                    return new CommandResult(true, Data: new { message });
                }
                catch (InvalidDataException ex)
                {
                    return CommandResult.Fail(ex.Message);
                }
            }

            case "stats.clear":
                if (_sync.Mode != StatsSyncMode.Off)
                {
                    return CommandResult.Fail(Strings.Usage_ClearSyncOn);
                }

                if (_folder.Mode != FolderSyncMode.Off)
                {
                    return CommandResult.Fail(Strings.Usage_ClearFolderOn);
                }

                await Task.Run(_usage.Clear);
                return CommandResult.Success;

            case "stats.sync.enable":
            {
                var pin = c.String("pin");
                if (!StatsSyncCrypto.IsValidPin(pin))
                {
                    return CommandResult.Fail(Strings.Sync_PinInvalid);
                }

                if (_folder.Mode != FolderSyncMode.Off)
                {
                    return CommandResult.Fail(Strings.Sync_FolderOn);
                }

                return await _sync.EnableAsync(pin) ? CommandResult.Success : CommandResult.Fail(Strings.Sync_EnabledElsewhere);
            }

            case "stats.sync.unlock":
                return await _sync.UnlockAsync(c.String("pin")) switch
                {
                    StatsSyncUnlock.WrongPin => CommandResult.Fail(Strings.Sync_WrongPin),
                    StatsSyncUnlock.NotEnabled => CommandResult.Fail(Strings.ApiError_statsSync_notEnabled),
                    _ => CommandResult.Success,
                };

            case "stats.sync.disable":
                await _sync.DisableAsync();
                return CommandResult.Success;

            case "stats.folder.choose":
            {
                if (_sync.Mode != StatsSyncMode.Off)
                {
                    return CommandResult.Fail(Strings.Folder_ServerOn);
                }

                if (await _platform.PickFolderAsync(Strings.Folder_DialogTitle) is not { } path)
                {
                    return CommandResult.Success;
                }

                return await Task.Run(() => _folder.Check(path)) switch
                {
                    FolderCheck.NotWritable => CommandResult.Fail(Strings.Folder_NotWritable),
                    var check => new CommandResult(true, Data: new { path, existing = check == FolderCheck.Existing }),
                };
            }

            case "stats.folder.enable":
            {
                var pin = c.String("pin");
                if (!StatsSyncCrypto.IsValidPin(pin))
                {
                    return CommandResult.Fail(Strings.Sync_PinInvalid);
                }

                if (_sync.Mode != StatsSyncMode.Off)
                {
                    return CommandResult.Fail(Strings.Folder_ServerOn);
                }

                return await _folder.EnableAsync(c.String("path"), pin) switch
                {
                    FolderSyncEnable.WrongPin => CommandResult.Fail(Strings.Sync_WrongPin),
                    FolderSyncEnable.NotWritable => CommandResult.Fail(Strings.Folder_NotWritable),
                    _ => CommandResult.Success,
                };
            }

            case "stats.folder.stop":
                await _folder.DisableAsync();
                return CommandResult.Success;

            case "noTitle.add":
            {
                if (!Enum.TryParse<NoTitleKind>(c.String("kind"), out var kind) || !Enum.IsDefined(kind))
                {
                    return CommandResult.Fail(Strings.Error_NoTitleKind);
                }

                if (NoTitleRule.Create(kind, c.String("pattern")) is not { } rule)
                {
                    return CommandResult.Fail(kind == NoTitleKind.Domain ? Strings.Error_NoTitleDomain : UiLanguage.Format(Strings.Error_NoTitleLength, NoTitleRule.MaxLength));
                }

                await _personalRules.AddNoTitleAsync(rule);
                return CommandResult.Success;
            }

            case "noTitle.remove":
            {
                if (Enum.TryParse<NoTitleKind>(c.String("kind"), out var kind))
                {
                    await _personalRules.RemoveNoTitleAsync(new NoTitleRule(kind, c.String("pattern")));
                }

                return CommandResult.Success;
            }

            case "personal.sync.enable":
                if (_personal.Folder is not null)
                {
                    return CommandResult.Fail(Strings.Personal_AccountFolderOn);
                }

                await _personal.EnableAccountSyncAsync();
                return CommandResult.Success;

            case "personal.sync.disable":
                await _personal.DisableAccountSyncAsync();
                return CommandResult.Success;

            case "personal.folder.choose":
            {
                if (_personal.AccountSync)
                {
                    return CommandResult.Fail(Strings.Personal_FolderAccountOn);
                }

                if (await _platform.PickFolderAsync(Strings.Personal_FolderDialogTitle) is not { } path)
                {
                    return CommandResult.Success;
                }

                if (!await Task.Run(() => _personal.CanUseFolder(path)))
                {
                    return CommandResult.Fail(Strings.Folder_NotWritable);
                }

                await _personal.EnableFolderAsync(path);
                return CommandResult.Success;
            }

            case "personal.folder.stop":
                _personal.StopFolder();
                return CommandResult.Success;

            case "personal.export":
            {
                var file = await _platform.PickSaveFileAsync(Strings.Personal_ExportTitle, string.Format(Strings.Personal_ExportFileName, DateTime.Now.ToString("yyyyMMdd")));
                if (file is null)
                {
                    return CommandResult.Success;
                }

                await Task.Run(() => _personal.Export(file));
                return new CommandResult(true, Data: new { message = UiLanguage.Format(Strings.Export_Done, file) });
            }

            case "personal.import":
            {
                var file = await _platform.PickOpenFileAsync(Strings.Personal_ImportTitle);
                if (file is null)
                {
                    return CommandResult.Success;
                }

                try
                {
                    var (rules, noTitle) = await _personal.ImportAsync(file);
                    return new CommandResult(true, Data: new { message = UiLanguage.Format(Strings.Personal_ImportDone, rules, noTitle) });
                }
                catch (InvalidDataException ex)
                {
                    return CommandResult.Fail(ex.Message);
                }
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
                    return CommandResult.Fail(Strings.Error_MemberNotInRoom);
                }

                if (room.Blocked?.Contains(userId) == true)
                {
                    return CommandResult.Fail(Strings.Error_BlockedCannotPin);
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

            case "member.reorder":
            {
                var roomId = c.Guid("roomId");
                var rooms = _rooms.Rooms;
                if (rooms.FirstOrDefault(r => r.Id == roomId) is not { } room)
                {
                    return CommandResult.Fail(Strings.Error_MemberNotInRoom);
                }

                foreach (var gone in _settings.MemberOrder.Keys.Where(id => rooms.All(r => r.Id != id)).ToList())
                {
                    _settings.MemberOrder.Remove(gone);
                }

                var requested = c.GuidList("userIds");
                var order = _rooms.SelectedRoom?.Id == room.Id
                    ? MemberOrder.Merge(_settings.MemberOrder.GetValueOrDefault(room.Id) ?? [], BuildState().Members, requested)
                    : requested.Distinct().Where(id => room.Members.Any(m => m.UserId == id)).ToList();
                _settings.MemberOrder[room.Id] = order;
                _store.Save(_settings);
                RaiseChanged();
                await SyncAsync(new UserPreferences(MemberOrder: new() { [room.Id] = order }));
                return CommandResult.Success;
            }

            case "presence.pause":
                _presence.SetPaused(c.Bool("paused"));
                return CommandResult.Success;

            case "presence.correct":
                return await CorrectAsync(c);

            case "presence.resetCorrection":
            {
                if (CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName) is not { } target)
                {
                    return CommandResult.Fail(Strings.Error_NothingRecognized);
                }

                await _personalRules.DeleteAsync(target.Kind, target.Pattern);
                return CommandResult.Success;
            }

            case "rules.set":
            {
                if (!Enum.TryParse<RuleKind>(c.String("kind"), out var kind) || !Enum.TryParse<ActivityCategory>(c.String("category"), out var category))
                {
                    return CommandResult.Fail(Strings.Error_CategoryRequired);
                }

                await _personalRules.SetAsync(kind, c.String("pattern"), category, c.OptionalString("name"));
                return CommandResult.Success;
            }

            case "rules.delete":
            {
                if (!Enum.TryParse<RuleKind>(c.String("kind"), out var kind))
                {
                    return CommandResult.Fail(Strings.Error_RuleKind);
                }

                await _personalRules.DeleteAsync(kind, c.String("pattern"));
                return CommandResult.Success;
            }

            case "me.rename":
            {
                var name = c.String("name").Trim();
                if (name.Length is < 1 or > 20)
                {
                    return CommandResult.Fail(Strings.Error_DisplayName);
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

            case "signupInvites.list":
                return new CommandResult(true, Data: SignupInviteItems(await _api.GetSignupInvitesAsync()));

            case "signupInvites.create":
            {
                var invite = await _api.CreateSignupInviteAsync();
                _platform.CopyText(invite.Code);
                return new CommandResult(true, Data: new
                {
                    code = invite.Code,
                    message = Strings.SignupInvite_Copied,
                    items = SignupInviteItems(await _api.GetSignupInvitesAsync()),
                });
            }

            case "signupInvites.copy":
                _platform.CopyText(c.String("code"));
                return CommandResult.Success;

            case "signupInvites.revoke":
                await _api.RevokeSignupInviteAsync(c.String("code"));
                return new CommandResult(true, Data: SignupInviteItems(await _api.GetSignupInvitesAsync()));

            case "settings.autoStart":
                _platform.SetAutoStart(c.Bool("enabled"));
                return CommandResult.Success;

            case "settings.topmost":
                _settings.Topmost = c.Bool("enabled");
                _store.Save(_settings);
                TopmostChanged?.Invoke(_settings.Topmost);
                await SyncAsync(new UserPreferences(Topmost: _settings.Topmost));
                return CommandResult.Success;

            case "settings.closeAction":
            {
                var action = NormalizeCloseAction(c.String("action"));
                if (action != c.String("action"))
                {
                    return CommandResult.Fail(Strings.Error_CloseAction);
                }

                _settings.CloseAction = action;
                _store.Save(_settings);
                RaiseChanged();
                await SyncAsync(new UserPreferences(CloseAction: action));
                return CommandResult.Success;
            }

            case "settings.theme":
            {
                var theme = NormalizeTheme(c.String("theme"));
                if (theme != c.String("theme"))
                {
                    return CommandResult.Fail(Strings.Error_Theme);
                }

                _settings.Theme = theme;
                _store.Save(_settings);
                ThemeChanged?.Invoke(theme);
                await SyncAsync(new UserPreferences(Theme: theme));
                return CommandResult.Success;
            }

            case "settings.idleMinutes":
            {
                var minutes = c.Args.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0;
                if (!AccountIdle.IsAllowed(minutes))
                {
                    return CommandResult.Fail(ApiErrorText.Describe(ApiErrorCodes.IdleMinutesInvalid)!);
                }

                if (_settings.PersonalSync)
                {
                    await _api.SetIdleMinutesAsync(minutes);
                }

                _settings.IdleMinutes = minutes;
                _store.Save(_settings);
                _personal.PreferencesEdited();
                return CommandResult.Success;
            }

            case "settings.switchDelay":
            {
                var seconds = c.Args.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : -1;
                if (!SwitchDelay.IsAllowed(seconds))
                {
                    return CommandResult.Fail(Strings.Error_SwitchDelay);
                }

                _presence.SetSwitchDelay(seconds);
                await SyncAsync(new UserPreferences(SwitchDelaySeconds: seconds));
                return CommandResult.Success;
            }

            case "settings.language":
            {
                var language = UiLanguage.Normalize(c.String("language"));
                if (language != c.String("language"))
                {
                    return CommandResult.Fail(Strings.Error_Language);
                }

                _settings.Language = language;
                _store.Save(_settings);
                UiLanguage.Apply(UiLanguage.Resolve(language, CultureInfo.CurrentUICulture));
                LanguageChanged?.Invoke();
                await SyncAsync(new UserPreferences(Language: language));
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

            case "plugins.setEnabled" when _provider is not null:
                return _provider.SetEnabled(c.String("id"), c.Bool("enabled"));

            case "plugins.move" when _provider is not null:
                return _provider.Move(c.String("id"), c.Args.TryGetProperty("offset", out var offset) && offset.TryGetInt32(out var by) ? by : 0);

            case "plugins.action" when _provider is not null:
                return await _provider.RunActionAsync(c.String("id"), c.String("action"));

            case "plugins.refresh" when _provider is not null:
                _provider.Refresh();
                return CommandResult.Success;

            case "plugins.openFolder" when _provider is not null:
                _platform.OpenExternal(_provider.Folder);
                return CommandResult.Success;

            case "logs.open":
                Directory.CreateDirectory(AppPaths.LogDirectory);
                _platform.OpenExternal(AppPaths.LogDirectory);
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
                    return CommandResult.Fail(Strings.Error_LogoutUnavailable);
                }

                await Logout();
                return CommandResult.Success;

            default:
                return CommandResult.Fail(UiLanguage.Format(Strings.Error_Unsupported, c.Type));
        }
    }

    private static object[] SignupInviteItems(List<SignupInviteDto> invites) => invites
        .Select(i => (object)new
        {
            code = i.Code,
            used = i.UsedAt is not null,
            text = i.UsedAt is { } usedAt
                ? UiLanguage.Format(Strings.SignupInvite_UsedBy, i.UsedByDisplayName ?? "", i.UsedByUserName ?? "", UiLanguage.Format(Strings.Date_MonthDayTime, usedAt.LocalDateTime))
                : UiLanguage.Format(Strings.SignupInvite_Created, UiLanguage.Format(Strings.Date_MonthDayTime, i.CreatedAt.LocalDateTime)),
        })
        .ToArray();

    private async Task<CommandResult> QueryStatsAsync(WebCommand c)
    {
        if (!Enum.TryParse<StatsScale>(c.String("scale"), out var scale) || !Enum.TryParse<StatsRankBy>(c.String("rankBy"), out var rankBy))
        {
            return CommandResult.Fail(Strings.Error_StatsRange);
        }

        var today = UsageService.Today;
        var picked = DateOnly.TryParseExact(c.String("date"), "yyyy-MM-dd", out var date) ? date : (DateOnly?)null;
        var filter = c.OptionalString("filter");

        if (c.OptionalString("userId") is null)
        {
            var anchor = picked is { } d && d <= today ? d : today;
            var range = StatsRange.Of(scale, anchor);
            var previous = StatsRange.Previous(scale, anchor);
            var to = range.To > today ? today : range.To;
            var hours = scale == StatsScale.Day ? c.HourList("hours") : null;
            var mine = await Task.Run(() => _usage.Report(range.From, to, (previous.From, previous.To), hours: hours));
            return new CommandResult(true, Data: StatsViewBuilder.Build(mine, scale, rankBy, anchor, today, filter: filter, myOffset: UsageService.Offset, hours: hours));
        }

        var userId = c.Guid("userId");
        if (_rooms.SelectedRoom is not { } room || room.Members.FirstOrDefault(m => m.UserId == userId) is not { } member)
        {
            return CommandResult.Fail(Strings.Error_MemberNotInRoom);
        }

        if (room.Blocked?.Contains(userId) == true)
        {
            return CommandResult.Fail(Strings.Error_BlockedCannotView);
        }

        if (!member.ShareStats)
        {
            return CommandResult.Fail(UiLanguage.Format(Strings.Error_MemberNotSharing, member.DisplayName));
        }

        // Days are the member's local days. Their date is known from an earlier answer; the first answer may show it
        // differs from mine, and then the page is asked for again by their date.
        var peerToday = _peerOffsets.TryGetValue(userId, out var known) ? TodayAt(known) : today;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var anchor = picked is { } d && d <= peerToday ? d : peerToday;
                var range = StatsRange.Of(scale, anchor);
                var previous = StatsRange.Previous(scale, anchor);
                var to = range.To > peerToday ? peerToday : range.To;
                var first = peerToday.AddDays(1 - member.StatsDays);
                var from = range.From < first ? first : range.From;
                if (from > to)
                {
                    return CommandResult.Fail(UiLanguage.Format(Strings.Error_MemberSharedDays, member.DisplayName, PresenceFormatter.Days(member.StatsDays)));
                }

                var report = await _stats.RequestAsync(room.Id, userId, from, to, (previous.From, previous.To));
                if (report.Offset is { } offset)
                {
                    _peerOffsets[userId] = offset;
                }

                var answeredToday = report.Today ?? peerToday;
                if (answeredToday != peerToday && attempt == 0)
                {
                    peerToday = answeredToday;
                    continue;
                }

                return new CommandResult(true, Data: StatsViewBuilder.Build(report, scale, rankBy, anchor, answeredToday, member.DisplayName, filter, UsageService.Offset));
            }
        }
        catch (StatsUnavailableException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private static DateOnly TodayAt(int offsetMinutes) => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(offsetMinutes)).DateTime);

    /// <summary>Detail page of one of my ranking rows; only for my own stats.</summary>
    private async Task<CommandResult> QueryAppStatsAsync(WebCommand c)
    {
        if (!Enum.TryParse<StatsScale>(c.String("scale"), out var scale) || c.OptionalString("app") is not { Length: > 0 } app)
        {
            return CommandResult.Fail(Strings.Error_StatsRange);
        }

        var today = UsageService.Today;
        var anchor = DateOnly.TryParseExact(c.String("date"), "yyyy-MM-dd", out var date) && date <= today ? date : today;
        var range = StatsRange.Of(scale, anchor);
        var to = range.To > today ? today : range.To;
        var title = c.OptionalString("title");
        var (detail, targets) = await Task.Run(() => (_usage.AppDetail(range.From, to, app, title), _usage.RuleTargets()));
        return new CommandResult(true, Data: StatsAppViewBuilder.Build(detail, app, title, scale, anchor, today, targets(app)));
    }

    private async Task<CommandResult> CorrectAsync(WebCommand c)
    {
        var target = CorrectionTarget.From(_presence.Snapshot, _presence.Classification?.AppDisplayName);
        if (target is null)
        {
            return CommandResult.Fail(Strings.Error_NothingToCorrect);
        }

        if (!Enum.TryParse<ActivityCategory>(c.String("category"), out var category))
        {
            return CommandResult.Fail(Strings.Error_CategoryRequired);
        }

        await _personalRules.SetAsync(target.Kind, target.Pattern, category, target.DisplayName);
        return CommandResult.Success;
    }

    private static string NormalizeTheme(string? theme) => theme is "light" or "dark" or "system" ? theme : "system";

    private static string NormalizeCloseAction(string? action) => action is "minimize" or "tray" or "quit" or "ask" ? action : "ask";

    private void RaiseChanged() => Changed?.Invoke();

    private Task SyncAsync(UserPreferences change)
    {
        _personal.PreferencesEdited();
        return PreferencesSync.UploadAsync(_settings, _store, _api, change);
    }

    /// <summary>Preferences came from the account, a file or the sync folder. Runs on any thread.</summary>
    private void ApplyPreferences()
    {
        TopmostChanged?.Invoke(_settings.Topmost);
        ThemeChanged?.Invoke(NormalizeTheme(_settings.Theme));
        UiLanguage.Apply(UiLanguage.Resolve(UiLanguage.Normalize(_settings.Language), CultureInfo.CurrentUICulture));
        LanguageChanged?.Invoke();
        _presence.SetSwitchDelay(SwitchDelay.Normalize(_settings.SwitchDelaySeconds));
        RaiseChanged();
    }
}
