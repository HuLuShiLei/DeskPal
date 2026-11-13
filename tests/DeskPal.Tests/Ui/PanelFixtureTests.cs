using System.Text.Json;
using DeskPal.Client.Core.Ui;
using DeskPal.Shared;

namespace DeskPal.Tests.Ui;

/// <summary>
/// The browser mock (web/src/mock/fixtures) must look exactly like what C# sends.
/// After changing PanelState or the panel text, run the tests once with DESKPAL_UPDATE_FIXTURES=1 to rewrite them.
/// </summary>
public class PanelFixtureTests
{
    private static readonly JsonSerializerOptions Options = new(DeskPalJson.Options)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TheoryData<string> Names => new(PanelSamples.All.Keys);

    [Theory]
    [MemberData(nameof(Names))]
    public void Mock_fixture_matches_csharp_output(string name)
    {
        Check(name, PanelStateBuilder.Build(PanelSamples.All[name]()));
    }

    [Fact]
    public void Stats_mock_fixture_matches_csharp_output() => Check("stats", StatsSamples.Views());

    private static void Check(string name, object value)
    {
        var json = JsonSerializer.Serialize(value, Options).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(RepoRoot(), "src", "DeskPal.Client", "web", "src", "mock", "fixtures", name + ".json");

        if (Environment.GetEnvironmentVariable("DESKPAL_UPDATE_FIXTURES") == "1")
        {
            File.WriteAllText(path, json);
            return;
        }

        Assert.True(File.Exists(path), $"缺少 {path}。用 DESKPAL_UPDATE_FIXTURES=1 运行测试生成。");
        Assert.True(
            File.ReadAllText(path).ReplaceLineEndings("\n") == json,
            $"{name}.json 和 C# 输出不一致。用 DESKPAL_UPDATE_FIXTURES=1 运行测试更新。");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeskPal.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }
}