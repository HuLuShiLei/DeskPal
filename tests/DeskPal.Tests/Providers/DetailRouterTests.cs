using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;

namespace DeskPal.Tests.Providers;

public class DetailRouterTests
{
    private static readonly ForegroundSignal Qq = new("QQ", null, "QQ", "QQ", false, IsBrowser: false);
    private static readonly ForegroundSignal Code = new("Code", null, "Visual Studio Code", "main.cs", false, IsBrowser: false);
    private static readonly ForegroundSignal Edge = new("msedge", null, "Microsoft Edge", "Inbox - Edge", false, IsBrowser: true);

    private static DetailEntry Entry(string id, params string[] apps) =>
        new(id, Enabled: true, Healthy: true, new HashSet<string>(apps, StringComparer.OrdinalIgnoreCase));

    private static DetailRouter Router(params DetailEntry[] entries)
    {
        var router = new DetailRouter();
        router.Update(entries);
        return router;
    }

    [Fact]
    public void Detail_replaces_the_title_of_a_covered_app()
    {
        var router = Router(Entry("user:qq.ps1", "qq"));
        router.Foreground(Qq);

        var published = router.Report("user:qq.ps1", new DetailSignal("QQ", "QQ", "张三"));

        Assert.Equal(Qq with { WindowTitle = "张三" }, published);
        Assert.Equal("user:qq.ps1", router.ActiveId);
    }

    [Fact]
    public void Other_apps_keep_their_own_title()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Qq);
        router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三"));

        Assert.Equal(Code, router.Foreground(Code));
        Assert.Null(router.ActiveId);
    }

    [Fact]
    public void Detail_comes_back_when_the_app_is_in_front_again()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Qq);
        router.Report("user:qq.ps1", new DetailSignal("QQ", "QQ", "张三"));
        router.Foreground(Code);

        Assert.Equal("张三", router.Foreground(Qq).WindowTitle);
    }

    [Fact]
    public void Detail_for_another_window_of_the_app_does_not_apply()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Qq);
        router.Report("user:qq.ps1", new DetailSignal("QQ", "QQ", "张三"));

        Assert.Equal("李四", router.Foreground(Qq with { WindowTitle = "李四" }).WindowTitle);
    }

    [Fact]
    public void Detail_reported_before_the_window_applies_when_it_arrives()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Code);

        Assert.Null(router.Report("user:qq.ps1", new DetailSignal("QQ", "QQ", "张三")));
        Assert.Equal("张三", router.Foreground(Qq).WindowTitle);
    }

    [Fact]
    public void Apps_the_plugin_did_not_name_are_left_alone()
    {
        var router = Router(Entry("user:qq.ps1", "WeChat"));
        router.Foreground(Qq);

        Assert.Null(router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三")));
        Assert.Null(router.ActiveId);
    }

    [Fact]
    public void Browsers_keep_their_title()
    {
        var router = Router(Entry("user:edge.ps1", "msedge"));
        router.Foreground(Edge);

        Assert.Null(router.Report("user:edge.ps1", new DetailSignal("msedge", null, "x")));
        Assert.Equal(Edge, router.Foreground(Edge));
    }

    [Fact]
    public void The_plugin_higher_in_the_list_wins()
    {
        var router = Router(Entry("user:a", "QQ"), Entry("user:b", "QQ"));
        router.Foreground(Qq);
        router.Report("user:b", new DetailSignal("QQ", null, "from b"));
        Assert.Equal("user:b", router.ActiveId);

        var published = router.Report("user:a", new DetailSignal("QQ", null, "from a"));

        Assert.Equal("from a", published?.WindowTitle);
        Assert.Equal("user:a", router.ActiveId);
    }

    [Fact]
    public void A_failed_or_disabled_plugin_falls_back_to_the_window_title()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Qq);
        router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三"));

        var failed = router.Update([Entry("user:qq.ps1", "QQ") with { Healthy = false }]);

        Assert.Equal(Qq, failed);
        Assert.Null(router.ActiveId);
        Assert.Equal("张三", router.Update([Entry("user:qq.ps1", "QQ")])?.WindowTitle);
        Assert.Equal(Qq, router.Update([Entry("user:qq.ps1", "QQ") with { Enabled = false }]));
    }

    [Fact]
    public void Taking_the_detail_back_or_forgetting_the_plugin_restores_the_title()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Qq);
        router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三"));

        Assert.Equal(Qq, router.Report("user:qq.ps1", new DetailSignal("QQ", null, null)));

        router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三"));
        Assert.Equal(Qq, router.Forget("user:qq.ps1"));
    }

    [Fact]
    public void Same_report_twice_publishes_once()
    {
        var router = Router(Entry("user:qq.ps1", "QQ"));
        router.Foreground(Qq);

        Assert.NotNull(router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三")));
        Assert.Null(router.Report("user:qq.ps1", new DetailSignal("QQ", null, "张三")));
    }
}