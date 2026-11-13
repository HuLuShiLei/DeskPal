using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Xunit.Abstractions;

namespace DeskPal.Tests.Usage;

public sealed class StatsSyncTests : IDisposable
{
    private const string Pin = "20261113";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    private static readonly DateOnly Day = new(2026, 11, 11);
    private static readonly StatsSyncCrypto.KdfParameters Cheap = new(64, 1, 1);
    private readonly List<string> _files = [];
    private readonly List<SqliteUsageStore> _stores = [];

    private static DateTimeOffset At(int hour, int minute = 0, int dayOffset = 0) =>
        new(Day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(8));

    private static UsageSegment Work(int hour, string title, int dayOffset = 0) =>
        new(At(hour, 0, dayOffset), At(hour, 30, dayOffset), PresenceState.Working, ActivityCategory.Work, "Cursor", title);

    [Fact]
    public void Pin_must_be_eight_digits()
    {
        Assert.True(StatsSyncCrypto.IsValidPin("01234567"));
        Assert.False(StatsSyncCrypto.IsValidPin("1234567"));
        Assert.False(StatsSyncCrypto.IsValidPin("123456789"));
        Assert.False(StatsSyncCrypto.IsValidPin("1234567a"));
        Assert.False(StatsSyncCrypto.IsValidPin(null));
    }

    [Fact]
    public void Key_check_tells_a_wrong_pin_and_sealed_days_need_the_key()
    {
        var salt = StatsSyncCrypto.NewSalt(Cheap);
        var key = StatsSyncCrypto.DeriveKey(Pin, salt);
        var check = StatsSyncCrypto.KeyCheck(key);

        Assert.Equal(key, StatsSyncCrypto.DeriveKey(Pin, salt));
        Assert.True(StatsSyncCrypto.Verify(key, check));
        Assert.False(StatsSyncCrypto.Verify(StatsSyncCrypto.DeriveKey("20261114", salt), check));
        Assert.NotEqual(key, StatsSyncCrypto.DeriveKey(Pin, StatsSyncCrypto.NewSalt(Cheap)));

        var plain = Encoding.UTF8.GetBytes("""{"title":"病历.docx"}""");
        var sealedDay = StatsSyncCrypto.Seal(key, plain);
        Assert.DoesNotContain("病历", Encoding.UTF8.GetString(sealedDay));
        Assert.Equal(plain, StatsSyncCrypto.Open(key, sealedDay));
        Assert.ThrowsAny<CryptographicException>(() => StatsSyncCrypto.Open(StatsSyncCrypto.DeriveKey("20261114", salt), sealedDay));
        Assert.Throws<FormatException>(() => StatsSyncCrypto.DeriveKey(Pin, "pbkdf2$x$y"));
    }

    [Fact]
    public async Task Nothing_is_uploaded_while_sync_is_off()
    {
        var server = new FakeServer();
        var store = NewStore();
        store.Add(Work(9, "a.md"));
        var sync = new StatsSync(store, server, new FakeKeys(), () => Day, Cheap);

        await sync.SyncAsync(allDays: true);

        Assert.Equal(StatsSyncMode.Off, sync.Mode);
        Assert.Equal(0, server.Uploads);
        Assert.Equal(0, server.Downloads);
    }

    [Fact]
    public async Task Another_computer_merges_after_the_pin_and_nothing_doubles()
    {
        var server = new FakeServer();
        var a = NewStore();
        a.Add(Work(9, "a.md"));
        a.Add(Work(10, "b.md", dayOffset: -5));
        var syncA = new StatsSync(a, server, new FakeKeys(), () => Day, Cheap);
        Assert.True(await syncA.EnableAsync(Pin));
        await syncA.SyncAsync(allDays: true);
        Assert.Equal(2, server.Uploads);
        Assert.All(server.Days.Values, d => Assert.DoesNotContain("a.md", Encoding.UTF8.GetString(d.Data)));

        var b = NewStore();
        var syncB = new StatsSync(b, server, new FakeKeys(), () => Day, Cheap);
        await syncB.SyncAsync(allDays: true);
        Assert.Equal(StatsSyncMode.NeedsPin, syncB.Mode);
        Assert.Equal(StatsSyncUnlock.WrongPin, await syncB.UnlockAsync("11111111"));
        Assert.Equal(StatsSyncUnlock.Unlocked, await syncB.UnlockAsync(Pin));

        await syncB.SyncAsync(allDays: true);
        await syncB.SyncAsync(allDays: true);
        Assert.Equal(2, server.Uploads);
        Assert.Equal(2, b.Segments(At(0, 0, -10), At(0, 0, 1)).Count);

        b.Add(Work(14, "c.md"));
        await syncB.SyncAsync(allDays: false);
        Assert.Equal(3, server.Uploads);

        await syncA.SyncAsync(allDays: false);
        Assert.Equal(["a.md", "c.md"], a.Segments(At(0), At(0, 0, 1)).Select(s => s.Title!).ToArray());
        Assert.Equal(3, server.Uploads);
    }

    [Fact]
    public async Task Turning_off_elsewhere_drops_the_key_and_enabling_twice_asks_for_the_pin()
    {
        var server = new FakeServer();
        var keysA = new FakeKeys();
        var syncA = new StatsSync(NewStore(), server, keysA, () => Day, Cheap);
        await syncA.EnableAsync(Pin);

        var syncB = new StatsSync(NewStore(), server, new FakeKeys(), () => Day, Cheap);
        Assert.False(await syncB.EnableAsync("99999999"));
        Assert.Equal(StatsSyncMode.NeedsPin, syncB.Mode);
        await syncB.UnlockAsync(Pin);
        await syncB.DisableAsync();
        Assert.False(server.Enabled);
        Assert.Empty(server.Days);

        await syncA.SyncAsync(allDays: false);
        Assert.Equal(StatsSyncMode.Off, syncA.Mode);
        Assert.Null(keysA.Load());
    }

    [Fact]
    public async Task Enabling_again_with_a_new_pin_needs_the_new_pin_on_other_computers()
    {
        var server = new FakeServer();
        var syncA = new StatsSync(NewStore(), server, new FakeKeys(), () => Day, Cheap);
        await syncA.EnableAsync(Pin);

        var syncB = new StatsSync(NewStore(), server, new FakeKeys(), () => Day, Cheap);
        await syncB.UnlockAsync(Pin);
        await syncB.DisableAsync();
        await syncB.EnableAsync("87654321");

        await syncA.SyncAsync(allDays: false);
        Assert.Equal(StatsSyncMode.NeedsPin, syncA.Mode);
        Assert.Equal(StatsSyncUnlock.WrongPin, await syncA.UnlockAsync(Pin));
        Assert.Equal(StatsSyncUnlock.Unlocked, await syncA.UnlockAsync("87654321"));
    }

    [Fact]
    public async Task Sync_stays_on_when_the_server_cannot_turn_it_off()
    {
        var server = new FakeServer();
        var keys = new FakeKeys();
        var sync = new StatsSync(NewStore(), server, keys, () => Day, Cheap);
        await sync.EnableAsync(Pin);

        server.Unreachable = true;
        await Assert.ThrowsAsync<HttpRequestException>(sync.DisableAsync);

        Assert.Equal(StatsSyncMode.On, sync.Mode);
        Assert.NotNull(keys.Load());
        Assert.True(server.Enabled);
    }

    [Fact]
    public async Task Days_missing_on_the_server_are_sent_again()
    {
        var server = new FakeServer();
        var b = NewStore();
        b.Add(Work(9, "old.md", dayOffset: -5));
        var syncB = new StatsSync(b, server, new FakeKeys(), () => Day, Cheap);
        await syncB.EnableAsync(Pin);
        await syncB.SyncAsync(allDays: true);
        Assert.Single(server.Days);

        server.Days.Clear();
        await syncB.SyncAsync(allDays: false);
        Assert.Equal([Day.AddDays(-5)], server.Days.Keys);
        await syncB.SyncAsync(allDays: false);
        Assert.Equal(2, server.Uploads);
    }

    [Fact]
    public async Task A_segment_that_grew_on_another_computer_grows_here_too()
    {
        var server = new FakeServer();
        var a = NewStore();
        var id = a.Add(new UsageSegment(At(9), At(9, 10), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
        var syncA = new StatsSync(a, server, new FakeKeys(), () => Day, Cheap);
        await syncA.EnableAsync(Pin);
        await syncA.SyncAsync(allDays: true);

        var b = NewStore();
        var syncB = new StatsSync(b, server, new FakeKeys(), () => Day, Cheap);
        await syncB.UnlockAsync(Pin);
        await syncB.SyncAsync(allDays: true);
        Assert.Equal(At(9, 10), b.Segments(At(0), At(0, 0, 1)).Single().End);

        a.UpdateEnd(id, At(9, 40));
        await syncA.SyncAsync(allDays: false);
        await syncB.SyncAsync(allDays: false);
        Assert.Equal(At(9, 40), b.Segments(At(0), At(0, 0, 1)).Single().End);

        await syncA.SyncAsync(allDays: false);
        Assert.Equal(At(9, 40), a.Segments(At(0), At(0, 0, 1)).Single().End);
    }

    [Fact]
    public async Task A_day_folded_on_another_computer_is_not_counted_twice()
    {
        var server = new FakeServer();
        var a = NewStore();
        a.Add(Work(9, "old.md", dayOffset: -5));
        var syncA = new StatsSync(a, server, new FakeKeys(), () => Day, Cheap);
        await syncA.EnableAsync(Pin);
        await syncA.SyncAsync(allDays: true);

        var b = NewStore();
        var syncB = new StatsSync(b, server, new FakeKeys(), () => Day, Cheap);
        await syncB.UnlockAsync(Pin);
        await syncB.SyncAsync(allDays: true);
        Assert.Single(b.Segments(At(0, 0, -5), At(0, 0, -4)));

        a.Compact(Day.AddDays(-2), At(12));
        await syncA.SyncAsync(allDays: true);
        await syncB.SyncAsync(allDays: false);

        Assert.Empty(b.Segments(At(0, 0, -5), At(0, 0, -4)));
        b.Compact(Day.AddDays(-2), At(12));
        Assert.Equal(1800, b.Rollups(Day.AddDays(-5), Day.AddDays(-5)).Sum(r => r.Seconds));
    }

    private SqliteUsageStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deskpal-sync-{Guid.NewGuid():N}.db");
        _files.Add(path);
        var store = new SqliteUsageStore(path, Guid.NewGuid(), Zone);
        _stores.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        foreach (var file in _files)
        {
            foreach (var path in new[] { file, file + "-wal", file + "-shm" })
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FakeKeys : IStatsSyncKeyStore
    {
        private StoredSyncKey? _key;

        public StoredSyncKey? Load() => _key;

        public void Save(StoredSyncKey key) => _key = key;

        public void Delete() => _key = null;
    }

    /// <summary>Behaves like StatsSyncService on the server: revisions only go up, days are opaque bytes.</summary>
    private sealed class FakeServer : IStatsSyncApi
    {
        private long _revision;

        public bool Enabled { get; private set; }
        public string? Salt { get; private set; }
        public string? KeyCheck { get; private set; }
        public Dictionary<DateOnly, (long Revision, byte[] Data)> Days { get; } = [];
        public int Uploads { get; private set; }
        public int Downloads { get; private set; }
        public bool Unreachable { get; set; }

        public Task<StatsSyncStatusDto> GetStatusAsync() => Task.FromResult(Enabled
            ? new StatsSyncStatusDto(true, Salt, KeyCheck, Days.OrderBy(d => d.Key).Select(d => new StatsSyncDayInfo(d.Key, d.Value.Revision)).ToList())
            : new StatsSyncStatusDto(false, null, null, []));

        public async Task<StatsSyncStatusDto?> EnableAsync(EnableStatsSyncRequest request)
        {
            if (Enabled)
            {
                return null;
            }

            (Enabled, Salt, KeyCheck) = (true, request.Salt, request.KeyCheck);
            Days.Clear();
            return await GetStatusAsync();
        }

        public Task DisableAsync()
        {
            if (Unreachable)
            {
                throw new HttpRequestException("unreachable");
            }

            (Enabled, Salt, KeyCheck) = (false, null, null);
            Days.Clear();
            return Task.CompletedTask;
        }

        public Task<StatsSyncDayInfo> UploadAsync(DateOnly date, byte[] data)
        {
            Assert.True(Enabled);
            Uploads++;
            Days[date] = (++_revision, data);
            return Task.FromResult(new StatsSyncDayInfo(date, _revision));
        }

        public Task<StatsSyncDayDto?> DownloadAsync(DateOnly date)
        {
            Downloads++;
            return Task.FromResult(Days.TryGetValue(date, out var day) ? new StatsSyncDayDto(date, day.Revision, Convert.ToBase64String(day.Data)) : null);
        }
    }
}

/// <summary>Uses all cores for a moment, so it runs alone rather than slowing the SignalR tests running beside it.</summary>
[Collection(nameof(StatsSyncKdfTests))]
public sealed class StatsSyncKdfTests(ITestOutputHelper output)
{
    [Fact]
    public void Default_derivation_takes_a_noticeable_time()
    {
        var watch = Stopwatch.StartNew();
        StatsSyncCrypto.DeriveKey("20261113", StatsSyncCrypto.NewSalt());
        output.WriteLine($"Argon2id {StatsSyncCrypto.DefaultParameters}: {watch.ElapsedMilliseconds} ms");
        Assert.InRange(watch.ElapsedMilliseconds, 50, 10_000);
    }
}

[CollectionDefinition(nameof(StatsSyncKdfTests), DisableParallelization = true)]
public sealed class StatsSyncKdfCollection;