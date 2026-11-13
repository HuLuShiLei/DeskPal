using System.IO;
using System.Text.Json;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared;
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

    public UsageService(AppSettings settings, SettingsStore store, ILogger<UsageService> logger)
    {
        _settings = settings;
        _store = store;
        _logger = logger;
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

    public UsageRecorder Recorder { get; }

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

    public void SetRetention(int days)
    {
        _settings.UsageRetentionDays = days;
        _store.Save(_settings);
        Compact();
    }

    public void SetNoTitleRules(List<NoTitleRule> rules)
    {
        _settings.NoTitleRules = rules;
        _store.Save(_settings);
        Recorder.SetNoTitleRules(rules);
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
        return result;
    }

    public void Clear()
    {
        Recorder.Flush();
        _db.Clear();
        LoadViews();
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

    private UsageReport Build(DateOnly from, DateOnly to)
    {
        var zone = TimeZoneInfo.Local;
        return UsageAggregator.Build(
            _db.Segments(UsageAggregator.LocalMidnight(from, zone), UsageAggregator.LocalMidnight(to.AddDays(1), zone)),
            _db.Rollups(from, to),
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