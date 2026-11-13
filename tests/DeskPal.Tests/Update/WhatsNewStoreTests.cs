using DeskPal.Client.Core.Updates;

namespace DeskPal.Tests.Update;

public sealed class WhatsNewStoreTests : IDisposable
{
    private const string V1 = "## 0.1.1\n\n- 关闭时可以选择最小化\n";
    private const string V2 = "## 0.1.2\n\n- 更新后显示更新内容\n- 面板可以调整大小\n\n" + V1;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deskpal-tests", Guid.NewGuid().ToString("N"));

    private string SeenPath => Path.Combine(_dir, "changelog-seen.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void First_run_only_records_what_is_there()
    {
        Assert.Null(new WhatsNewStore(V2, SeenPath, "0.1.2").Pending);
        Assert.True(File.Exists(SeenPath));
        Assert.Null(new WhatsNewStore(V2, SeenPath, "0.1.2").Pending);
    }

    [Fact]
    public void After_an_update_shows_the_new_items_until_acknowledged()
    {
        _ = new WhatsNewStore(V1, SeenPath, "0.1.1");

        var store = new WhatsNewStore(V2, SeenPath, "0.1.2-dev.3");
        Assert.Equal("相较于 0.1.1，共 2 项更新", store.Pending!.Summary);

        Assert.NotNull(new WhatsNewStore(V2, SeenPath, "0.1.2-dev.3").Pending);

        store.Acknowledge();
        Assert.Null(store.Pending);
        Assert.Null(new WhatsNewStore(V2, SeenPath, "0.1.2-dev.3").Pending);
    }

    [Fact]
    public void Test_builds_of_the_same_version_show_only_items_added_since()
    {
        _ = new WhatsNewStore("## 0.1.2\n\n- 更新后显示更新内容\n\n" + V1, SeenPath, "0.1.2-dev.3");

        var store = new WhatsNewStore(V2, SeenPath, "0.1.2-dev.5");

        Assert.Equal("相较于 0.1.2-dev.3，共 1 项更新", store.Pending!.Summary);
        Assert.Equal(["面板可以调整大小"], store.Pending.Sections.Single().Items);
    }

    [Fact]
    public void Going_back_to_a_lower_version_shows_nothing_and_keeps_what_was_seen()
    {
        _ = new WhatsNewStore(V2, SeenPath, "0.1.2-dev.5");

        Assert.Null(new WhatsNewStore(V1, SeenPath, "0.1.1").Pending);
        Assert.Null(new WhatsNewStore(V2, SeenPath, "0.1.2-dev.5").Pending);
    }

    [Fact]
    public void Missing_changelog_or_broken_record_does_not_show_anything()
    {
        Assert.Null(new WhatsNewStore(null, SeenPath, "0.1.2").Pending);
        Assert.False(File.Exists(SeenPath));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(SeenPath, "{not json");
        Assert.Null(new WhatsNewStore(V2, SeenPath, "0.1.2").Pending);
        Assert.Null(new WhatsNewStore(V2, SeenPath, "0.1.2").Pending);
    }
}
