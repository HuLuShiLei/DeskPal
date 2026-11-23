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
}
