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
    [InlineData("com.tencent.WeWorkMac", ActivityCategory.Work)]
    [InlineData("com.tinyspeck.slackmacgap", ActivityCategory.Work)]
    [InlineData("us.zoom.xos", ActivityCategory.Work)]
    [InlineData("com.tencent.xinWeChat", ActivityCategory.Communication)]
    [InlineData("com.tencent.qq", ActivityCategory.Communication)]
    public void Office_chat_is_work_and_personal_chat_is_chatting(string process, ActivityCategory expected)
    {
        Assert.Equal(expected, Rules.Match(TestData.App(process))!.Category);
    }

    [Theory]
    [InlineData("ffxiv_dx11", ActivityCategory.Game)]
    [InlineData("steam", ActivityCategory.Other)]
    [InlineData("steamwebhelper", ActivityCategory.Other)]
    [InlineData("PotPlayer64", ActivityCategory.Video)]
    [InlineData("Plex", ActivityCategory.Video)]
    public void Launchers_are_other_and_games_and_players_are_known(string process, ActivityCategory expected)
    {
        Assert.Equal(expected, Rules.Match(TestData.App(process))!.Category);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("chatgpt")]
    [InlineData("opencode")]
    [InlineData("workbuddy")]
    public void Coding_agents_are_work(string process)
    {
        Assert.Equal(ActivityCategory.Work, Rules.Match(TestData.App(process))!.Category);
    }

    [Theory]
    [InlineData("sunloginclient")]
    [InlineData("mstsc")]
    [InlineData("vmware")]
    [InlineData("mumuplayer")]
    [InlineData("explorer")]
    [InlineData("obsidian")]
    [InlineData("doubao")]
    [InlineData("foxmail")]
    public void Apps_used_both_for_work_and_daily_life_are_other(string process)
    {
        var rule = Rules.Match(TestData.App(process))!;
        Assert.Equal(ActivityCategory.Other, rule.Category);
        Assert.NotNull(rule.Name);
    }

    [Theory]
    [InlineData("outlook.office.com", ActivityCategory.Work)]
    [InlineData("teams.microsoft.com", ActivityCategory.Work)]
    [InlineData("meeting.tencent.com", ActivityCategory.Work)]
    [InlineData("claude.ai", ActivityCategory.Work)]
    [InlineData("chatgpt.com", ActivityCategory.Work)]
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

    /// <summary>Format checks that used to live in validate-rules.ps1; the rule-candidates workflow runs this class too.</summary>
    [Fact]
    public void Rule_file_follows_the_format()
    {
        var problems = new List<string>();
        if (File.Version < 1) problems.Add("version must be a positive integer");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in File.Rules)
        {
            var label = $"[{r.Kind}] {r.Pattern}";
            if (!Enum.IsDefined(r.Kind)) problems.Add($"{label}: invalid kind");
            if (!Enum.IsDefined(r.Category)) problems.Add($"{label}: category is not an ActivityCategory member");
            if (string.IsNullOrWhiteSpace(r.Name)) problems.Add($"{label}: missing name");
            if (r.Kind != RuleKind.TitleKeyword && r.Pattern != r.Pattern.Trim().ToLowerInvariant()) problems.Add($"{label}: must be lower case without surrounding spaces");
            if (r.Kind == RuleKind.Process && r.Pattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) problems.Add($"{label}: drop the .exe suffix");
            if (r.Kind == RuleKind.Process && Browsers.IsBrowser(r.Pattern)) problems.Add($"{label}: browsers are classified by domain, not by process");
            if (r.Kind == RuleKind.Domain && (r.Pattern.StartsWith("http", StringComparison.Ordinal) || r.Pattern.StartsWith("www.", StringComparison.Ordinal) || r.Pattern.IndexOfAny(['/', ':']) >= 0))
                problems.Add($"{label}: host only, no scheme, www., path or port");
            foreach (var (language, name) in r.Names ?? new Dictionary<string, string>())
            {
                if (language != "en") problems.Add($"{label}: names.{language} is not a UI language (en)");
                if (string.IsNullOrWhiteSpace(name)) problems.Add($"{label}: names.{language} is empty");
            }

            if (!seen.Add($"{r.Kind}|{r.Pattern.Trim().ToLowerInvariant()}")) problems.Add($"{label}: duplicate");
        }

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in File.Folders ?? [])
        {
            var label = $"[Folder] {f.Folder}";
            if (string.IsNullOrWhiteSpace(f.Folder) || !f.Folder.StartsWith('\\')) problems.Add($"{label}: must start with \\ (write / as \\)");
            else if (f.Folder.Contains('/')) problems.Add($"{label}: write / as \\");
            if (!Enum.IsDefined(f.Category)) problems.Add($"{label}: category is not an ActivityCategory member");
            if (!folders.Add(f.Folder)) problems.Add($"{label}: duplicate");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
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
