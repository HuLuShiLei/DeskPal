using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;

namespace DeskPal.Tests.Providers;

public class CapabilityRouterTests
{
    private static readonly HashSet<ProviderCapability> Everything = ProviderCapabilities.All.ToHashSet();
    private static readonly HashSet<ProviderCapability> WindowOnly = [ProviderCapability.Foreground];

    private static RoutingEntry Official(bool enabled = true, bool healthy = true) => new("official:linux", enabled, healthy, Everything);

    private static RoutingEntry User(bool enabled = true, bool healthy = true) => new("user:sway.sh", enabled, healthy, WindowOnly);

    [Fact]
    public void Higher_plugin_takes_only_what_it_supplies()
    {
        var owners = ProviderRouting.Route([User(), Official()]);

        Assert.Equal("user:sway.sh", owners[ProviderCapability.Foreground]);
        Assert.Equal("official:linux", owners[ProviderCapability.Domain]);
        Assert.Equal("official:linux", owners[ProviderCapability.Media]);
    }

    [Fact]
    public void Order_decides()
    {
        Assert.Equal("official:linux", ProviderRouting.Route([Official(), User()])[ProviderCapability.Foreground]);
    }

    [Fact]
    public void Disabled_and_failed_plugins_are_skipped()
    {
        Assert.Equal("official:linux", ProviderRouting.Route([User(enabled: false), Official()])[ProviderCapability.Foreground]);
        Assert.Equal("official:linux", ProviderRouting.Route([User(healthy: false), Official()])[ProviderCapability.Foreground]);
        Assert.Empty(ProviderRouting.Route([Official(enabled: false)]));
    }

    [Fact]
    public void Only_the_owner_is_heard()
    {
        var router = new CapabilityRouter();
        router.Reroute([User(), Official()]);
        var fromUser = new ForegroundSignal("foot", null, "foot", "~", false, false);
        var fromOfficial = new ForegroundSignal("gnome-shell", null, "Shell", "", false, false);

        Assert.Equal(fromUser, router.Report("user:sway.sh", fromUser));
        Assert.Null(router.Report("official:linux", fromOfficial));
    }

    [Fact]
    public void A_capability_that_moves_brings_the_new_owners_last_fact()
    {
        var router = new CapabilityRouter();
        router.Reroute([User(), Official()]);
        var official = new ForegroundSignal("code", null, "Code", "main.rs", false, false);
        router.Report("official:linux", official);

        var moves = router.Reroute([User(enabled: false), Official()]);

        var move = Assert.Single(moves);
        Assert.Equal((ProviderCapability.Foreground, "official:linux", (ActivitySignal?)official), (move.Capability, move.Owner, move.Signal));
    }

    [Fact]
    public void A_capability_nobody_supplies_is_cleared()
    {
        var router = new CapabilityRouter();
        router.Reroute([Official()]);
        router.Report("official:linux", new MediaSignal(true, "song", null, "spotify"));
        router.Report("official:linux", new LockSignal(true));

        var moves = router.Reroute([Official(enabled: false)]).ToDictionary(m => m.Capability);

        Assert.Equal(new MediaSignal(false, null, null, null), moves[ProviderCapability.Media].Signal);
        Assert.Equal(new LockSignal(false), moves[ProviderCapability.Lock].Signal);
        Assert.Equal(new BrowserDomainSignal(null), moves[ProviderCapability.Domain].Signal);
        Assert.Null(moves[ProviderCapability.Foreground].Signal);
    }

    [Fact]
    public void Forgotten_facts_are_not_replayed()
    {
        var router = new CapabilityRouter();
        router.Reroute([Official()]);
        router.Report("official:linux", new IdleSignal(true));
        router.Reroute([Official(enabled: false)]);
        router.Forget("official:linux");

        var moves = router.Reroute([Official()]).ToDictionary(m => m.Capability);

        Assert.Equal(new IdleSignal(false), moves[ProviderCapability.Idle].Signal);
    }
}
