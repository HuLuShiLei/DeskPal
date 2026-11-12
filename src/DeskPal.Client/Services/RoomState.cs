using DeskPal.Client.Core.Classification;
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
    LayeredClassifier classifier,
    HubService hub,
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
                return _rooms.FirstOrDefault(r => r.Id == settings.SelectedRoomId) ?? _rooms.FirstOrDefault();
            }
        }
    }

    public void Select(Guid roomId)
    {
        var changed = settings.SelectedRoomId != roomId;
        settings.SelectedRoomId = roomId;
        store.Save(settings);
        Changed?.Invoke();
        if (changed && Rooms.Any(r => r.Id == roomId))
        {
            _ = ActivateSelectedAsync();
        }
    }

    private async Task ActivateSelectedAsync()
    {
        try
        {
            await ReloadRoomRulesAsync();
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
                var dto = await api.GetPrivacyAsync(room.Id);
                privacy[room.Id] = new PrivacyProfile { Levels = dto.Levels ?? new() };
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

            await ReloadRoomRulesAsync();
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

    public async Task ReloadRoomRulesAsync()
    {
        var room = SelectedRoom;
        var rules = room is null ? [] : await api.GetRoomRulesAsync(room.Id);
        classifier.SetRoom(new RuleSet(rules.OrderByDescending(r => r.UpdatedAt).Select(r => new RuleDto(r.Kind, r.Pattern, r.Category, r.Name))));
    }

    public async Task SavePrivacyAsync(Guid roomId, PrivacyProfile profile)
    {
        var saved = await api.SetPrivacyAsync(roomId, new PrivacyProfileDto(profile.Levels));
        lock (_lock)
        {
            _privacy[roomId] = new PrivacyProfile { Levels = saved.Levels ?? new() };
        }

        Changed?.Invoke();
        if (RoomsReloaded is { } handler)
        {
            await handler();
        }
    }

    public Task<bool> PokeAsync(Guid roomId, Guid targetUserId, string? text) =>
        hub.TryPublishAsync(Envelope.Create(MessageTypes.Poke, roomId, new PokePayload(text, targetUserId)));

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

                case MessageTypes.Poke:
                    var poke = envelope.ReadPayload<PokePayload>();
                    if (poke?.TargetUserId is null || poke.TargetUserId == MyUserId)
                    {
                        var text = string.IsNullOrWhiteSpace(poke?.Text) ? "戳了你一下" : $"戳了你一下：{poke!.Text}";
                        Notify?.Invoke(MemberName(envelope.RoomId, envelope.SenderId), text);
                    }

                    break;

                case MessageTypes.RoomKicked:
                    OnKicked(envelope);
                    break;

                case MessageTypes.RoomMemberJoined:
                case MessageTypes.RoomMemberLeft:
                case MessageTypes.RoomOwnerChanged:
                case MessageTypes.RoomDissolved:
                case MessageTypes.RoomRenamed:
                    OnRoomEvent(envelope);
                    break;

                case MessageTypes.RoomRulesChanged:
                    _ = ReloadRoomRulesAsync().ContinueWith(_ => Changed?.Invoke(), TaskScheduler.Default);
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
