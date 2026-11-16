using DeskPal.Server.Features.Auth;

namespace DeskPal.Tests.Server;

public sealed class AuthOptionsTests
{
    [Fact]
    public void Admin_names_are_comma_separated_case_insensitive_and_trimmed()
    {
        var options = new AuthOptions { AdminUserNames = [" InviteAdmin , inviteadmin2 ,, ", "third"] };

        Assert.True(options.IsAdmin("inviteadmin"));
        Assert.True(options.IsAdmin("INVITEADMIN2"));
        Assert.True(options.IsAdmin("Third"));
        Assert.False(options.IsAdmin("invite"));
        Assert.False(options.IsAdmin(""));
    }
}