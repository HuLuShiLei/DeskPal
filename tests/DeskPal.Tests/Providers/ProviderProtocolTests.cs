using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;

namespace DeskPal.Tests.Providers;

public class ProviderProtocolTests
{
    [Fact]
    public void Foreground_line_becomes_a_signal()
    {
        var result = ProviderProtocol.Parse("""{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"Inbox","fullscreen":true,"path":"/usr/lib/firefox/firefox"}""");

        var foreground = Assert.IsType<ProviderForeground>(result.Message);
        Assert.Equal(("firefox", "Firefox", "Inbox", true, "/usr/lib/firefox/firefox"),
            (foreground.Process, foreground.Name, foreground.Title, foreground.Fullscreen, foreground.Path));
        var signal = ProviderProtocol.ToSignal(foreground);
        Assert.Equal(("firefox", "Firefox", "Inbox", true, true),
            (signal.ProcessName, signal.AppDisplayName, signal.WindowTitle, signal.IsFullscreen, signal.IsBrowser));
    }

    [Fact]
    public void Only_process_is_required()
    {
        var foreground = Assert.IsType<ProviderForeground>(ProviderProtocol.Parse("""{"type":"foreground","process":"kitty"}""").Message);

        Assert.Equal(("kitty", null, "", false, null), (foreground.Process, foreground.Name, foreground.Title, foreground.Fullscreen, foreground.Path));
        Assert.Equal("kitty", ProviderProtocol.ToSignal(foreground).AppDisplayName);
        Assert.False(ProviderProtocol.ToSignal(foreground).IsBrowser);
    }

    [Theory]
    [InlineData("/usr/bin/code", "code")]
    [InlineData(@"C:\Games\Game.exe", "Game")]
    [InlineData("  msedge  ", "msedge")]
    public void Process_loses_its_folder_and_exe_suffix(string process, string expected)
    {
        var line = System.Text.Json.JsonSerializer.Serialize(new { type = "foreground", process });

        Assert.Equal(expected, Assert.IsType<ProviderForeground>(ProviderProtocol.Parse(line).Message).Process);
    }

    [Fact]
    public void Long_titles_are_cut_and_control_characters_removed()
    {
        var title = "a\u0007b\n" + new string('x', 2000);
        var line = System.Text.Json.JsonSerializer.Serialize(new { type = "foreground", process = "app", title });

        var parsed = Assert.IsType<ProviderForeground>(ProviderProtocol.Parse(line).Message);

        Assert.Equal(ProviderProtocol.MaxTitleLength, parsed.Title.Length);
        Assert.StartsWith("a b x", parsed.Title);
    }

    [Theory]
    [InlineData("""{"type":"domain","domain":"https://www.GitHub.com/a?b"}""", "github.com")]
    [InlineData("""{"type":"domain","domain":"localhost:5173"}""", "localhost")]
    [InlineData("""{"type":"domain","domain":null}""", null)]
    [InlineData("""{"type":"domain"}""", null)]
    [InlineData("""{"type":"domain","domain":"search words"}""", null)]
    [InlineData("""{"type":"domain","domain":"newtab"}""", null)]
    public void Domain_is_reduced_to_a_host(string line, string? expected) =>
        Assert.Equal(expected, Assert.IsType<ProviderDomain>(ProviderProtocol.Parse(line).Message).Domain);

    [Fact]
    public void Hello_names_the_plugin()
    {
        var hello = Assert.IsType<ProviderHello>(ProviderProtocol.Parse("""{"v":1,"type":"hello","name":"hyprland","version":"0.1"}""").Message);

        Assert.Equal(("hyprland", "0.1"), (hello.Name, hello.Version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"v":1,"type":"something-new","x":1}""")]
    public void Blank_lines_and_unknown_types_are_ignored(string line) =>
        Assert.Equal(default, ProviderProtocol.Parse(line));

    [Theory]
    [InlineData("hello")]
    [InlineData("[1,2]")]
    [InlineData("""{"process":"a"}""")]
    [InlineData("""{"v":2,"type":"foreground","process":"a"}""")]
    [InlineData("""{"v":"1","type":"foreground","process":"a"}""")]
    [InlineData("""{"type":"foreground"}""")]
    [InlineData("""{"type":"foreground","process":""}""")]
    [InlineData("""{"type":"foreground","process":"/"}""")]
    [InlineData("""{"type":"foreground","process":5}""")]
    [InlineData("""{"type":"foreground","process":"a","title":7}""")]
    [InlineData("""{"type":"foreground","process":"a","fullscreen":"yes"}""")]
    [InlineData("""{"type":"domain","domain":3}""")]
    [InlineData("""{"type":"hello"}""")]
    public void Invalid_lines_are_rejected_with_a_reason(string line)
    {
        var result = ProviderProtocol.Parse(line);

        Assert.Null(result.Message);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Overlong_lines_are_rejected()
    {
        var line = System.Text.Json.JsonSerializer.Serialize(new { type = "foreground", process = "a", title = new string('x', ProviderProtocol.MaxLineLength) });

        Assert.NotNull(ProviderProtocol.Parse(line).Error);
    }

    [Fact]
    public void Hello_lists_what_the_plugin_supplies_and_skips_unknown_names()
    {
        var hello = Assert.IsType<ProviderHello>(ProviderProtocol.Parse("""{"type":"hello","name":"sway","provides":["foreground","media","teleport"]}""").Message);

        Assert.Equal(new HashSet<ProviderCapability> { ProviderCapability.Foreground, ProviderCapability.Media }, hello.Provides);
    }

    [Fact]
    public void Hello_without_provides_leaves_it_to_the_default()
    {
        var hello = Assert.IsType<ProviderHello>(ProviderProtocol.Parse("""{"type":"hello","name":"x"}""").Message);

        Assert.Null(hello.Provides);
        Assert.Equal(new HashSet<ProviderCapability> { ProviderCapability.Foreground, ProviderCapability.Domain }, ProviderCapabilities.PluginDefault);
    }

    [Fact]
    public void Idle_lock_and_media_lines_are_read()
    {
        Assert.Equal(42.5, Assert.IsType<ProviderIdle>(ProviderProtocol.Parse("""{"type":"idle","seconds":42.5}""").Message).Seconds);
        Assert.True(Assert.IsType<ProviderLock>(ProviderProtocol.Parse("""{"type":"lock","locked":true}""").Message).Locked);

        var media = Assert.IsType<ProviderMedia>(ProviderProtocol.Parse("""{"type":"media","playing":true,"title":"Blue in Green","artist":"Miles Davis","source":"/usr/bin/spotify"}""").Message);
        Assert.Equal(new MediaSignal(true, "Blue in Green", "Miles Davis", "spotify"), ProviderProtocol.ToSignal(media));
    }

    [Fact]
    public void Stopped_media_carries_no_details()
    {
        var media = Assert.IsType<ProviderMedia>(ProviderProtocol.Parse("""{"type":"media","playing":false,"title":"left over"}""").Message);

        Assert.Equal(new MediaSignal(false, null, null, null), ProviderProtocol.ToSignal(media));
    }

    [Theory]
    [InlineData("""{"type":"idle"}""")]
    [InlineData("""{"type":"idle","seconds":-1}""")]
    [InlineData("""{"type":"idle","seconds":"10"}""")]
    [InlineData("""{"type":"lock","locked":"yes"}""")]
    [InlineData("""{"type":"media","title":"x"}""")]
    [InlineData("""{"type":"media","playing":true,"title":5}""")]
    [InlineData("""{"type":"hello","name":"x","provides":"foreground"}""")]
    public void Malformed_new_messages_are_rejected(string line)
    {
        Assert.NotNull(ProviderProtocol.Parse(line).Error);
    }

    [Fact]
    public void Hello_names_the_apps_a_detail_plugin_covers()
    {
        var hello = Assert.IsType<ProviderHello>(ProviderProtocol.Parse("""{"type":"hello","name":"qq","provides":["detail"],"apps":["QQ.exe","C:\\Apps\\WeChat.exe",5,""]}""").Message);

        Assert.Equal(new HashSet<ProviderCapability> { ProviderCapability.Detail }, hello.Provides);
        Assert.Equal(["QQ", "WeChat"], hello.Apps!.Order());
        Assert.Contains("qq", hello.Apps!);
    }

    [Fact]
    public void Detail_line_is_read()
    {
        var detail = Assert.IsType<ProviderDetail>(ProviderProtocol.Parse("""{"v":1,"type":"detail","process":"QQ.exe","window":" QQ ","title":"张三"}""").Message);

        Assert.Equal(new ProviderDetail("QQ", "QQ", "张三"), detail);
        Assert.Equal(new ProviderDetail("QQ", null, null), ProviderProtocol.Parse("""{"type":"detail","process":"QQ","title":null}""").Message);
    }

    [Theory]
    [InlineData("""{"type":"detail","title":"x"}""")]
    [InlineData("""{"type":"detail","process":"QQ","title":3}""")]
    [InlineData("""{"type":"detail","process":"QQ","window":false}""")]
    [InlineData("""{"type":"hello","name":"x","apps":"QQ"}""")]
    public void Malformed_detail_lines_are_rejected(string line)
    {
        Assert.NotNull(ProviderProtocol.Parse(line).Error);
    }

    [Theory]
    [InlineData("GitHub.com", "github.com")]
    [InlineData("https://www.github.com/a/b", "github.com")]
    [InlineData("localhost", "localhost")]
    [InlineData("not a host", null)]
    [InlineData("intranet", null)]
    [InlineData("", null)]
    public void Domains_are_normalized(string raw, string? expected)
    {
        Assert.Equal(expected, ProviderProtocol.NormalizeDomain(raw));
    }
}
