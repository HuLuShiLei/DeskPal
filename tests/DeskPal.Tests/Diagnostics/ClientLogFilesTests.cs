using DeskPal.Client.Core.Diagnostics;

namespace DeskPal.Tests.Diagnostics;

public sealed class ClientLogFilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "deskpal-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Rolls_over_and_keeps_only_the_newest_archives()
    {
        using var logs = new ClientLogFiles(_directory, maxFileBytes: 220, maxArchiveFiles: 2);
        for (var i = 0; i < 6; i++)
        {
            logs.Write("INF", "Test", new string('x', 80) + i);
        }

        logs.Dispose();
        Assert.True(File.Exists(Path.Combine(_directory, "deskpal.log")));
        var archives = Directory.GetFiles(_directory, "deskpal-*.log");
        Assert.Equal(2, archives.Length);
        var text = File.ReadAllText(Path.Combine(_directory, "deskpal.log"));
        Assert.Contains("xxxxxx5", text);
    }

    [Fact]
    public void Deletes_archives_older_than_the_age_limit()
    {
        Directory.CreateDirectory(_directory);
        var stale = Path.Combine(_directory, "deskpal-20000101-000000.log");
        File.WriteAllText(stale, "old");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-8));

        using var logs = new ClientLogFiles(_directory, maxAge: TimeSpan.FromDays(7));
        logs.Write("INF", "Test", "fresh");

        Assert.False(File.Exists(stale));
        Assert.Contains("fresh", File.ReadAllText(Path.Combine(_directory, "deskpal.log")));
    }

    [Fact]
    public void Redacts_tokens_and_truncates_long_lines()
    {
        using var logs = new ClientLogFiles(_directory);
        logs.Write("ERR", "Test", "access_token=secret-value Bearer abc.def password=hunter2 " + new string('y', 5000));
        logs.Dispose();

        var text = File.ReadAllText(Path.Combine(_directory, "deskpal.log"));
        Assert.DoesNotContain("secret-value", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("abc.def", text);
        Assert.Contains("access_token=(redacted)", text);
        Assert.Contains("password=(redacted)", text);
        Assert.Contains("Bearer (redacted)", text);
        Assert.True(text.Length < ClientLogFiles.MaxLineChars + 80);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}