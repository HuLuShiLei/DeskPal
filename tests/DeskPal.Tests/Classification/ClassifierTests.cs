using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Classification;

public class ClassifierTests
{
    private static readonly RuleSet Builtin = new(
    [
        new RuleDto(RuleKind.Process, "devenv.exe", ActivityCategory.Work, "Visual Studio"),
        new RuleDto(RuleKind.Process, "cloudmusic", ActivityCategory.Music, "网易云音乐"),
        new RuleDto(RuleKind.Domain, "amazon.com", ActivityCategory.Shopping, "Amazon"),
        new RuleDto(RuleKind.Domain, "aws.amazon.com", ActivityCategory.Work, "AWS"),
        new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Video, "哔哩哔哩"),
        new RuleDto(RuleKind.TitleKeyword, "淘宝", ActivityCategory.Shopping, "淘宝"),
    ]);

    private static LayeredClassifier Create()
    {
        var classifier = new LayeredClassifier();
        classifier.SetBuiltin(Builtin);
        return classifier;
    }

    [Fact]
    public void Process_rule_matches_without_exe_suffix_and_case()
    {
        var result = Create().Classify(TestData.App("DevEnv"));

        Assert.Equal(ActivityCategory.Work, result.Category);
        Assert.Equal("Visual Studio", result.AppDisplayName);
        Assert.Equal(RuleLayer.Builtin, result.Layer);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1", ActivityCategory.Video)]
    [InlineData("live.bilibili.com", ActivityCategory.Video)]
    [InlineData("console.aws.amazon.com", ActivityCategory.Work)]
    [InlineData("www.amazon.com", ActivityCategory.Shopping)]
    public void Most_specific_domain_wins(string domain, ActivityCategory expected)
    {
        Assert.Equal(expected, Builtin.Match(TestData.Browser(domain))!.Category);
    }

    [Fact]
    public void Title_keyword_matches_browser_titles_only_inside_a_rule_set()
    {
        Assert.Equal(ActivityCategory.Shopping, Builtin.Match(TestData.Browser(null, "【淘宝】双十一 - Google Chrome"))!.Category);
        Assert.Null(Builtin.Match(TestData.App("notepad2", "淘宝订单.txt")));
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1", "bilibili.com")]
    [InlineData("live.bilibili.com", "live.bilibili.com")]
    [InlineData("console.aws.amazon.com", "console.aws.amazon.com")]
    [InlineData("www.amazon.com", "amazon.com")]
    public void Browser_is_browsing_with_the_domain(string domain, string expectedName)
    {
        var result = Create().Classify(TestData.Browser(domain, "页面 - Google Chrome"));

        Assert.Equal(ActivityCategory.Browsing, result.Category);
        Assert.Equal(expectedName, result.AppDisplayName);
        Assert.Equal(RuleLayer.Browser, result.Layer);
    }

    [Fact]
    public void Browser_ignores_builtin_title_fullscreen_and_media()
    {
        var classifier = Create();
        var titled = classifier.Classify(TestData.Browser(null, "【淘宝】双十一 - Google Chrome"));
        Assert.Equal((ActivityCategory.Browsing, (string?)null, RuleLayer.Browser), (titled.Category, titled.AppDisplayName, titled.Layer));

        var fullscreen = classifier.Classify(TestData.Browser("unknown-site.cn", "未知 - Google Chrome", fullscreen: true)
            .Apply(new MediaSignal(true, "晴天", "周杰伦", "chrome")));
        Assert.Equal((ActivityCategory.Browsing, "unknown-site.cn", RuleLayer.Browser), (fullscreen.Category, fullscreen.AppDisplayName, fullscreen.Layer));
    }

    [Fact]
    public void Room_rule_beats_personal_rule()
    {
        var classifier = Create();
        var snapshot = TestData.Browser("bilibili.com");
        classifier.SetPersonal(new RuleSet([new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Reading)]));
        var personal = classifier.Classify(snapshot);
        Assert.Equal((ActivityCategory.Reading, "bilibili.com", RuleLayer.Personal), (personal.Category, personal.AppDisplayName, personal.Layer));

        classifier.SetRoom(new RuleSet([new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Work, "B 站学习")]));
        var room = classifier.Classify(snapshot);
        Assert.Equal((ActivityCategory.Work, "B 站学习", RuleLayer.Room), (room.Category, room.AppDisplayName, room.Layer));
    }

    [Fact]
    public void Room_process_rule_beats_personal_and_builtin()
    {
        var classifier = Create();
        var snapshot = TestData.App("DevEnv");
        classifier.SetPersonal(new RuleSet([new RuleDto(RuleKind.Process, "devenv.exe", ActivityCategory.Game, "VS")]));
        Assert.Equal((ActivityCategory.Game, "VS", RuleLayer.Personal), (classifier.Classify(snapshot).Category, classifier.Classify(snapshot).AppDisplayName, classifier.Classify(snapshot).Layer));

        classifier.SetRoom(new RuleSet([new RuleDto(RuleKind.Process, "devenv.exe", ActivityCategory.Work, "Visual Studio")]));
        var room = classifier.Classify(snapshot);
        Assert.Equal((ActivityCategory.Work, "Visual Studio", RuleLayer.Room), (room.Category, room.AppDisplayName, room.Layer));
    }

    [Fact]
    public void Steam_library_path_is_a_game()
    {
        var result = Create().Classify(TestData.App("SomeIndieGame", path: @"D:\Steam\steamapps\common\Indie\game.exe"));

        Assert.Equal((ActivityCategory.Game, RuleLayer.Heuristic), (result.Category, result.Layer));
    }

    [Fact]
    public void Fullscreen_media_app_is_video()
    {
        var snapshot = TestData.App("player", fullscreen: true).Apply(new MediaSignal(true, "电影", null, "player"));

        Assert.Equal(ActivityCategory.Video, Create().Classify(snapshot).Category);
    }

    [Fact]
    public void Unknown_app_with_media_playing_is_music()
    {
        var snapshot = TestData.App("unknownplayer").Apply(new MediaSignal(true, "晴天", "周杰伦", "unknownplayer"));

        Assert.Equal(ActivityCategory.Music, Create().Classify(snapshot).Category);
    }

    [Fact]
    public void Unknown_app_falls_back_to_other_with_display_name()
    {
        var result = Create().Classify(TestData.App("mystery"));

        Assert.Equal((ActivityCategory.Other, "mystery", RuleLayer.Fallback), (result.Category, result.AppDisplayName, result.Layer));
    }

    [Fact]
    public void Correction_target_prefers_domain_for_browsers()
    {
        Assert.Equal(RuleKind.Domain, CorrectionTarget.From(TestData.Browser("www.zhihu.com"), null)!.Kind);
        Assert.Equal("zhihu.com", CorrectionTarget.From(TestData.Browser("www.zhihu.com"), null)!.Pattern);
        Assert.Equal(RuleKind.Process, CorrectionTarget.From(TestData.Browser(null), null)!.Kind);
        Assert.Equal("devenv", CorrectionTarget.From(TestData.App("devenv.exe"), null)!.Pattern);
    }

    [Fact]
    public void Switching_to_another_app_clears_browser_domain()
    {
        var snapshot = TestData.Browser("bilibili.com").Apply(new ForegroundSignal("devenv", null, "VS", "x", false, false));

        Assert.Null(snapshot.BrowserDomain);
    }
}
