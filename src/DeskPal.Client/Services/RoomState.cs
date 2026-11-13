using DeskPal.Shared.Api;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>Rooms, members' latest presence and my privacy profile per Room. Updated from REST and hub messages.</summary>
public sealed class RoomState(
    ApiClient api,
    AppSettings settings,
    SettingsStore store,
    ILogger<RoomState> logger)
{
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private List<RoomDto> _rooms = [];
    private Dictionary<Guid, PrivacyProfile> _privacy = new();
    private readonly Dictionary<(Guid RoomId, Guid UserId), PresenceSnapshot> _presence = new();

    public event Action? Changed;

    public event Action<string, string>? Notify;

    /// <summary>Raised after rooms or privacy profiles change, so my presence is re-sent with the right filter.</summary>
    public event Func<Task>? RoomsReloaded;

    /// <summary>Raised after the active room changes to one already in the list.</summary>
    public event Func<Task>? SelectionChanged;

    public Guid MyUserId => settings.UserId;

    public IReadOnlyList<RoomDto> Rooms
    {
        get
        {
            lock (_lock)
            {
                return _rooms;
            }
        }
    }

    public RoomDto? SelectedRoom
    {
        get
        {
            lock (_lock)
            {
                return settings.NoRoom ? null : _rooms.FirstOrDefault(r => r.Id == settings.SelectedRoomId) ?? _rooms.FirstOrDefault();
            }
        }
    }

    /// <param name="roomId">null leaves every room: they all see me offline until a room is selected again.</param>
    public void Select(Guid? roomId)
    {
        var changed = roomId is null ? !settings.NoRoom : settings.NoRoom || settings.SelectedRoomId != roomId;
        settings.NoRoom = roomId is null;
        if (roomId is not null)
        {
            settings.SelectedRoomId = roomId;
        }

        store.Save(settings);
        if (roomId is null)
        {
            logger.LogInformation("Left all rooms");
        }
        else
        {
            logger.LogInformation("Selected room {RoomId}", roomId);
        }

        Changed?.Invoke();
        if (changed && (roomId is null || Rooms.Any(r => r.Id == roomId)))
        {
            _ = ActivateSelectedAsync();
        }
    }

    private async Task ActivateSelectedAsync()
    {
        try
        {
            if (SelectionChanged is { } handler)
            {
                await handler();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Switching the active room failed");
        }
    }

    public PrivacyProfile GetPrivacy(Guid roomId)
    {
        lock (_lock)
        {
            return _privacy.GetValueOrDefault(roomId) ?? PrivacyProfile.CreateDefault();
        }
    }

    public PresenceSnapshot? GetPresence(Guid roomId, Guid userId)
    {
        lock (_lock)
        {
            return _presence.GetValueOrDefault((roomId, userId));
        }
    }

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var rooms = await api.GetRoomsAsync();
            var privacy = new Dictionary<Guid, PrivacyProfile>();
            foreach (var room in rooms)
            {
                privacy[room.Id] = ToProfile(await api.GetPrivacyAsync(room.Id));
            }

            lock (_lock)
            {
                _rooms = rooms;
                _privacy = privacy;
                var memberKeys = rooms.SelectMany(r => r.Members.Select(m => (r.Id, m.UserId))).ToHashSet();
                foreach (var key in _presence.Keys.Where(k => !memberKeys.Contains(k)).ToList())
                {
                    _presence.Remove(key);
                }
            }

            if (settings.SelectedRoomId is { } selected && rooms.All(r => r.Id != selected))
            {
                settings.SelectedRoomId = rooms.FirstOrDefault()?.Id;
                store.Save(settings);
            }
        }
        finally
        {
            _refreshGate.Release();
        }

        Changed?.Invoke();
        if (RoomsReloaded is { } handler)
        {
            await handler();
        }
    }

    public async Task SavePrivacyAsync(Guid roomId, PrivacyProfile profile)
    {
        await SaveAsync(roomId, new PrivacyProfileDto(profile.Levels));
        if (RoomsReloaded is { } handler)
        {
            await handler();
        }
    }

    /// <summary>Turns usage stats sharing on or off in a room. The live presence is not affected.</summary>
    public Task SaveStatsSharingAsync(Guid roomId, bool share, int days) =>
        SaveAsync(roomId, new PrivacyProfileDto(GetPrivacy(roomId).Levels, share, days));

    private async Task SaveAsync(Guid roomId, PrivacyProfileDto dto)
    {
        var saved = await api.SetPrivacyAsync(roomId, dto);
        lock (_lock)
        {
            _privacy[roomId] = ToProfile(saved);
        }

        Changed?.Invoke();
    }

    private static PrivacyProfile ToProfile(PrivacyProfileDto dto) => new()
    {
        Levels = dto.Levels ?? new(),
        ShareStats = dto.ShareStats ?? false,
        StatsDays = dto.StatsDays ?? PrivacyProfile.DefaultStatsDays,
    };

    public void Handle(Envelope envelope)
    {
        try
        {
            switch (envelope.Type)
            {
                case MessageTypes.PresenceUpdate when envelope.SenderId != MyUserId:
                    if (envelope.Enc == EncodingKinds.None && envelope.ReadPayload<PresenceSnapshot>() is { } snapshot)
                    {
                        lock (_lock)
                        {
                            _presence[(envelope.RoomId, envelope.SenderId)] = snapshot;
                        }

                        Changed?.Invoke();
                    }

                    break;

                case MessageTypes.RoomKicked:
                    OnKicked(envelope);
                    break;

                case MessageTypes.MemberRenamed:
                    _ = OnMemberRenamedAsync(envelope.ReadPayload<RoomEventPayload>()?.UserId);
                    break;

                case MessageTypes.RoomBlocksChanged:
                case MessageTypes.MemberSharingChanged:
                    _ = RefreshAsync().ContinueWith(t => logger.LogWarning(t.Exception, "Refreshing rooms failed"), TaskContinuationOptions.OnlyOnFaulted);
                    break;

                case MessageTypes.RoomMemberJoined:
                case MessageTypes.RoomMemberLeft:
                case MessageTypes.RoomOwnerChanged:
                case MessageTypes.RoomDissolved:
                case MessageTypes.RoomRenamed:
                    OnRoomEvent(envelope);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Handling {Type} failed", envelope.Type);
        }
    }

    private void OnRoomEvent(Envelope envelope)
    {
        var roomName = Rooms.FirstOrDefault(r => r.Id == envelope.RoomId)?.Name ?? "房间";
        var userId = envelope.ReadPayload<RoomEventPayload>()?.UserId;
        if (envelope.Type == MessageTypes.RoomDissolved)
        {
            Notify?.Invoke(roomName, "该房间已被所有者解散");
        }
        else if (envelope.Type == MessageTypes.RoomMemberJoined && userId is { } joined && joined != MyUserId)
        {
            _ = RefreshAsync().ContinueWith(_ => Notify?.Invoke(roomName, $"{MemberName(envelope.RoomId, joined)} 加入了房间"), TaskScheduler.Default);
            return;
        }

        _ = RefreshAsync().ContinueWith(t => logger.LogWarning(t.Exception, "Refreshing rooms failed"), TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Renamed on another computer: take the new name from the server, then reload the member lists.</summary>
    private async Task OnMemberRenamedAsync(Guid? userId)
    {
        try
        {
            if (userId == MyUserId)
            {
                var me = await api.MeAsync();
                if (settings.DisplayName != me.DisplayName)
                {
                    settings.DisplayName = me.DisplayName;
                    store.Save(settings);
                }
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Refreshing after a rename failed");
        }
    }

    private string MemberName(Guid roomId, Guid userId) =>
        Rooms.FirstOrDefault(r => r.Id == roomId)?.Members.FirstOrDefault(m => m.UserId == userId)?.DisplayName ?? "房间成员";

    private void OnKicked(Envelope envelope)
    {
        var userId = envelope.ReadPayload<RoomEventPayload>()?.UserId;
        if (userId != MyUserId)
        {
            return;
        }

        var roomName = Rooms.FirstOrDefault(r => r.Id == envelope.RoomId)?.Name ?? "房间";
        Notify?.Invoke(roomName, "你已被移出该房间。邀请码更新后才能再次加入");
        _ = RefreshAsync().ContinueWith(t => logger.LogWarning(t.Exception, "Refreshing rooms failed"), TaskContinuationOptions.OnlyOnFaulted);
    }
}
