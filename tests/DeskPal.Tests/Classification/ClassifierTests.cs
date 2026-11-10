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
        Assert.Equal(expected, Create().Classify(TestData.Browser(domain)).Category);
    }

    [Fact]
    public void Title_keyword_applies_to_browsers_only()
    {
        var classifier = Create();

        Assert.Equal(ActivityCategory.Shopping, classifier.Classify(TestData.Browser(null, "【淘宝】双十一 - Google Chrome")).Category);
        Assert.Equal(ActivityCategory.Other, classifier.Classify(TestData.App("notepad2", "淘宝订单.txt")).Category);
    }

    [Fact]
    public void Personal_override_beats_room_rule_and_room_rule_beats_builtin()
    {
        var classifier = Create();
        var snapshot = TestData.Browser("bilibili.com");

        classifier.SetRoom(new RuleSet([new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Work, "B 站学习")]));
        Assert.Equal((ActivityCategory.Work, RuleLayer.Room), (classifier.Classify(snapshot).Category, classifier.Classify(snapshot).Layer));

        classifier.SetPersonal(new RuleSet([new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Reading)]));
        Assert.Equal((ActivityCategory.Reading, RuleLayer.Personal), (classifier.Classify(snapshot).Category, classifier.Classify(snapshot).Layer));
    }

    [Fact]
    public void Steam_library_path_is_a_game()
    {
        var result = Create().Classify(TestData.App("SomeIndieGame", path: @"D:\Steam\steamapps\common\Indie\game.exe"));

        Assert.Equal((ActivityCategory.Game, RuleLayer.Heuristic), (result.Category, result.Layer));
    }

    [Fact]
    public void Unknown_browser_site_in_fullscreen_is_video()
    {
        Assert.Equal(ActivityCategory.Video, Create().Classify(TestData.Browser("unknown-site.cn", fullscreen: true)).Category);
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