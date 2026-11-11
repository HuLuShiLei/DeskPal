using DeskPal.Client.Core.Updates;

namespace DeskPal.Tests.Update;

public class UpdatePolicyTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void ReceiveBeta_uses_the_saved_choice_or_the_installed_channel(bool? setting, bool installedPrerelease, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.ReceiveBeta(setting, installedPrerelease));

    [Theory]
    [InlineData(true, UpdatePolicy.BetaChannel)]
    [InlineData(false, UpdatePolicy.StableChannel)]
    public void Channel_matches_the_choice(bool receiveBeta, string expected) =>
        Assert.Equal(expected, UpdatePolicy.Channel(receiveBeta));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Downgrade_is_not_applied_without_asking(bool isDowngrade, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.ApplyWithoutAsking(isDowngrade));
}