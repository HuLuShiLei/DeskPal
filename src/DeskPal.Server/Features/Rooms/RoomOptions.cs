namespace DeskPal.Server.Features.Rooms;

public sealed class RoomOptions
{
    public int MaxOwnedRoomsPerUser { get; set; } = 5;
    public int MaxMembersPerRoom { get; set; } = 2;
    public int InviteLifetimeHours { get; set; } = 24;
}