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
    [InlineData(false, null, "win")]
    [InlineData(true, null, "beta")]
    [InlineData(false, "arm64", "osx-arm64")]
    [InlineData(true, "arm64", "osx-arm64-beta")]
    [InlineData(false, "x64", "osx-x64")]
    [InlineData(true, "x64", "osx-x64-beta")]
    public void Mac_builds_use_their_own_channel_per_chip(bool receiveBeta, string? arch, string expected) =>
        Assert.Equal(expected, UpdatePolicy.Channel(receiveBeta, arch));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Downgrade_is_not_applied_without_asking(bool isDowngrade, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.ApplyWithoutAsking(isDowngrade));
}