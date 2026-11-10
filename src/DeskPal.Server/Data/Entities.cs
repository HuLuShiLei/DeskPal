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

public sealed class RoomRule
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public RuleKind Kind { get; set; }
    public required string Pattern { get; set; }
    public ActivityCategory Category { get; set; }
    public string? Name { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AppMeta
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}