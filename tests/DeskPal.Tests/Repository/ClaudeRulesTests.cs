using System.Text.RegularExpressions;

namespace DeskPal.Tests.Repository;

public class ClaudeRulesTests
{
    [Fact]
    public void Claude_rules_match_cursor_rules()
    {
        var root = FindRoot();
        var source = Path.Combine(root, ".cursor", "rules");
        var target = Path.Combine(root, ".claude", "rules");

        var expected = Directory.GetFiles(source, "*.mdc")
            .ToDictionary(f => Path.ChangeExtension(Path.GetFileName(f), ".md"), Convert);
        var actual = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.md").ToDictionary(f => Path.GetFileName(f), Read)
            : [];

        var stale = expected.Keys.Union(actual.Keys)
            .Where(name => !expected.TryGetValue(name, out var e) || !actual.TryGetValue(name, out var a) || e != a)
            .Order()
            .ToList();

        Assert.True(stale.Count == 0,
            $".claude/rules 和 .cursor/rules 不一致：{string.Join(", ", stale)}。在本地编译一次 tests/DeskPal.Tests 会重新生成，再提交 .claude/rules。");
    }

    private static string Convert(string mdcPath)
    {
        var text = Read(mdcPath);
        var match = Regex.Match(text, @"\A---\n(.*?)\n---\n", RegexOptions.Singleline);
        Assert.True(match.Success, $"{Path.GetFileName(mdcPath)} 没有 frontmatter");

        var globs = match.Groups[1].Value.Split('\n')
            .Select(line => Regex.Match(line, @"^globs:\s*(.*)$"))
            .Where(m => m.Success)
            .SelectMany(m => m.Groups[1].Value.Split(','))
            .Select(g => g.Trim())
            .Where(g => g.Length > 0)
            .ToList();

        var header = globs.Count > 0
            ? "---\npaths:\n" + string.Concat(globs.Select(g => $"  - \"{g}\"\n")) + "---\n"
            : "";
        return header
            + $"<!-- Generated from .cursor/rules/{Path.GetFileName(mdcPath)} by DeskPal.Tests build. Do not edit. -->\n"
            + text[match.Length..];
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeskPal.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}