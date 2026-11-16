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
    private static readonly TimeSpan CompactEvery = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions ExportOptions = new(DeskPalJson.Options) { WriteIndented = true };

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ILogger<UsageService> _logger;
    private readonly SqliteUsageStore _db;
    private readonly Timer _compact;
    private IReadOnlyList<StatsViewRecord> _views = [];
    private volatile UsageReclassifier _reclassifier = UsageReclassifier.None;

    public UsageService(AppSettings settings, SettingsStore store, PersonalRules personalRules, ILogger<UsageService> logger)
    {
        _settings = settings;
        _store = store;
        _logger = logger;
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
        _compact = new Timer(_ => Compact(), null, TimeSpan.FromSeconds(30), CompactEvery);
        LoadViews();
    }

    /// <summary>Raised when the view records change.</summary>
    public event Action? Changed;

    /// <summary>Raised when past days may have changed: import, folding old days, clearing.</summary>
    public event Action? DaysChanged;

    public UsageRecorder Recorder { get; }

    public SqliteUsageStore Store => _db;

    /// <summary>Who viewed my stats in the last 7 days, latest first.</summary>
    public IReadOnlyList<StatsViewRecord> RecentViews => _views;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <param name="previous">Range whose usage is compared with; null skips the comparison.</param>
    public UsageReport Report(DateOnly from, DateOnly to, (DateOnly From, DateOnly To)? previous = null)
    {
        Recorder.Flush();
        var report = Build(from, to);
        return previous is { } p ? report with { PreviousSeconds = UsageAggregator.ActiveSeconds(Build(p.From, p.To)) } : report;
    }

    /// <summary>The retention or the no-title rules changed, here or from another computer through sync.</summary>
    public event Action? SettingsChanged;

    public void SetRetention(int days)
    {
        _settings.UsageRetentionDays = days;
        _store.Save(_settings);
        Compact();
        SettingsChanged?.Invoke();
    }

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

    /// <exception cref="InvalidDataException">The file is too large or not a usage export.</exception>
    public ImportResult Import(string path)
    {
        if (new FileInfo(path).Length > UsageExport.MaxFileBytes)
        {
            throw new InvalidDataException(Strings.Import_TooLarge);
        }

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
        Compact();
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
        _compact.Dispose();
        Recorder.Flush();
        _db.Dispose();
    }

    /// <summary>The personal rule a ranking row of the range would be saved against: the domain for sites, the process for apps.</summary>
    public Func<string, StatsRuleTarget?> RuleTargets(DateOnly from, DateOnly to)
    {
        var zone = TimeZoneInfo.Local;
        var processes = _db.ProcessesByApp(UsageAggregator.LocalMidnight(from, zone), UsageAggregator.LocalMidnight(to.AddDays(1), zone));
        return app =>
        {
            var process = processes.GetValueOrDefault(app);
            if (process is null || Browsers.IsBrowser(process))
            {
                var host = RulePattern.NormalizeHost(app);
                return host == app && host.Contains('.') ? new StatsRuleTarget(nameof(RuleKind.Domain), host, app) : null;
            }

            return new StatsRuleTarget(nameof(RuleKind.Process), RulePattern.Normalize(RuleKind.Process, process), app);
        };
    }

    private UsageReport Build(DateOnly from, DateOnly to)
    {
        var zone = TimeZoneInfo.Local;
        var reclassifier = _reclassifier;
        return UsageAggregator.Build(
            _db.Segments(UsageAggregator.LocalMidnight(from, zone), UsageAggregator.LocalMidnight(to.AddDays(1), zone)).Select(reclassifier.Apply),
            _db.Rollups(from, to).Select(reclassifier.Apply),
            from,
            to,
            zone);
    }

    private void Compact()
    {
        try
        {
            var folded = _db.Compact(Today.AddDays(1 - _settings.UsageRetentionDays), DateTimeOffset.UtcNow);
            if (folded > 0)
            {
                _logger.LogInformation("Folded {Count} usage segments into daily totals", folded);
                DaysChanged?.Invoke();
            }

            LoadViews();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Compacting usage failed");
        }
    }

    private void LoadViews()
    {
        _views = _db.Views(DateTimeOffset.UtcNow - SqliteUsageStore.ViewsKeptFor);
        Changed?.Invoke();
    }
}