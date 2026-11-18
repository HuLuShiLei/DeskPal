using System.IO;
using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Stats;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>My usage history on this computer: recording, queries, import and export. One database file per account.</summary>
public sealed class UsageService : IDisposable
{
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions ExportOptions = new(DeskPalJson.Options) { WriteIndented = true };

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ILogger<UsageService> _logger;
    private readonly SqliteUsageStore _db;
    private readonly Timer _prune;
    private IReadOnlyList<StatsViewRecord> _views = [];
    private readonly PersonalRules _personalRules;
    private readonly BuiltinRuleCache _builtinRules;
    private volatile UsageReclassifier _reclassifier = UsageReclassifier.None;

    public UsageService(AppSettings settings, SettingsStore store, PersonalRules personalRules, BuiltinRuleCache builtinRules, ILogger<UsageService> logger)
    {
        _settings = settings;
        _store = store;
        _logger = logger;
        _personalRules = personalRules;
        _builtinRules = builtinRules;
        _reclassifier = new UsageReclassifier(personalRules.Rules);
        personalRules.Changed += () => _reclassifier = new UsageReclassifier(personalRules.Rules);
        if (settings.DeviceId == Guid.Empty)
        {
            settings.DeviceId = Guid.NewGuid();
            store.Save(settings);
        }

        _db = new SqliteUsageStore(Path.Combine(SettingsStore.Directory, "usage", $"{settings.UserId:N}.db"), settings.DeviceId);
        Recorder = new UsageRecorder(_db);
        Recorder.SetNoTitleRules(settings.NoTitleRules);
        _prune = new Timer(_ => PruneViews(), null, TimeSpan.FromSeconds(30), PruneEvery);
        LoadViews();
    }

    /// <summary>Raised when the view records change.</summary>
    public event Action? Changed;

    /// <summary>Raised when past days may have changed: import, clearing.</summary>
    public event Action? DaysChanged;

    public UsageRecorder Recorder { get; }

    public SqliteUsageStore Store => _db;

    /// <summary>Who viewed my stats in the last 7 days, latest first.</summary>
    public IReadOnlyList<StatsViewRecord> RecentViews => _views;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>This computer's UTC offset now, in minutes.</summary>
    public static int Offset => (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;

    /// <param name="previous">Range whose usage is compared with; null skips the comparison.</param>
    /// <param name="hidePaused">Time recorded while sharing was paused counts as paused, without what was done.</param>
    public UsageReport Report(DateOnly from, DateOnly to, (DateOnly From, DateOnly To)? previous = null, bool hidePaused = false)
    {
        Recorder.Flush();
        var report = Build(from, to, hidePaused) with { Today = Today, Offset = Offset };
        return previous is { } p ? report with { Previous = UsageAggregator.Totals(Build(p.From, p.To, hidePaused)) } : report;
    }

    /// <summary>Nothing to show for days outside what is shared, with the date and offset to show other days by.</summary>
    public static UsageReport Empty(DateOnly from, DateOnly to) => new(from, to, [], [], [], [], [], Today: Today, Offset: Offset);

    /// <param name="title">Only this title of the app; null for the whole app.</param>
    public UsageAppDetail AppDetail(DateOnly from, DateOnly to, string app, string? title)
    {
        Recorder.Flush();
        var reclassifier = _reclassifier;
        var window = UsageAggregator.Window(from, to);
        return UsageAppDetail.Build(
            _db.Segments(window.From, window.To).Select(reclassifier.Apply),
            _db.Rollups(from, to).Select(reclassifier.Apply),
            app,
            title,
            from,
            to);
    }

    /// <summary>The no-title rules changed, here or from another computer through sync.</summary>
    public event Action? SettingsChanged;

    public void SetNoTitleRules(List<NoTitleRule> rules)
    {
        _settings.NoTitleRules = rules;
        _store.Save(_settings);
        Recorder.SetNoTitleRules(rules);
        SettingsChanged?.Invoke();
    }

    public void Export(string path)
    {
        Recorder.Flush();
        using var file = File.Create(path);
        JsonSerializer.Serialize(file, _db.Export(DateTimeOffset.UtcNow), ExportOptions);
    }

    /// <exception cref="InvalidDataException">The file is not a usage export.</exception>
    public ImportResult Import(string path)
    {
        UsageExport? data;
        try
        {
            using var file = File.OpenRead(path);
            data = JsonSerializer.Deserialize<UsageExport>(file, DeskPalJson.Options);
        }
        catch (JsonException)
        {
            throw new InvalidDataException(Strings.Import_NotExport);
        }

        var result = _db.Import(data ?? throw new InvalidDataException(Strings.Import_Empty));
        DaysChanged?.Invoke();
        return result;
    }

    public void Clear()
    {
        Recorder.Flush();
        _db.Clear();
        LoadViews();
        DaysChanged?.Invoke();
    }

    public void AddView(StatsViewRecord view)
    {
        _db.AddView(view);
        LoadViews();
    }

    public void Dispose()
    {
        _prune.Dispose();
        Recorder.Flush();
        _db.Dispose();
    }

    /// <summary>The personal rule a ranking row would be saved against: the domain for sites, the process for apps.</summary>
    public Func<string, StatsRuleTarget?> RuleTargets()
    {
        var personal = _personalRules.Rules.OrderByDescending(r => r.UpdatedAt).Select(r => new RuleDto(r.Kind, r.Pattern, r.Category, r.Name));
        return UsageRuleTargets.Create(_db.ProcessesByApp(), personal.Concat(_builtinRules.Load().Rules));
    }

    private UsageReport Build(DateOnly from, DateOnly to, bool hidePaused = false)
    {
        var reclassifier = _reclassifier;
        var window = UsageAggregator.Window(from, to, UsageAggregator.DayStartHour);
        var segments = _db.Segments(window.From, window.To).Select(reclassifier.Apply);
        var rollups = _db.Rollups(from, to).Select(reclassifier.Apply);
        if (hidePaused)
        {
            segments = segments.Select(UsageStates.HidePaused);
            rollups = rollups.Select(UsageStates.HidePaused);
        }

        return UsageAggregator.Build(segments, rollups, from, to);
    }

    private void PruneViews()
    {
        try
        {
            _db.PruneViews(DateTimeOffset.UtcNow);
            LoadViews();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pruning stats views failed");
        }
    }

    private void LoadViews()
    {
        _views = _db.Views(DateTimeOffset.UtcNow - SqliteUsageStore.ViewsKeptFor);
        Changed?.Invoke();
    }
}