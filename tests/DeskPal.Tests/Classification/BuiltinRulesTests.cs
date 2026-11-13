using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Classification;

public class BuiltinRulesTests
{
    private static readonly RuleSetDto File = JsonSerializer.Deserialize<RuleSetDto>(
        System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Features", "Rules", "builtin-rules.json")),
        DeskPalJson.Options)!;

    private static readonly RuleSet Rules = new(File.Rules);

    [Theory]
    [InlineData("wxwork", ActivityCategory.Work)]
    [InlineData("dingtalk", ActivityCategory.Work)]
    [InlineData("feishu", ActivityCategory.Work)]
    [InlineData("ms-teams", ActivityCategory.Work)]
    [InlineData("outlook", ActivityCategory.Work)]
    [InlineData("zoom", ActivityCategory.Work)]
    [InlineData("wemeetapp", ActivityCategory.Work)]
    [InlineData("wechat", ActivityCategory.Communication)]
    [InlineData("qq", ActivityCategory.Communication)]
    [InlineData("telegram", ActivityCategory.Communication)]
    public void Office_chat_is_work_and_personal_chat_is_chatting(string process, ActivityCategory expected)
    {
        Assert.Equal(expected, Rules.Match(TestData.App(process))!.Category);
    }

    [Theory]
    [InlineData("mail.qq.com", ActivityCategory.Work)]
    [InlineData("teams.microsoft.com", ActivityCategory.Work)]
    [InlineData("meeting.tencent.com", ActivityCategory.Work)]
    [InlineData("wx.qq.com", ActivityCategory.Communication)]
    public void Web_versions_follow_their_desktop_apps(string domain, ActivityCategory expected)
    {
        Assert.Equal(expected, Rules.Match(TestData.Browser(domain))!.Category);
    }

    [Fact]
    public void Chinese_names_have_an_english_name()
    {
        var missing = File.Rules
            .Where(r => r.Name is not null && r.Name.Any(c => c is >= '\u3400' and <= '\u9fff') && r.Names?.GetValueOrDefault("en") is not { Length: > 0 })
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(missing);
        Assert.All(File.Rules.Where(r => r.Names is not null), r => Assert.All(r.Names!.Keys, k => Assert.Equal("en", k)));
    }

    [Fact]
    public void Meeting_and_retired_categories_are_not_in_the_builtin_rules()
    {
        Assert.True(File.Version >= 3);
        Assert.DoesNotContain(File.Rules, r => r.Category == ActivityCategory.Meeting || r.Category.IsRetired());
        Assert.Null(Rules.Match(TestData.App("discord")));
        Assert.Null(Rules.Match(TestData.App("kindle")));
        Assert.Null(Rules.Match(TestData.App("cloudmusic")));
    }
}
