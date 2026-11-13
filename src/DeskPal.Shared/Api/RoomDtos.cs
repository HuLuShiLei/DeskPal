using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

public enum RoomRole
{
    Member = 0,
    Owner = 1,
}

/// <param name="ShareStats">The member shares usage stats in this room.</param>
/// <param name="StatsDays">How many recent days of stats the member shares; 0 when not shared.</param>
public sealed record RoomMemberDto(Guid UserId, string DisplayName, RoomRole Role, DateTimeOffset JoinedAt, bool ShareStats = false, int StatsDays = 0);

/// <param name="Blocked">Members the requesting user has blocked in this room. Never includes other people's blocks.</param>
public sealed record RoomDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    int MaxMembers,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoomMemberDto> Members,
    IReadOnlyList<Guid>? Blocked = null);

public sealed record CreateRoomRequest(string Name);

public sealed record RenameRoomRequest(string Name);

public sealed record JoinRoomRequest(string Code);

public sealed record InviteDto(string Code, DateTimeOffset ExpiresAt);

public sealed record TransferOwnerRequest(Guid NewOwnerUserId);

public sealed record KickMemberRequest(Guid UserId);

public sealed record BlockMemberRequest(Guid UserId);

/// <param name="ShareStats">null keeps the saved value, so older clients do not turn sharing off.</param>
/// <param name="StatsDays">null keeps the saved value.</param>
public sealed record PrivacyProfileDto(Dictionary<ActivityCategory, PrivacyLevel> Levels, bool? ShareStats = null, int? StatsDays = null);
