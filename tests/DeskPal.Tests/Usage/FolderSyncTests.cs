using System.Text;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Usage;

public sealed class FolderSyncTests : IDisposable
{
    private const string Pin = "20261113";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    private static readonly DateOnly Day = new(2026, 11, 11);
    private static readonly StatsSyncCrypto.KdfParameters Cheap = new(64, 1, 1);
    private readonly Guid _user = Guid.NewGuid();
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"deskpal-folder-{Guid.NewGuid():N}");
    private readonly List<SqliteUsageStore> _stores = [];

    public FolderSyncTests()
    {
        Directory.CreateDirectory(Cloud);
    }

    private string Cloud => Path.Combine(_work, "cloud");

    private string UserRoot => Path.Combine(Cloud, FolderSync.RootName, _user.ToString("N"));

    private static DateTimeOffset At(int hour, int minute = 0, int dayOffset = 0) =>
        new(Day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(8));

    private static UsageSegment Work(int hour, string title, int dayOffset = 0) =>
        new(At(hour, 0, dayOffset), At(hour, 30, dayOffset), PresenceState.Working, ActivityCategory.Work, "Cursor", title);

    [Fact]
    public async Task Nothing_is_written_while_off()
    {
        var a = NewStore();
        a.Add(Work(9, "a.md"));
        var sync = NewSync(a, new FakeStore());

        await sync.SyncAsync(allDays: true);

        Assert.Equal(FolderSyncMode.Off, sync.Mode);
        Assert.False(Directory.Exists(Path.Combine(Cloud, FolderSync.RootName)));
    }

    [Fact]
    public async Task Two_computers_merge_through_the_folder_and_nothing_doubles()
    {
        var a = NewStore();
        a.Add(Work(9, "a.md"));
        a.Add(Work(10, "old.md", dayOffset: -5));
        var syncA = NewSync(a, new FakeStore());
        Assert.Equal(FolderCheck.New, syncA.Check(Cloud));
        Assert.Equal(FolderSyncEnable.Enabled, await syncA.EnableAsync(Cloud, Pin));
        await syncA.SyncAsync(allDays: true);

        var files = Directory.GetFiles(Path.Combine(UserRoot, a.DeviceId.ToString("N")));
        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.DoesNotContain("a.md", Encoding.UTF8.GetString(File.ReadAllBytes(f))));

        var b = NewStore();
        b.Add(Work(14, "b.md"));
        var syncB = NewSync(b, new FakeStore());
        Assert.Equal(FolderCheck.Existing, syncB.Check(Cloud));
        Assert.Equal(FolderSyncEnable.WrongPin, await syncB.EnableAsync(Cloud, "11111111"));
        Assert.Equal(FolderSyncMode.Off, syncB.Mode);
        Assert.Equal(FolderSyncEnable.Enabled, await syncB.EnableAsync(Cloud, Pin));

        await syncB.SyncAsync(allDays: true);
        await syncB.SyncAsync(allDays: true);
        await syncA.SyncAsync(allDays: false);
        await syncA.SyncAsync(allDays: false);

        Assert.Equal(["a.md", "b.md"], a.Segments(At(0), At(0, 0, 1)).Select(s => s.Title!).ToArray());
        Assert.Equal(["a.md", "b.md"], b.Segments(At(0), At(0, 0, 1)).Select(s => s.Title!).ToArray());
        Assert.Single(b.Segments(At(0, 0, -5), At(0, 0, -4)));
        Assert.Single(Directory.GetFiles(Path.Combine(UserRoot, b.DeviceId.ToString("N"))));
    }

    [Fact]
    public async Task Each_computer_writes_only_its_own_days_and_only_when_they_change()
    {
        var a = NewStore();
        var id = a.Add(new UsageSegment(At(9), At(9, 10), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
        var syncA = NewSync(a, new FakeStore());
        await syncA.EnableAsync(Cloud, Pin);
        await syncA.SyncAsync(allDays: true);
        var file = Path.Combine(UserRoot, a.DeviceId.ToString("N"), FolderSync.DayFileName(Day));
        var first = File.GetLastWriteTimeUtc(file);

        var b = NewStore();
        var syncB = NewSync(b, new FakeStore());
        await syncB.EnableAsync(Cloud, Pin);
        await syncB.SyncAsync(allDays: true);
        Assert.False(Directory.Exists(Path.Combine(UserRoot, b.DeviceId.ToString("N"))) && Directory.GetFiles(Path.Combine(UserRoot, b.DeviceId.ToString("N"))).Length > 0);

        await Task.Delay(20);
        await syncA.SyncAsync(allDays: false);
        Assert.Equal(first, File.GetLastWriteTimeUtc(file));

        a.UpdateEnd(id, At(9, 40));
        await syncA.SyncAsync(allDays: false);
        await syncB.SyncAsync(allDays: false);
        Assert.Equal(At(9, 40), b.Segments(At(0), At(0, 0, 1)).Single().End);
    }

    [Fact]
    public async Task A_day_folded_on_another_computer_is_not_counted_twice()
    {
        var a = NewStore();
        a.Add(Work(9, "old.md", dayOffset: -5));
        var syncA = NewSync(a, new FakeStore());
        await syncA.EnableAsync(Cloud, Pin);
        await syncA.SyncAsync(allDays: true);

        var b = NewStore();
        var syncB = NewSync(b, new FakeStore());
        await syncB.EnableAsync(Cloud, Pin);
        await syncB.SyncAsync(allDays: true);
        Assert.Single(b.Segments(At(0, 0, -5), At(0, 0, -4)));

        a.Compact(Day.AddDays(-2), At(12));
        await syncA.SyncAsync(allDays: true);
        await syncB.SyncAsync(allDays: false);

        Assert.Empty(b.Segments(At(0, 0, -5), At(0, 0, -4)));
        b.Compact(Day.AddDays(-2), At(12));
        Assert.Equal(1800, b.Rollups(Day.AddDays(-5), Day.AddDays(-5)).Sum(r => r.Seconds));
    }

    [Fact]
    public async Task Conflict_copies_temp_files_and_unreadable_files_are_skipped()
    {
        var a = NewStore();
        a.Add(Work(9, "a.md"));
        var syncA = NewSync(a, new FakeStore());
        await syncA.EnableAsync(Cloud, Pin);
        await syncA.SyncAsync(allDays: true);
        var dirA = Path.Combine(UserRoot, a.DeviceId.ToString("N"));
        File.Copy(Path.Combine(dirA, FolderSync.DayFileName(Day)), Path.Combine(dirA, "2026-11-11 (1).dps"));
        File.WriteAllBytes(Path.Combine(dirA, FolderSync.DayFileName(Day.AddDays(-1))), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(dirA, FolderSync.DayFileName(Day.AddDays(-2)) + ".tmp"), [1, 2, 3]);

        var b = NewStore();
        var syncB = NewSync(b, new FakeStore());
        await syncB.EnableAsync(Cloud, Pin);
        await syncB.SyncAsync(allDays: true);

        Assert.Single(b.Segments(At(0, 0, -3), At(0, 0, 1)));
        Assert.Equal(FolderSyncMode.On, syncB.Mode);
    }

    [Fact]
    public async Task The_key_is_kept_and_stopping_leaves_the_files()
    {
        var a = NewStore();
        a.Add(Work(9, "a.md"));
        var store = new FakeStore();
        var syncA = NewSync(a, store);
        await syncA.EnableAsync(Cloud, Pin);
        await syncA.SyncAsync(allDays: true);
        Assert.Equal(Path.GetFullPath(Cloud), store.Load()!.Folder);

        var restarted = NewSync(a, store);
        Assert.Equal(FolderSyncMode.On, restarted.Mode);
        await restarted.DisableAsync();

        Assert.Null(store.Load());
        Assert.Equal(FolderSyncMode.Off, restarted.Mode);
        Assert.Single(Directory.GetFiles(Path.Combine(UserRoot, a.DeviceId.ToString("N"))));
        Assert.Single(a.Segments(At(0), At(0, 0, 1)));
    }

    [Fact]
    public async Task A_folder_set_up_again_turns_sync_off_and_a_missing_folder_is_unavailable()
    {
        var a = NewStore();
        var store = new FakeStore();
        var syncA = NewSync(a, store);
        await syncA.EnableAsync(Cloud, Pin);

        var moved = Cloud + "-moved";
        Directory.Move(Cloud, moved);
        await syncA.SyncAsync(allDays: false);
        Assert.Equal(FolderSyncMode.Unavailable, syncA.Mode);
        Directory.Move(moved, Cloud);
        await syncA.SyncAsync(allDays: false);
        Assert.Equal(FolderSyncMode.On, syncA.Mode);

        Directory.Delete(Path.Combine(Cloud, FolderSync.RootName), recursive: true);
        var syncB = NewSync(NewStore(), new FakeStore());
        await syncB.EnableAsync(Cloud, "87654321");
        await syncA.SyncAsync(allDays: false);
        Assert.Equal(FolderSyncMode.Off, syncA.Mode);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Accounts_sharing_a_folder_do_not_read_each_other()
    {
        var a = NewStore();
        a.Add(Work(9, "a.md"));
        var syncA = NewSync(a, new FakeStore());
        await syncA.EnableAsync(Cloud, Pin);
        await syncA.SyncAsync(allDays: true);

        var other = NewStore();
        var syncOther = new FolderSync(other, new FakeStore(), Guid.NewGuid(), () => Day, Cheap);
        Assert.Equal(FolderCheck.New, syncOther.Check(Cloud));
        await syncOther.EnableAsync(Cloud, Pin);
        await syncOther.SyncAsync(allDays: true);

        Assert.Empty(other.Segments(At(0), At(0, 0, 1)));
    }

    [Fact]
    public void A_missing_folder_is_not_writable()
    {
        var sync = NewSync(NewStore(), new FakeStore());
        Assert.Equal(FolderCheck.NotWritable, sync.Check(Path.Combine(_work, "missing")));
    }

    private FolderSync NewSync(SqliteUsageStore store, FakeStore settings) => new(store, settings, _user, () => Day, Cheap);

    private SqliteUsageStore NewStore()
    {
        var store = new SqliteUsageStore(Path.Combine(_work, $"{Guid.NewGuid():N}.db"), Guid.NewGuid(), Zone);
        _stores.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        Directory.Delete(_work, recursive: true);
    }

    private sealed class FakeStore : IFolderSyncStore
    {
        private FolderSyncConfig? _config;

        public FolderSyncConfig? Load() => _config;

        public void Save(FolderSyncConfig config) => _config = config;

        public void Delete() => _config = null;
    }
}