using System.Text;
using DeskPal.Client.Core.Personal;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Personal;

public sealed class PersonalDataTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 1, 0, 0, TimeSpan.Zero);
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"deskpal-personal-{Guid.NewGuid():N}");

    public PersonalDataTests() => Directory.CreateDirectory(Cloud);

    private string Cloud => Path.Combine(_work, "cloud");

    private static UserRuleDto Rule(string pattern, ActivityCategory category, DateTimeOffset at) => new(RuleKind.Process, pattern, category, null, at);

    private static NoTitleRuleDto NoTitle(string pattern, DateTimeOffset at) => new(NoTitleKind.Process, pattern, at);

    [Fact]
    public void Merge_takes_the_latest_change_of_each_rule_and_deletions_count()
    {
        var mine = PersonalData.Empty
            .WithRule(Rule("wechat", ActivityCategory.Work, Start))
            .WithRule(Rule("qq", ActivityCategory.Communication, Start))
            .WithNoTitle(NoTitle("sunloginclient", Start));
        var theirs = PersonalData.Empty
            .WithRule(Rule("wechat", ActivityCategory.Communication, Start.AddMinutes(1)))
            .WithRule(Rule("qq", ActivityCategory.Communication, Start))
            .WithoutRule(RuleKind.Process, "qq", Start.AddMinutes(2))
            .WithNoTitle(NoTitle("todesk", Start));

        var merged = PersonalMerge.Merge(mine, theirs, Start.AddMinutes(3));

        Assert.Equal(ActivityCategory.Communication, Assert.Single(merged.Rules).Category);
        Assert.Equal(["sunloginclient", "todesk"], merged.NoTitleRules.Select(r => r.Pattern).Order());
        Assert.Single(merged.Tombstones);

        var readded = PersonalMerge.Merge(merged, PersonalData.Empty.WithRule(Rule("qq", ActivityCategory.Game, Start.AddMinutes(5))), Start.AddMinutes(6));
        Assert.Equal(2, readded.Rules.Count);
        Assert.Empty(readded.Tombstones);

        var forgotten = PersonalMerge.Merge(merged, PersonalData.Empty, Start.AddDays(91));
        Assert.Empty(forgotten.Tombstones);
    }

    [Fact]
    public void Import_replaces_the_same_rules_and_keeps_the_rest()
    {
        var mine = PersonalData.Empty
            .WithRule(Rule("wechat", ActivityCategory.Work, Start))
            .WithRule(Rule("qq", ActivityCategory.Communication, Start));
        var file = PersonalData.Empty
            .WithRule(Rule("wechat", ActivityCategory.Communication, Start.AddDays(-30)))
            .WithNoTitle(NoTitle("sunloginclient", Start.AddDays(-30)));

        var imported = PersonalMerge.Import(mine, file, Start.AddHours(1));

        Assert.Equal(2, imported.Rules.Count);
        var wechat = imported.Rules.Single(r => r.Pattern == "wechat");
        Assert.Equal((ActivityCategory.Communication, Start.AddHours(1)), (wechat.Category, wechat.UpdatedAt));
        Assert.Equal(Start.AddHours(1), Assert.Single(imported.NoTitleRules).UpdatedAt);
    }

    [Fact]
    public void File_round_trips_and_other_json_is_not_taken_for_it()
    {
        var data = PersonalData.Empty.WithRule(Rule("wechat", ActivityCategory.Work, Start)).WithNoTitle(NoTitle("sunloginclient", Start));
        var file = PersonalFile.Create(data, new UserPreferences(Theme: "dark"), 15, Start, Start);

        var json = System.Text.Encoding.UTF8.GetString(file.Serialize());
        var parsed = PersonalFile.Parse(file.Serialize())!;

        Assert.All(new[] { "\"data\"", "\"rule\"", "\"tombstones\"" }, name => Assert.DoesNotContain(name, json));

        Assert.Equal(("wechat", "sunloginclient"), (parsed.Data.Rules.Single().Pattern, parsed.Data.NoTitleRules.Single().Pattern));
        Assert.Equal(("dark", (int?)15, (DateTimeOffset?)Start), (parsed.Preferences!.Theme, parsed.IdleMinutes, parsed.PreferencesUpdatedAt));
        Assert.Null(PersonalFile.Parse("""{"format":"deskpal-usage","segments":[]}"""u8.ToArray()));
        Assert.Null(PersonalFile.Parse("not json"u8.ToArray()));
    }

    [Fact]
    public void Store_belongs_to_one_account()
    {
        var store = new PersonalDataStore(Path.Combine(_work, "local"));
        var alice = Guid.NewGuid();
        store.Save(alice, PersonalData.Empty.WithNoTitle(NoTitle("sunloginclient", Start)));

        Assert.Equal("sunloginclient", store.Load(alice)!.NoTitleRules.Single().Pattern);
        Assert.Null(store.Load(Guid.NewGuid()));
    }

    [Fact]
    public void Two_computers_sync_through_the_folder_and_read_each_file_once()
    {
        var user = Guid.NewGuid();
        var a = new PersonalFolderSync(user, Guid.NewGuid());
        var b = new PersonalFolderSync(user, Guid.NewGuid());
        Assert.True(a.CanUse(Cloud));

        a.Write(Cloud, PersonalFile.Create(PersonalData.Empty.WithNoTitle(NoTitle("sunloginclient", Start)), null, null, null, Start));
        Assert.Empty(a.ReadChanged(Cloud));

        var read = Assert.Single(b.ReadChanged(Cloud));
        Assert.Equal("sunloginclient", read.Data.NoTitleRules.Single().Pattern);
        Assert.Empty(b.ReadChanged(Cloud));

        var path = Directory.GetFiles(PersonalFolderSync.DirectoryOf(Cloud, user)).Single();
        var written = File.GetLastWriteTimeUtc(path);
        a.Write(Cloud, PersonalFile.Create(PersonalData.Empty.WithNoTitle(NoTitle("sunloginclient", Start)), null, null, null, Start.AddMinutes(5)));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));

        Assert.False(a.CanUse(Path.Combine(_work, "missing")));
        Assert.Throws<DirectoryNotFoundException>(() => a.ReadChanged(Path.Combine(_work, "missing")));
    }

    [Fact]
    public async Task Personal_files_and_usage_stats_share_one_folder()
    {
        var user = Guid.NewGuid();
        var day = DateOnly.FromDateTime(Start.UtcDateTime);
        var personal = new PersonalFolderSync(user, Guid.NewGuid());
        personal.Write(Cloud, PersonalFile.Create(PersonalData.Empty.WithNoTitle(NoTitle("sunloginclient", Start)), null, null, null, Start));

        using (var db = new SqliteUsageStore(Path.Combine(_work, "usage.db"), Guid.NewGuid(), TimeZoneInfo.Utc))
        {
            db.Add(new UsageSegment(Start, Start.AddMinutes(30), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
            var stats = new FolderSync(db, new MemoryFolderStore(), user, () => day, new StatsSyncCrypto.KdfParameters(64, 1, 1));
            Assert.Equal(FolderCheck.New, stats.Check(Cloud));
            Assert.Equal(FolderSyncEnable.Enabled, await stats.EnableAsync(Cloud, "20261113"));
            await stats.SyncAsync(allDays: true);
            Assert.Equal(FolderSyncMode.On, stats.Mode);
        }

        var other = new PersonalFolderSync(user, Guid.NewGuid());
        Assert.Equal("sunloginclient", Assert.Single(other.ReadChanged(Cloud)).Data.NoTitleRules.Single().Pattern);
        Assert.Contains("sunloginclient", Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(PersonalFolderSync.DirectoryOf(Cloud, user)).Single())));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private sealed class MemoryFolderStore : IFolderSyncStore
    {
        private FolderSyncConfig? _config;

        public FolderSyncConfig? Load() => _config;

        public void Save(FolderSyncConfig config) => _config = config;

        public void Delete() => _config = null;
    }
}