using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Classification;

public class AppTitleRuleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private static LayeredClassifier Create(params UserRuleDto[] personal)
    {
        var classifier = new LayeredClassifier();
        classifier.SetBuiltin(new RuleSet([new RuleDto(RuleKind.Process, "qq", ActivityCategory.Communication, "QQ")]));
        classifier.SetPersonal(PersonalRuleStore.ToRuleSet(personal));
        return classifier;
    }

    [Theory]
    [InlineData(" QQ.exe / 项目组 ", "qq/项目组")]
    [InlineData("qq/a/b", "qq/a/b")]
    [InlineData("qq/", "")]
    [InlineData("/项目组", "")]
    [InlineData("qq", "")]
    public void Pattern_is_the_process_and_the_keyword(string pattern, string expected)
    {
        Assert.Equal(expected, RulePattern.Normalize(RuleKind.AppTitle, pattern));
    }

    [Fact]
    public void Split_gives_back_both_parts()
    {
        Assert.Equal(("qq", "a/b"), RulePattern.SplitAppTitle("QQ/A/B"));
        Assert.Null(RulePattern.SplitAppTitle("qq"));
        Assert.Equal("weixin/项目组", RulePattern.AppTitle("Weixin.exe", "项目组"));
    }

    [Fact]
    public void Titles_with_the_keyword_get_their_own_category_and_keep_the_app_name()
    {
        var classifier = Create(new UserRuleDto(RuleKind.AppTitle, "qq/项目组", ActivityCategory.Work, "QQ", Now));

        var team = classifier.Classify(TestData.App("QQ", "前端项目组"));
        Assert.Equal((ActivityCategory.Work, "QQ", RuleLayer.Personal, ActivityCategory.Communication), (team.Category, team.AppDisplayName, team.Layer, team.BaseCategory));

        var friend = classifier.Classify(TestData.App("QQ", "张三"));
        Assert.Equal((ActivityCategory.Communication, RuleLayer.Builtin), (friend.Category, friend.Layer));
    }

    [Fact]
    public void A_title_rule_wins_over_the_rule_for_the_whole_app()
    {
        var classifier = Create(
            new UserRuleDto(RuleKind.Process, "qq", ActivityCategory.Other, "QQ", Now.AddDays(1)),
            new UserRuleDto(RuleKind.AppTitle, "qq/项目组", ActivityCategory.Work, "QQ", Now));

        Assert.Equal(ActivityCategory.Work, classifier.Classify(TestData.App("QQ", "项目组")).Category);
        Assert.Equal(ActivityCategory.Other, classifier.Classify(TestData.App("QQ", "张三")).Category);
        Assert.Equal(RuleKind.AppTitle, classifier.PersonalRule(TestData.App("QQ", "项目组"))!.Kind);
    }

    [Fact]
    public void Other_apps_and_browsers_ignore_it()
    {
        var classifier = Create(new UserRuleDto(RuleKind.AppTitle, "qq/项目组", ActivityCategory.Work, "QQ", Now));

        Assert.Equal(ActivityCategory.Other, classifier.Classify(TestData.App("notepad", "项目组.txt")).Category);
        Assert.Equal(ActivityCategory.Browsing, classifier.Classify(TestData.Browser("example.com", "项目组 - Google Chrome")).Category);
        Assert.Null(classifier.PersonalRule(TestData.App("notepad", "项目组.txt")));
    }

    [Fact]
    public void Recorded_usage_follows_the_title()
    {
        var reclassifier = new UsageReclassifier(
        [
            new UserRuleDto(RuleKind.AppTitle, "qq/项目组", ActivityCategory.Work, "QQ", Now),
        ]);

        Assert.Equal((PresenceState.Working, ActivityCategory.Work), reclassifier.Apply(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "项目组", "QQ", null));
        Assert.Equal((PresenceState.Communicating, ActivityCategory.Communication), reclassifier.Apply(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "张三", "QQ", null));
        // Kept without a title (a no-title rule) or without a process (older records): the name alone does not match.
        Assert.Equal((PresenceState.Communicating, ActivityCategory.Communication), reclassifier.Apply(PresenceState.Communicating, ActivityCategory.Communication, "QQ", null, "QQ", null));
        Assert.Equal((PresenceState.Communicating, ActivityCategory.Communication), reclassifier.Apply(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "项目组", null, null));
    }
}