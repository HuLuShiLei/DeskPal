using DeskPal.Client.Core.Ui;

namespace DeskPal.Tests.Ui;

public class PinnedMembersTests
{
    private static readonly Guid RoomA = Guid.NewGuid();
    private static readonly Guid RoomB = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    [Fact]
    public void Pins_several_members_once_each_and_reports_changes()
    {
        var pins = new PinnedMembers();
        var changes = 0;
        pins.Changed += () => changes++;

        pins.Pin(RoomA, Bob);
        pins.Pin(RoomA, Carol);
        pins.Pin(RoomA, Bob);

        Assert.Equal([Bob, Carol], pins.Users);
        Assert.Equal(2, changes);

        pins.Unpin(Bob);
        pins.Unpin(Bob);
        Assert.Equal([Carol], pins.Users);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Switching_rooms_drops_every_pin_and_they_do_not_come_back()
    {
        var pins = new PinnedMembers();
        pins.Pin(RoomA, Bob);
        pins.Pin(RoomA, Carol);

        Assert.Equal([Bob, Carol], pins.Retain(RoomB, [Bob, Carol]));
        Assert.Empty(pins.Users);
        Assert.Null(pins.RoomId);

        Assert.Empty(pins.Retain(RoomA, [Bob, Carol]));
        Assert.Empty(pins.Users);
    }

    [Fact]
    public void Leaving_every_room_drops_every_pin()
    {
        var pins = new PinnedMembers();
        pins.Pin(RoomA, Bob);

        Assert.Equal([Bob], pins.Retain(null, []));
        Assert.Empty(pins.Users);
    }

    [Fact]
    public void A_member_who_left_or_was_kicked_is_unpinned()
    {
        var pins = new PinnedMembers();
        pins.Pin(RoomA, Bob);
        pins.Pin(RoomA, Carol);
        var changes = 0;
        pins.Changed += () => changes++;

        Assert.Equal([Bob], pins.Retain(RoomA, [Carol]));
        Assert.Equal([Carol], pins.Users);
        Assert.Empty(pins.Retain(RoomA, [Carol]));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Pinning_in_another_room_replaces_the_old_pins()
    {
        var pins = new PinnedMembers();
        pins.Pin(RoomA, Bob);

        pins.Pin(RoomB, Carol);

        Assert.Equal([Carol], pins.Users);
        Assert.Equal(RoomB, pins.RoomId);
    }
}