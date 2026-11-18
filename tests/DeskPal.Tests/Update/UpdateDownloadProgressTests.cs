using DeskPal.Client.Core.Updates;

namespace DeskPal.Tests.Update;

public class UpdateDownloadProgressTests
{
    [Fact]
    public void Full_download_shows_the_percent_as_is()
    {
        var progress = new UpdateDownloadProgress(delta: false);

        Assert.Equal(0, progress.Map(0));
        Assert.Equal(70, progress.Map(70));
        Assert.Equal(100, progress.Map(100));
    }

    [Fact]
    public void Delta_download_fills_the_whole_range_then_shows_preparing()
    {
        var progress = new UpdateDownloadProgress(delta: true);

        Assert.Equal(0, progress.Map(0));
        Assert.Equal(50, progress.Map(35));
        Assert.Equal(97, progress.Map(68));
        Assert.Null(progress.Map(70));
        Assert.Null(progress.Map(100));
    }

    [Fact]
    public void Progress_going_back_means_velopack_fell_back_to_the_full_package()
    {
        var progress = new UpdateDownloadProgress(delta: true);
        progress.Map(40);
        progress.Map(70);

        Assert.Equal(2, progress.Map(2));
        Assert.Equal(80, progress.Map(80));
        Assert.Equal(100, progress.Map(100));
    }

    [Theory]
    [InlineData(true, 1, 10, 100, true)]
    [InlineData(true, 10, 10, 100, true)]
    [InlineData(false, 1, 10, 100, false)]
    [InlineData(true, 0, 0, 100, false)]
    [InlineData(true, 11, 10, 100, false)]
    [InlineData(true, 2, 101, 100, false)]
    public void Deltas_are_used_only_when_velopack_would_try_them(bool hasBase, int count, long deltaBytes, long fullBytes, bool expected)
    {
        Assert.Equal(expected, UpdateDownloadProgress.UsesDeltas(hasBase, count, deltaBytes, fullBytes));
    }
}