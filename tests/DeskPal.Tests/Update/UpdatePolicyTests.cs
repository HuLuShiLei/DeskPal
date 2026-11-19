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

    [Theory]
    [InlineData(null, "github")]
    [InlineData("github", "github")]
    [InlineData("ghproxy", "ghproxy")]
    [InlineData("", "github")]
    [InlineData("GHProxy", "github")]
    [InlineData("mirror", "github")]
    public void Unknown_sources_fall_back_to_github(string? saved, string expected) =>
        Assert.Equal(expected, UpdatePolicy.NormalizeSource(saved));

    [Theory]
    [InlineData("github", "https://api.github.com/")]
    [InlineData(null, "https://api.github.com/")]
    [InlineData("ghproxy", "https://gh-proxy.com/https://api.github.com/")]
    public void Only_the_mirror_prefixes_the_address(string? source, string expected) =>
        Assert.Equal(expected, UpdatePolicy.SourceUrl(source, "https://api.github.com/"));

    [Fact]
    public void Mirror_keeps_the_asset_address_after_its_prefix() =>
        Assert.Equal(
            "https://gh-proxy.com/https://github.com/HuLuShiLei/DeskPal/releases/download/v1/DeskPal-win-full.nupkg",
            UpdatePolicy.SourceUrl("ghproxy", "https://github.com/HuLuShiLei/DeskPal/releases/download/v1/DeskPal-win-full.nupkg"));

    /// <summary>Velopack builds the release list address with new Uri(baseUri, "repos/..."); the mirror path must survive that.</summary>
    [Fact]
    public void Mirror_api_base_url_still_gets_the_releases_path_appended()
    {
        var baseUri = new Uri(UpdatePolicy.SourceUrl("ghproxy", "https://api.github.com/"));
        var releases = new Uri(baseUri, "repos/HuLuShiLei/DeskPal/releases?per_page=10&page=1");
        Assert.Equal(
            "https://gh-proxy.com/https://api.github.com/repos/HuLuShiLei/DeskPal/releases?per_page=10&page=1",
            releases.ToString());
    }
}