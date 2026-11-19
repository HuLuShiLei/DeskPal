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
    public void Personal_rule_reclassifies_a_site()
    {
        var classifier = Create();
        classifier.SetPersonal(new RuleSet([new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Work, "B 站学习")]));

        var result = classifier.Classify(TestData.Browser("bilibili.com"));

        Assert.Equal((ActivityCategory.Work, "B 站学习", RuleLayer.Personal), (result.Category, result.AppDisplayName, result.Layer));
    }

    [Fact]
    public void Personal_rule_beats_builtin()
    {
        var classifier = Create();
        var snapshot = TestData.App("DevEnv");
        Assert.Equal((ActivityCategory.Work, RuleLayer.Builtin), (classifier.Classify(snapshot).Category, classifier.Classify(snapshot).Layer));

        classifier.SetPersonal(PersonalRuleStore.ToRuleSet(
        [
            new UserRuleDto(RuleKind.Process, "devenv", ActivityCategory.Work, "旧的", DateTimeOffset.UnixEpoch),
            new UserRuleDto(RuleKind.Process, "DevEnv.exe", ActivityCategory.Game, "VS", DateTimeOffset.UnixEpoch.AddDays(1)),
        ]));
        var personal = classifier.Classify(snapshot);
        Assert.Equal((ActivityCategory.Game, "VS", RuleLayer.Personal), (personal.Category, personal.AppDisplayName, personal.Layer));
    }

    [Fact]
    public void Steam_library_path_is_a_game()
    {
        var result = Create().Classify(TestData.App("SomeIndieGame", path: @"D:\Steam\steamapps\common\Indie\game.exe"));

        Assert.Equal((ActivityCategory.Game, RuleLayer.Heuristic), (result.Category, result.Layer));
    }

    [Theory]
    [InlineData("/Users/me/Library/Application Support/Steam/steamapps/common/Indie/Indie.app/Contents/MacOS/Indie")]
    [InlineData("/Users/Shared/Epic Games/Fortnite/FortniteGame/Binaries/Mac/FortniteClient-Mac-Shipping.app/Contents/MacOS/FortniteClient-Mac-Shipping")]
    public void Mac_game_library_paths_are_games(string path)
    {
        var result = Create().Classify(TestData.App("com.example.indie", path: path));

        Assert.Equal((ActivityCategory.Game, RuleLayer.Heuristic), (result.Category, result.Layer));
    }

    [Fact]
    public void Mac_launcher_next_to_games_is_not_a_game()
    {
        const string path = "/Users/me/Library/Application Support/Steam/steamapps/common/Indie/UnityCrashHandler.app/Contents/MacOS/UnityCrashHandler";

        Assert.Equal(ActivityCategory.Other, Create().Classify(TestData.App("com.unity3d.crashhandler", path: path)).Category);
    }

    [Theory]
    [InlineData("com.apple.Safari")]
    [InlineData("com.google.Chrome")]
    [InlineData("com.microsoft.edgemac")]
    [InlineData("org.mozilla.firefox")]
    [InlineData("company.thebrowser.Browser")]
    public void Mac_browsers_are_recognized_by_bundle_identifier(string bundleId) =>
        Assert.True(Browsers.IsBrowser(bundleId));

    [Fact]
    public void Mac_bundle_identifier_matches_a_process_rule_ignoring_case()
    {
        var classifier = Create();
        classifier.SetBuiltin(new RuleSet([new RuleDto(RuleKind.Process, "com.tencent.xinWeChat", ActivityCategory.Communication, "微信")]));

        var result = classifier.Classify(TestData.App("com.tencent.xinWeChat"));

        Assert.Equal((ActivityCategory.Communication, "微信", RuleLayer.Builtin), (result.Category, result.AppDisplayName, result.Layer));
    }

    [Fact]
    public void Fullscreen_media_app_is_video()
    {
        var snapshot = TestData.App("player", fullscreen: true).Apply(new MediaSignal(true, "电影", null, "player"));

        Assert.Equal(ActivityCategory.Video, Create().Classify(snapshot).Category);
    }

    [Fact]
    public void Background_media_does_not_change_the_focused_app()
    {
        var snapshot = TestData.App("unknownplayer").Apply(new MediaSignal(true, "晴天", "周杰伦", "unknownplayer"));

        Assert.Equal(ActivityCategory.Other, Create().Classify(snapshot).Category);
    }

    [Fact]
    public void Retired_rules_fall_through_to_browsing_or_other()
    {
        Assert.Equal(ActivityCategory.Other, Create().Classify(TestData.App("cloudmusic")).Category);

        var classifier = Create();
        classifier.SetPersonal(new RuleSet([new RuleDto(RuleKind.Domain, "amazon.com", ActivityCategory.Shopping, "Amazon")]));
        var shop = classifier.Classify(TestData.Browser("www.amazon.com"));
        Assert.Equal((ActivityCategory.Browsing, "amazon.com", RuleLayer.Browser), (shop.Category, shop.AppDisplayName, shop.Layer));

        classifier.SetPersonal(new RuleSet([new RuleDto(RuleKind.Process, "kindle", ActivityCategory.Reading, "Kindle")]));
        var reading = classifier.Classify(TestData.App("kindle"));
        Assert.Equal((ActivityCategory.Other, "kindle", RuleLayer.Fallback), (reading.Category, reading.AppDisplayName, reading.Layer));
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
    public void Browser_playing_the_video_of_its_tab_is_video()
    {
        var watching = TestData.Browser("www.bilibili.com", "(3) 猫猫合集_哔哩哔哩_bilibili - Google Chrome", fullscreen: true)
            .Apply(new MediaSignal(true, "猫猫合集", "UP 主", "Chrome"));
        var result = Create().Classify(watching);
        Assert.Equal((ActivityCategory.Video, "bilibili.com", RuleLayer.Browser), (result.Category, result.AppDisplayName, result.Layer));

        var otherTab = TestData.Browser("github.com", "PR #1 - Google Chrome").Apply(new MediaSignal(true, "猫猫合集", null, "Chrome"));
        Assert.Equal(ActivityCategory.Browsing, Create().Classify(otherTab).Category);

        var otherBrowser = TestData.Browser("www.bilibili.com", "猫猫合集_哔哩哔哩_bilibili - Google Chrome").Apply(new MediaSignal(true, "猫猫合集", null, "MSEdge"));
        Assert.Equal(ActivityCategory.Browsing, Create().Classify(otherBrowser).Category);
    }

    [Fact]
    public void Fullscreen_game_with_a_video_in_the_background_is_not_video()
    {
        var snapshot = TestData.App("ffxiv_dx11", "最终幻想XIV", @"D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\ffxiv_dx11.exe", fullscreen: true)
            .Apply(new MediaSignal(true, "猫猫合集", null, "MSEdge"));

        Assert.Equal((ActivityCategory.Game, RuleLayer.Heuristic), (Create().Classify(snapshot).Category, Create().Classify(snapshot).Layer));
        Assert.Equal(ActivityCategory.Other, Create().Classify(TestData.App("mystery", fullscreen: true).Apply(new MediaSignal(true, "猫猫合集", null, "MSEdge"))).Category);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe")]
    [InlineData(@"D:\WeGameApps\英雄联盟\Game\League of Legends.exe")]
    [InlineData(@"C:\XboxGames\Forza Horizon 5\Content\ForzaHorizon5.exe")]
    [InlineData(@"D:\miHoYo\Genshin Impact\Genshin Impact Game\YuanShen.exe")]
    public void Game_library_paths_are_games(string path)
    {
        Assert.Equal(ActivityCategory.Game, Create().Classify(TestData.App(Path.GetFileNameWithoutExtension(path), path: path)).Category);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicWebHelper.exe")]
    [InlineData(@"D:\miHoYo Launcher\launcher.exe")]
    [InlineData(@"D:\Steam\steamapps\common\Indie\UnityCrashHandler64.exe")]
    public void Launchers_next_to_games_are_not_games(string path)
    {
        Assert.Equal(ActivityCategory.Other, Create().Classify(TestData.App(Path.GetFileNameWithoutExtension(path), path: path)).Category);
    }

    [Fact]
    public void Personal_result_keeps_the_category_it_replaced()
    {
        var classifier = Create();
        classifier.SetPersonal(new RuleSet(
        [
            new RuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Work, null),
            new RuleDto(RuleKind.Process, "devenv", ActivityCategory.Game, null),
        ]));

        Assert.Equal((ActivityCategory.Work, ActivityCategory.Browsing), (classifier.Classify(TestData.Browser("bilibili.com")).Category, classifier.Classify(TestData.Browser("bilibili.com")).Default));
        Assert.Equal((ActivityCategory.Game, ActivityCategory.Work), (classifier.Classify(TestData.App("devenv")).Category, classifier.Classify(TestData.App("devenv")).Default));
        Assert.Equal(ActivityCategory.Other, Create().Classify(TestData.App("mystery")).Default);
    }

    [Theory]
    [InlineData("MSEdge", "msedge", true)]
    [InlineData("Chrome", "chrome", true)]
    [InlineData("308046B0AF4A39CB", "firefox", true)]
    [InlineData("Spotify.exe", "Spotify", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "spotify", true)]
    [InlineData("MSEdge", "chrome", false)]
    [InlineData("308046B0AF4A39CB", "chrome", false)]
    [InlineData(null, "chrome", false)]
    public void Media_session_is_matched_to_its_process(string? sourceApp, string process, bool expected)
    {
        Assert.Equal(expected, MediaSources.IsFrom(sourceApp, process));
    }

    [Fact]
    public void Browser_address_is_pending_until_read_after_a_title_change()
    {
        var snapshot = TestData.Browser("github.com", "PR #1 - Google Chrome");
        Assert.False(snapshot.DomainPending);

        var switched = snapshot.Apply(new ForegroundSignal("chrome", null, "Google Chrome", "猫猫合集 - Google Chrome", false, true));
        Assert.Equal((true, "github.com"), (switched.DomainPending, switched.BrowserDomain));
        Assert.False(switched.Apply(new BrowserDomainSignal("bilibili.com")).DomainPending);
        Assert.False(switched.Apply(new ForegroundSignal("devenv", null, "VS", "x", false, false)).DomainPending);
    }

    [Fact]
    public void Switching_to_another_app_clears_browser_domain()
    {
        var snapshot = TestData.Browser("bilibili.com").Apply(new ForegroundSignal("devenv", null, "VS", "x", false, false));

        Assert.Null(snapshot.BrowserDomain);
    }
}
