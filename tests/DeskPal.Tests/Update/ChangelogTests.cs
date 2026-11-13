using DeskPal.Client.Core.Updates;

namespace DeskPal.Tests.Update;

public class ChangelogTests
{
    private const string Markdown = """
        # 更新日志

        推送时整理，写给用户看。

        ## 0.1.2

        - 更新后显示更新内容
        - 面板可以拖动边框调整大小，
          最小 400×480
        * 设置里可以修改显示名称

        ## v0.1.1（2026-11-12）

        - 点关闭时可以选择最小化

        ## 0.1.0

        """;

    [Fact]
    public void Parses_sections_items_and_continuation_lines()
    {
        var sections = Changelog.Parse(Markdown.ReplaceLineEndings("\r\n"));

        Assert.Equal(["0.1.2", "0.1.1"], sections.Select(s => s.Version));
        Assert.Equal(["更新后显示更新内容", "面板可以拖动边框调整大小， 最小 400×480", "设置里可以修改显示名称"], sections[0].Items);
        Assert.Equal(["点关闭时可以选择最小化"], sections[1].Items);
    }

    [Fact]
    public void Shows_only_unseen_items_grouped_by_version_and_counts_them()
    {
        var sections = Changelog.Parse(Markdown);

        var view = Changelog.WhatsNew(sections, new HashSet<string> { "点关闭时可以选择最小化", "设置里可以修改显示名称" }, "0.1.1", "0.1.2-dev.18");

        Assert.NotNull(view);
        Assert.Equal("已更新到 0.1.2-dev.18", view.Title);
        Assert.Equal("相较于 0.1.1，共 2 项更新", view.Summary);
        var section = Assert.Single(view.Sections);
        Assert.Equal("0.1.2", section.Version);
        Assert.Equal(2, section.Items.Count);
    }

    [Fact]
    public void Nothing_new_returns_null()
    {
        var sections = Changelog.Parse(Markdown);

        Assert.Null(Changelog.WhatsNew(sections, Changelog.AllItems(sections).ToHashSet(), "0.1.2-dev.20", "0.1.1"));
    }

    [Fact]
    public void Same_version_or_unknown_previous_omits_the_comparison()
    {
        var sections = Changelog.Parse(Markdown);

        Assert.Equal("共 4 项更新", Changelog.WhatsNew(sections, new HashSet<string>(), null, "0.1.2")!.Summary);
        Assert.Equal("共 4 项更新", Changelog.WhatsNew(sections, new HashSet<string>(), "0.1.2", "0.1.2")!.Summary);
    }

    [Fact]
    public void An_item_listed_twice_is_shown_once()
    {
        var sections = new[]
        {
            new ChangelogSection("0.1.2", ["面板可以调整大小"]),
            new ChangelogSection("0.1.1", ["面板可以调整大小", "关闭时可以选择"]),
        };

        var view = Changelog.WhatsNew(sections, new HashSet<string>(), "0.1.0", "0.1.2")!;

        Assert.Equal("相较于 0.1.0，共 2 项更新", view.Summary);
        Assert.Equal(["0.1.2", "0.1.1"], view.Sections.Select(s => s.Version));
    }
}
