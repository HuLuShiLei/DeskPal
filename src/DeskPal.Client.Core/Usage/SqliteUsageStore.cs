using System.Globalization;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Presence;
using Microsoft.Data.Sqlite;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Usage history of one account on this computer, in a local SQLite file. Raw segments are kept indefinitely. Per-day
/// totals hold days that older versions or other computers folded.
/// </summary>
public sealed class SqliteUsageStore : IUsageWriter, IDisposable
{
    public static readonly TimeSpan ViewsKeptFor = TimeSpan.FromDays(7);

    private readonly SqliteConnection _db;
    private readonly Guid _deviceId;
    private readonly TimeZoneInfo _zone;
    private readonly Lock _lock = new();

    public SqliteUsageStore(string path, Guid deviceId, TimeZoneInfo? zone = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _deviceId = deviceId;
        _zone = zone ?? TimeZoneInfo.Local;
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS Segments (
                Id INTEGER PRIMARY KEY,
                DeviceId TEXT NOT NULL,
                StartAt INTEGER NOT NULL,
                EndAt INTEGER NOT NULL,
                State TEXT NOT NULL,
                Category TEXT NOT NULL,
                App TEXT NULL,
                Title TEXT NULL,
                Process TEXT NULL,
                BaseCategory TEXT NULL,
                Offset INTEGER NULL,
                Paused INTEGER NOT NULL DEFAULT 0);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Segments_Device_Start ON Segments (DeviceId, StartAt);
            CREATE INDEX IF NOT EXISTS IX_Segments_Start ON Segments (StartAt);
            CREATE TABLE IF NOT EXISTS DailyRollups (
                DeviceId TEXT NOT NULL,
                Date TEXT NOT NULL,
                State TEXT NOT NULL,
                Category TEXT NOT NULL,
                App TEXT NOT NULL,
                Title TEXT NOT NULL,
                Process TEXT NOT NULL DEFAULT '',
                BaseCategory TEXT NOT NULL DEFAULT '',
                Seconds INTEGER NOT NULL,
                Paused INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Paused));
            CREATE TABLE IF NOT EXISTS StatsViews (
                RoomId TEXT NOT NULL,
                RoomName TEXT NOT NULL,
                ViewerId TEXT NOT NULL,
                ViewerName TEXT NOT NULL,
                At INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS SyncDays (
                Date TEXT PRIMARY KEY,
                Hash TEXT NULL,
                Revision INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS SyncSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Hash TEXT NULL,
                Revision INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS FolderWritten (
                Date TEXT PRIMARY KEY,
                Hash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS FolderRead (
                DeviceId TEXT NOT NULL,
                Date TEXT NOT NULL,
                Stamp TEXT NOT NULL,
                PRIMARY KEY (DeviceId, Date));
            """);
        Upgrade();
    }

    /// <summary>Databases written before process and base category were kept get the new columns.</summary>
    private void Upgrade()
    {
        if (!HasColumn("Segments", "Process"))
        {
            Execute("ALTER TABLE Segments ADD COLUMN Process TEXT NULL; ALTER TABLE Segments ADD COLUMN BaseCategory TEXT NULL;");
        }

        if (!HasColumn("DailyRollups", "Process"))
        {
            // The primary key changes, which SQLite can only do by copying the table.
            Execute("""
                BEGIN;
                CREATE TABLE DailyRollupsNew (
                    DeviceId TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    State TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    App TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Process TEXT NOT NULL DEFAULT '',
                    BaseCategory TEXT NOT NULL DEFAULT '',
                    Seconds INTEGER NOT NULL,
                    PRIMARY KEY (DeviceId, Date, State, Category, App, Title, Process, BaseCategory));
                INSERT INTO DailyRollupsNew (DeviceId, Date, State, Category, App, Title, Seconds)
                    SELECT DeviceId, Date, State, Category, App, Title, Seconds FROM DailyRollups;
                DROP TABLE DailyRollups;
                ALTER TABLE DailyRollupsNew RENAME TO DailyRollups;
                COMMIT;
                """);
        }

        if (!HasColumn("Segments", "Offset"))
        {
            Execute("ALTER TABLE Segments ADD COLUMN Offset INTEGER NULL;");
        }

        if (!HasColumn("Segments", "Paused"))
        {
            Execute("ALTER TABLE Segments ADD COLUMN Paused INTEGER NOT NULL DEFAULT 0;");
        }

        if (!HasColumn("DailyRollups", "Paused"))
        {
            Execute("""
                BEGIN;
                CREATE TABLE DailyRollupsNew (
                    DeviceId TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    State TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    App TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Process TEXT NOT NULL DEFAULT '',
                    BaseCategory TEXT NOT NULL DEFAULT '',
                    Seconds INTEGER NOT NULL,
                    Paused INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Paused));
                INSERT INTO DailyRollupsNew (DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Seconds)
                    SELECT DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Seconds FROM DailyRollups;
                DROP TABLE DailyRollups;
                ALTER TABLE DailyRollupsNew RENAME TO DailyRollups;
                COMMIT;
                """);
        }

        // Segments recorded before the offset was kept are taken as recorded in this computer's time zone.
        var missing = new List<(long Id, long Start)>();
        using (var select = Command("SELECT Id, StartAt FROM Segments WHERE Offset IS NULL"))
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                missing.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        using var tx = _db.BeginTransaction();
        foreach (var (id, start) in missing)
        {
            using var update = Command("UPDATE Segments SET Offset = $offset WHERE Id = $id");
            update.Parameters.AddWithValue("$offset", (long)_zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(start)).TotalMinutes);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private bool HasColumn(string table, string column)
    {
        using var cmd = Command($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column");
        cmd.Parameters.AddWithValue("$column", column);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>This computer. Segments and totals recorded here carry it.</summary>
    public Guid DeviceId => _deviceId;

    public long Add(UsageSegment segment)
    {
        lock (_lock)
        {
            using var cmd = Command("""
                INSERT INTO Segments (DeviceId, StartAt, EndAt, State, Category, App, Title, Process, BaseCategory, Offset, Paused)
                VALUES ($device, $start, $end, $state, $category, $app, $title, $process, $base, $offset, $paused)
                ON CONFLICT (DeviceId, StartAt) DO UPDATE SET EndAt = excluded.EndAt, State = excluded.State,
                    Category = excluded.Category, App = excluded.App, Title = excluded.Title,
                    Process = excluded.Process, BaseCategory = excluded.BaseCategory, Offset = excluded.Offset, Paused = excluded.Paused
                RETURNING Id
                """);
            BindSegment(cmd, _deviceId, segment.Start, segment.End, segment.State, segment.Category, segment.App, segment.Title, segment.Process, segment.BaseCategory, segment.Paused);
            return (long)cmd.ExecuteScalar()!;
        }
    }

    public void UpdateEnd(long id, DateTimeOffset end)
    {
        lock (_lock)
        {
            using var cmd = Command("UPDATE Segments SET EndAt = $end WHERE Id = $id");
            cmd.Parameters.AddWithValue("$end", end.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Segments that overlap the range, from every computer.</summary>
    public IReadOnlyList<UsageSegment> Segments(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT StartAt, EndAt, State, Category, App, Title, Process, BaseCategory, Offset, Paused FROM Segments WHERE StartAt < $to AND EndAt > $from ORDER BY StartAt");
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            var list = new List<UsageSegment>();
            while (reader.Read())
            {
                var offset = TimeSpan.FromMinutes(reader.GetInt64(8));
                list.Add(new UsageSegment(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)).ToOffset(offset),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).ToOffset(offset),
                    Enum.Parse<PresenceState>(reader.GetString(2)),
                    Enum.Parse<ActivityCategory>(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    TitleText.Clean(reader.IsDBNull(5) ? null : reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    ParseCategory(reader.IsDBNull(7) ? null : reader.GetString(7)),
                    reader.GetInt64(9) != 0));
            }

            return list;
        }
    }

    /// <summary>For each app name ever recorded with a process, the process it was recorded with the longest.</summary>
    public IReadOnlyDictionary<string, string> ProcessesByApp()
    {
        lock (_lock)
        {
            using var cmd = Command("""
                SELECT App, Process, SUM(Seconds) AS Total FROM (
                    SELECT App, Process, (EndAt - StartAt) / 1000 AS Seconds FROM Segments WHERE App IS NOT NULL AND Process IS NOT NULL
                    UNION ALL
                    SELECT App, Process, Seconds FROM DailyRollups WHERE App <> '' AND Process <> '')
                GROUP BY App, Process ORDER BY Total DESC
                """);
            using var reader = cmd.ExecuteReader();
            var processes = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                processes.TryAdd(reader.GetString(0), reader.GetString(1));
            }

            return processes;
        }
    }

    public IReadOnlyList<UsageRollup> Rollups(DateOnly from, DateOnly to)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT Date, State, Category, App, Title, Seconds, Process, BaseCategory, Paused FROM DailyRollups WHERE Date >= $from AND Date <= $to");
            cmd.Parameters.AddWithValue("$from", DateText(from));
            cmd.Parameters.AddWithValue("$to", DateText(to));
            using var reader = cmd.ExecuteReader();
            var list = new List<UsageRollup>();
            while (reader.Read())
            {
                list.Add(new UsageRollup(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Enum.Parse<PresenceState>(reader.GetString(1)),
                    Enum.Parse<ActivityCategory>(reader.GetString(2)),
                    NullIfEmpty(reader.GetString(3)),
                    TitleText.Clean(reader.GetString(4)),
                    reader.GetInt64(5),
                    NullIfEmpty(reader.GetString(6)),
                    ParseCategory(reader.GetString(7)),
                    reader.GetInt64(8) != 0));
            }

            return list;
        }
    }

    /// <summary>Folds segments of days before <paramref name="keepFrom"/> into per-day totals and deletes them. Also drops old view records.</summary>
    /// <returns>Number of segments folded.</returns>
    public int Compact(DateOnly keepFrom, DateTimeOffset now)
    {
        lock (_lock)
        {
            var cutoff = LocalMidnight(keepFrom).ToUnixTimeMilliseconds();
            using var tx = _db.BeginTransaction();
            var totals = new Dictionary<(string Device, string Date, string State, string Category, string App, string Title, string Process, string Base, long Paused), long>();
            using (var select = Command("SELECT DeviceId, StartAt, EndAt, State, Category, App, Title, Process, BaseCategory, Offset, Paused FROM Segments WHERE StartAt < $cutoff"))
            {
                select.Parameters.AddWithValue("$cutoff", cutoff);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    var seconds = (reader.GetInt64(2) - reader.GetInt64(1)) / 1000;
                    var date = LocalDate(reader.GetInt64(1), reader.GetInt64(9));
                    var key = (reader.GetString(0), DateText(date), reader.GetString(3), reader.GetString(4),
                        reader.IsDBNull(5) ? "" : reader.GetString(5), reader.IsDBNull(6) ? "" : reader.GetString(6),
                        reader.IsDBNull(7) ? "" : reader.GetString(7), reader.IsDBNull(8) ? "" : reader.GetString(8), reader.GetInt64(10));
                    totals[key] = totals.GetValueOrDefault(key) + seconds;
                }
            }

            foreach (var ((device, date, state, category, app, title, process, baseCategory, paused), seconds) in totals)
            {
                using var insert = Command("""
                    INSERT INTO DailyRollups (DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Seconds, Paused)
                    VALUES ($device, $date, $state, $category, $app, $title, $process, $base, $seconds, $paused)
                    ON CONFLICT (DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Paused) DO UPDATE SET Seconds = Seconds + excluded.Seconds
                    """);
                insert.Parameters.AddWithValue("$paused", paused);
                insert.Parameters.AddWithValue("$device", device);
                insert.Parameters.AddWithValue("$date", date);
                insert.Parameters.AddWithValue("$state", state);
                insert.Parameters.AddWithValue("$category", category);
                insert.Parameters.AddWithValue("$app", app);
                insert.Parameters.AddWithValue("$title", title);
                insert.Parameters.AddWithValue("$process", process);
                insert.Parameters.AddWithValue("$base", baseCategory);
                insert.Parameters.AddWithValue("$seconds", seconds);
                insert.ExecuteNonQuery();
            }

            int folded;
            using (var delete = Command("DELETE FROM Segments WHERE StartAt < $cutoff"))
            {
                delete.Parameters.AddWithValue("$cutoff", cutoff);
                folded = delete.ExecuteNonQuery();
            }

            using (var views = Command("DELETE FROM StatsViews WHERE At < $before"))
            {
                views.Parameters.AddWithValue("$before", (now - ViewsKeptFor).ToUnixTimeMilliseconds());
                views.ExecuteNonQuery();
            }

            tx.Commit();
            return folded;
        }
    }

    /// <summary>Drops view records older than <see cref="ViewsKeptFor"/>. Segments are kept.</summary>
    public void PruneViews(DateTimeOffset now)
    {
        lock (_lock)
        {
            using var views = Command("DELETE FROM StatsViews WHERE At < $before");
            views.Parameters.AddWithValue("$before", (now - ViewsKeptFor).ToUnixTimeMilliseconds());
            views.ExecuteNonQuery();
        }
    }

    public void AddView(StatsViewRecord view)
    {
        lock (_lock)
        {
            using var cmd = Command("INSERT INTO StatsViews (RoomId, RoomName, ViewerId, ViewerName, At) VALUES ($room, $roomName, $viewer, $viewerName, $at)");
            cmd.Parameters.AddWithValue("$room", view.RoomId.ToString());
            cmd.Parameters.AddWithValue("$roomName", view.RoomName);
            cmd.Parameters.AddWithValue("$viewer", view.ViewerId.ToString());
            cmd.Parameters.AddWithValue("$viewerName", view.ViewerName);
            cmd.Parameters.AddWithValue("$at", view.At.ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Views since <paramref name="since"/>, latest first.</summary>
    public IReadOnlyList<StatsViewRecord> Views(DateTimeOffset since)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT RoomId, RoomName, ViewerId, ViewerName, At FROM StatsViews WHERE At >= $since ORDER BY At DESC");
            cmd.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            var list = new List<StatsViewRecord>();
            while (reader.Read())
            {
                list.Add(new StatsViewRecord(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    Guid.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
            }

            return list;
        }
    }

    /// <summary>Deletes all recorded usage and view records of this account on this computer.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Execute("DELETE FROM Segments; DELETE FROM DailyRollups; DELETE FROM StatsViews; DELETE FROM SyncDays; DELETE FROM FolderWritten; DELETE FROM FolderRead;");
        }
    }

    public UsageExport Export(DateTimeOffset now)
    {
        lock (_lock)
        {
            using var segments = Command($"SELECT {SegmentColumns} FROM Segments ORDER BY StartAt");
            using var rollups = Command($"SELECT {RollupColumns} FROM DailyRollups ORDER BY Date");
            return new UsageExport(UsageExport.CurrentVersion, _deviceId, now, ReadSegments(segments), ReadRollups(rollups));
        }
    }

    /// <summary>
    /// Everything recorded on one local day, for sync. The same content always gives the same file: no export time,
    /// fixed order.
    /// </summary>
    /// <param name="date">Local day of the recording, whatever this computer's time zone is now.</param>
    /// <param name="device">Only what this computer recorded; null for every computer.</param>
    public UsageExport ExportDay(DateOnly date, Guid? device = null)
    {
        lock (_lock)
        {
            using var segments = Command($"""
                SELECT {SegmentColumns} FROM Segments
                WHERE {RecordedOn} AND ($device IS NULL OR DeviceId = $device) ORDER BY StartAt, DeviceId
                """);
            BindDay(segments, date);
            segments.Parameters.AddWithValue("$device", (object?)device?.ToString() ?? DBNull.Value);
            using var rollups = Command($"""
                SELECT {RollupColumns} FROM DailyRollups
                WHERE Date = $date AND ($device IS NULL OR DeviceId = $device) ORDER BY DeviceId, State, Category, App, Title, Process, BaseCategory, Paused
                """);
            rollups.Parameters.AddWithValue("$date", DateText(date));
            rollups.Parameters.AddWithValue("$device", (object?)device?.ToString() ?? DBNull.Value);
            return new UsageExport(UsageExport.CurrentVersion, device ?? Guid.Empty, DateTimeOffset.UnixEpoch, ReadSegments(segments), ReadRollups(rollups));
        }
    }

    /// <summary>Local days that have any segment or daily total.</summary>
    /// <param name="device">Only days with something this computer recorded; null for every computer.</param>
    public IReadOnlyList<DateOnly> Dates(Guid? device = null)
    {
        lock (_lock)
        {
            var dates = new HashSet<DateOnly>();
            using (var cmd = Command("SELECT StartAt, Offset FROM Segments WHERE $device IS NULL OR DeviceId = $device"))
            {
                cmd.Parameters.AddWithValue("$device", (object?)device?.ToString() ?? DBNull.Value);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    dates.Add(LocalDate(reader.GetInt64(0), reader.GetInt64(1)));
                }
            }

            using (var cmd = Command("SELECT DISTINCT Date FROM DailyRollups WHERE $device IS NULL OR DeviceId = $device"))
            {
                cmd.Parameters.AddWithValue("$device", (object?)device?.ToString() ?? DBNull.Value);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    dates.Add(ParseDate(reader.GetString(0)));
                }
            }

            return dates.Order().ToList();
        }
    }

    /// <summary>What was last uploaded or downloaded for each day.</summary>
    public IReadOnlyDictionary<DateOnly, SyncMark> SyncMarks()
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT Date, Hash, Revision FROM SyncDays");
            using var reader = cmd.ExecuteReader();
            var marks = new Dictionary<DateOnly, SyncMark>();
            while (reader.Read())
            {
                marks[ParseDate(reader.GetString(0))] = new SyncMark(reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2));
            }

            return marks;
        }
    }

    public void SetSyncMark(DateOnly date, SyncMark mark)
    {
        lock (_lock)
        {
            using var cmd = Command("""
                INSERT INTO SyncDays (Date, Hash, Revision) VALUES ($date, $hash, $revision)
                ON CONFLICT (Date) DO UPDATE SET Hash = excluded.Hash, Revision = excluded.Revision
                """);
            cmd.Parameters.AddWithValue("$date", DateText(date));
            cmd.Parameters.AddWithValue("$hash", (object?)mark.Hash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$revision", mark.Revision);
            cmd.ExecuteNonQuery();
        }
    }


    /// <summary>Forgets what was synced with the server, after sync was turned off or on again.</summary>
    public void ResetSync()
    {
        lock (_lock)
        {
            Execute("DELETE FROM SyncDays; DELETE FROM SyncSettings;");
        }
    }

    /// <summary>Hash of what this computer last wrote to the sync folder, per day.</summary>
    public IReadOnlyDictionary<DateOnly, string> FolderWritten()
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT Date, Hash FROM FolderWritten");
            using var reader = cmd.ExecuteReader();
            var hashes = new Dictionary<DateOnly, string>();
            while (reader.Read())
            {
                hashes[ParseDate(reader.GetString(0))] = reader.GetString(1);
            }

            return hashes;
        }
    }

    public void SetFolderWritten(DateOnly date, string hash)
    {
        lock (_lock)
        {
            using var cmd = Command("INSERT INTO FolderWritten (Date, Hash) VALUES ($date, $hash) ON CONFLICT (Date) DO UPDATE SET Hash = excluded.Hash");
            cmd.Parameters.AddWithValue("$date", DateText(date));
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Size and modification time of the files of other computers last read from the sync folder.</summary>
    public IReadOnlyDictionary<(Guid Device, DateOnly Date), string> FolderRead()
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT DeviceId, Date, Stamp FROM FolderRead");
            using var reader = cmd.ExecuteReader();
            var stamps = new Dictionary<(Guid, DateOnly), string>();
            while (reader.Read())
            {
                stamps[(Guid.Parse(reader.GetString(0)), ParseDate(reader.GetString(1)))] = reader.GetString(2);
            }

            return stamps;
        }
    }

    public void SetFolderRead(Guid device, DateOnly date, string stamp)
    {
        lock (_lock)
        {
            using var cmd = Command("""
                INSERT INTO FolderRead (DeviceId, Date, Stamp) VALUES ($device, $date, $stamp)
                ON CONFLICT (DeviceId, Date) DO UPDATE SET Stamp = excluded.Stamp
                """);
            cmd.Parameters.AddWithValue("$device", device.ToString());
            cmd.Parameters.AddWithValue("$date", DateText(date));
            cmd.Parameters.AddWithValue("$stamp", stamp);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Forgets what was written to and read from the sync folder.</summary>
    public void ResetFolderSync()
    {
        lock (_lock)
        {
            Execute("DELETE FROM FolderWritten; DELETE FROM FolderRead;");
        }
    }

    /// <summary>
    /// Takes a synced day that may hold several computers. For each other computer in it, what is here for that computer
    /// and day is replaced, so a segment that grew there or a day folded into totals there is not counted twice. What
    /// this computer recorded is never replaced.
    /// </summary>
    public void MergeDay(DateOnly date, UsageExport day)
    {
        var devices = day.Segments.Select(s => s.DeviceId).Concat(day.Rollups.Select(r => r.DeviceId)).Distinct().Where(d => d != _deviceId).ToList();
        foreach (var device in devices)
        {
            ReplaceDay(date, device, day);
        }
    }

    /// <summary>Replaces what is here for <paramref name="device"/> on <paramref name="date"/> with what <paramref name="day"/> has for it.</summary>
    /// <exception cref="ArgumentException"><paramref name="device"/> is this computer.</exception>
    public void ReplaceDay(DateOnly date, Guid device, UsageExport day)
    {
        if (device == _deviceId)
        {
            throw new ArgumentException("This computer's records are never replaced", nameof(device));
        }

        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using (var delete = Command($"DELETE FROM Segments WHERE DeviceId = $device AND {RecordedOn}"))
            {
                delete.Parameters.AddWithValue("$device", device.ToString());
                BindDay(delete, date);
                delete.ExecuteNonQuery();
            }

            using (var delete = Command("DELETE FROM DailyRollups WHERE DeviceId = $device AND Date = $date"))
            {
                delete.Parameters.AddWithValue("$device", device.ToString());
                delete.Parameters.AddWithValue("$date", DateText(date));
                delete.ExecuteNonQuery();
            }

            InsertAll(
                day.Segments.Where(s => s.DeviceId == device && DateOnly.FromDateTime(s.Start.DateTime) == date),
                day.Rollups.Where(r => r.DeviceId == device && r.Date == date));
            tx.Commit();
        }
    }

    /// <summary>Adds what is not here yet. Segments are matched by computer and start time, totals by computer, day and key.</summary>
    /// <exception cref="InvalidDataException">The file is not a usage export of a supported version.</exception>
    public ImportResult Import(UsageExport data)
    {
        if (data.SchemaVersion != UsageExport.CurrentVersion || data.Segments is null || data.Rollups is null)
        {
            throw new InvalidDataException(Localization.Strings.Import_Unsupported);
        }

        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            var result = InsertAll(data.Segments, data.Rollups);
            tx.Commit();
            return result;
        }
    }

    /// <summary>Adds what is not here yet, inside the caller's lock and transaction.</summary>
    private ImportResult InsertAll(IEnumerable<ExportedSegment> segmentList, IEnumerable<ExportedRollup> rollupList)
    {
        var segments = 0;
        foreach (var s in segmentList)
        {
            if (s.End <= s.Start || s.End - s.Start > TimeSpan.FromDays(1))
            {
                continue;
            }

            using var cmd = Command("""
                INSERT OR IGNORE INTO Segments (DeviceId, StartAt, EndAt, State, Category, App, Title, Process, BaseCategory, Offset, Paused)
                VALUES ($device, $start, $end, $state, $category, $app, $title, $process, $base, $offset, $paused)
                """);
            BindSegment(cmd, s.DeviceId, s.Start, s.End, s.State, s.Category, s.App, s.Title, s.Process, s.BaseCategory, s.Paused);
            segments += cmd.ExecuteNonQuery();
        }

        var rollups = 0;
        foreach (var r in rollupList)
        {
            if (r.Seconds <= 0)
            {
                continue;
            }

            using var cmd = Command("""
                INSERT OR IGNORE INTO DailyRollups (DeviceId, Date, State, Category, App, Title, Process, BaseCategory, Seconds, Paused)
                VALUES ($device, $date, $state, $category, $app, $title, $process, $base, $seconds, $paused)
                """);
            cmd.Parameters.AddWithValue("$paused", r.Paused ? 1 : 0);
            cmd.Parameters.AddWithValue("$device", r.DeviceId.ToString());
            cmd.Parameters.AddWithValue("$date", DateText(r.Date));
            cmd.Parameters.AddWithValue("$state", r.State.ToString());
            cmd.Parameters.AddWithValue("$category", r.Category.ToString());
            cmd.Parameters.AddWithValue("$app", r.App ?? "");
            cmd.Parameters.AddWithValue("$title", r.Title ?? "");
            cmd.Parameters.AddWithValue("$process", r.Process ?? "");
            cmd.Parameters.AddWithValue("$base", r.BaseCategory?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$seconds", r.Seconds);
            rollups += cmd.ExecuteNonQuery();
        }

        return new ImportResult(segments, rollups);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _db.Dispose();
        }
    }

    private DateTimeOffset LocalMidnight(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    /// <summary>Offsets run from UTC−12 to UTC+14; the start is narrowed with these first so the index on StartAt is used.</summary>
    private static readonly TimeSpan MaxOffsetBehind = TimeSpan.FromHours(12);

    private static readonly TimeSpan MaxOffsetAhead = TimeSpan.FromHours(14);

    /// <summary>Segments whose start falls on the local day bound with <see cref="BindDay"/>, in the offset they were recorded at.</summary>
    private const string RecordedOn = "StartAt >= $from AND StartAt < $to AND StartAt + Offset * 60000 >= $localFrom AND StartAt + Offset * 60000 < $localTo";

    private static void BindDay(SqliteCommand cmd, DateOnly date)
    {
        var localFrom = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var localTo = localFrom.AddDays(1);
        cmd.Parameters.AddWithValue("$from", (localFrom - MaxOffsetAhead).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$to", (localTo + MaxOffsetBehind).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$localFrom", localFrom.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$localTo", localTo.ToUnixTimeMilliseconds());
    }

    private static DateOnly LocalDate(long startAt, long offsetMinutes) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(startAt).ToOffset(TimeSpan.FromMinutes(offsetMinutes)).DateTime);

    private const string SegmentColumns = "DeviceId, StartAt, EndAt, State, Category, App, Title, Process, BaseCategory, Offset, Paused";

    private const string RollupColumns = "DeviceId, Date, State, Category, App, Title, Seconds, Process, BaseCategory, Paused";

    private static void BindSegment(SqliteCommand cmd, Guid device, DateTimeOffset start, DateTimeOffset end, PresenceState state, ActivityCategory category, string? app, string? title, string? process, ActivityCategory? baseCategory, bool paused)
    {
        cmd.Parameters.AddWithValue("$paused", paused ? 1 : 0);
        cmd.Parameters.AddWithValue("$device", device.ToString());
        cmd.Parameters.AddWithValue("$start", start.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$end", end.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$state", state.ToString());
        cmd.Parameters.AddWithValue("$category", category.ToString());
        cmd.Parameters.AddWithValue("$app", (object?)app ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$process", (object?)process ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$base", (object?)baseCategory?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$offset", (long)start.Offset.TotalMinutes);
    }

    private static ActivityCategory? ParseCategory(string? text) =>
        Enum.TryParse<ActivityCategory>(text, out var category) ? category : null;

    private SqliteCommand Command(string sql)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private void Execute(string sql)
    {
        using var cmd = Command(sql);
        cmd.ExecuteNonQuery();
    }

    private static List<ExportedSegment> ReadSegments(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var segments = new List<ExportedSegment>();
        while (reader.Read())
        {
            var offset = TimeSpan.FromMinutes(reader.GetInt64(9));
            segments.Add(new ExportedSegment(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).ToOffset(offset),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)).ToOffset(offset),
                Enum.Parse<PresenceState>(reader.GetString(3)),
                Enum.Parse<ActivityCategory>(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                ParseCategory(reader.IsDBNull(8) ? null : reader.GetString(8)),
                reader.GetInt64(10) != 0));
        }

        return segments;
    }

    private static List<ExportedRollup> ReadRollups(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var rollups = new List<ExportedRollup>();
        while (reader.Read())
        {
            rollups.Add(new ExportedRollup(
                Guid.Parse(reader.GetString(0)),
                ParseDate(reader.GetString(1)),
                Enum.Parse<PresenceState>(reader.GetString(2)),
                Enum.Parse<ActivityCategory>(reader.GetString(3)),
                NullIfEmpty(reader.GetString(4)),
                NullIfEmpty(reader.GetString(5)),
                reader.GetInt64(6),
                NullIfEmpty(reader.GetString(7)),
                ParseCategory(reader.GetString(8)),
                reader.GetInt64(9) != 0));
        }

        return rollups;
    }

    private static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}