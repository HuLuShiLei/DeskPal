using DeskPal.Client.Core.Diagnostics;

namespace DeskPal.Tests.Diagnostics;

public sealed class InstallDirectoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "deskpal-install-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Not_installed_returns_null(string? root)
    {
        Assert.Null(InstallDirectory.Writable(root));
    }

    [Fact]
    public void Writable_root_is_used_and_left_clean()
    {
        Directory.CreateDirectory(_directory);

        Assert.Equal(_directory, InstallDirectory.Writable(_directory));
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public void Root_that_cannot_be_created_returns_null()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(file, "x");

        Assert.Null(InstallDirectory.Writable(Path.Combine(file, "DeskPal")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}