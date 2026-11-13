using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Server.Data;

public sealed class User
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>null until the user has answered. Only users who agreed are counted when proposing builtin rules.</summary>
    public bool? ShareUsage { get; set; }

    /// <summary>
    /// Replaced on every sign-in, so the account is signed in on one computer at a time. null until the first sign-in after
    /// this was added; tokens issued before that carry no session and stay valid until then.
    /// </summary>
    public Guid? SessionId { get; set; }
}

public sealed class Room
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid OwnerId { get; set; }
    public int MaxMembers { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<RoomMember> Members { get; set; } = new();
}

public sealed class RoomMember
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public RoomRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public string? PrivacyProfileJson { get; set; }

    public Room? Room { get; set; }
    public User? User { get; set; }
}

public sealed class RoomBan
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public Guid BannedBy { get; set; }
    public DateTimeOffset BannedAt { get; set; }
}

/// <summary>The blocker and the blocked member stop seeing each other's presence in this room. Only the blocker knows.</summary>
public sealed class RoomBlock
{
    public Guid RoomId { get; set; }
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RoomInvite
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public required string Code { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class BuiltinRule
{
    public int Id { get; set; }
    public RuleKind Kind { get; set; }
    public required string Pattern { get; set; }
    public ActivityCategory Category { get; set; }
    public string? Name { get; set; }
}

public sealed class UserRule
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public RuleKind Kind { get; set; }
    public required string Pattern { get; set; }
    public ActivityCategory Category { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Latest report per user and app. Only foreground minutes of one day, no titles or times of use.</summary>
public sealed class UnknownAppReport
{
    public Guid UserId { get; set; }
    public RuleKind Kind { get; set; }
    public required string Pattern { get; set; }
    public string? DisplayName { get; set; }
    public int Minutes { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
}

public sealed class AppMeta
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}
