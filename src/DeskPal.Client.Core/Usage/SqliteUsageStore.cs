using System.Globalization;
using DeskPal.Shared.Presence;
using Microsoft.Data.Sqlite;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Usage history of one account on this computer, in a local SQLite file. Raw segments are kept for the retention
/// period; older days are folded into per-day totals that are kept indefinitely.
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
                Title TEXT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Segments_Device_Start ON Segments (DeviceId, StartAt);
            CREATE INDEX IF NOT EXISTS IX_Segments_Start ON Segments (StartAt);
            CREATE TABLE IF NOT EXISTS DailyRollups (
                DeviceId TEXT NOT NULL,
                Date TEXT NOT NULL,
                State TEXT NOT NULL,
                Category TEXT NOT NULL,
                App TEXT NOT NULL,
                Title TEXT NOT NULL,
                Seconds INTEGER NOT NULL,
                PRIMARY KEY (DeviceId, Date, State, Category, App, Title));
            CREATE TABLE IF NOT EXISTS StatsViews (
                RoomId TEXT NOT NULL,
                RoomName TEXT NOT NULL,
                ViewerId TEXT NOT NULL,
                ViewerName TEXT NOT NULL,
                At INTEGER NOT NULL);
            """);
    }

    public long Add(UsageSegment segment)
    {
        lock (_lock)
        {
            using var cmd = Command("""
                INSERT INTO Segments (DeviceId, StartAt, EndAt, State, Category, App, Title)
                VALUES ($device, $start, $end, $state, $category, $app, $title)
                ON CONFLICT (DeviceId, StartAt) DO UPDATE SET EndAt = excluded.EndAt, State = excluded.State,
                    Category = excluded.Category, App = excluded.App, Title = excluded.Title
                RETURNING Id
                """);
            BindSegment(cmd, _deviceId, segment.Start, segment.End, segment.State, segment.Category, segment.App, segment.Title);
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
            using var cmd = Command("SELECT StartAt, EndAt, State, Category, App, Title FROM Segments WHERE StartAt < $to AND EndAt > $from ORDER BY StartAt");
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            var list = new List<UsageSegment>();
            while (reader.Read())
            {
                list.Add(new UsageSegment(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    Enum.Parse<PresenceState>(reader.GetString(2)),
                    Enum.Parse<ActivityCategory>(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return list;
        }
    }

    public IReadOnlyList<UsageRollup> Rollups(DateOnly from, DateOnly to)
    {
        lock (_lock)
        {
            using var cmd = Command("SELECT Date, State, Category, App, Title, Seconds FROM DailyRollups WHERE Date >= $from AND Date <= $to");
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
                    NullIfEmpty(reader.GetString(4)),
                    reader.GetInt64(5)));
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
            var totals = new Dictionary<(string Device, string Date, string State, string Category, string App, string Title), long>();
            using (var select = Command("SELECT DeviceId, StartAt, EndAt, State, Category, App, Title FROM Segments WHERE StartAt < $cutoff"))
            {
                select.Parameters.AddWithValue("$cutoff", cutoff);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    var start = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
                    var seconds = (reader.GetInt64(2) - reader.GetInt64(1)) / 1000;
                    var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _zone).DateTime);
                    var key = (reader.GetString(0), DateText(date), reader.GetString(3), reader.GetString(4),
                        reader.IsDBNull(5) ? "" : reader.GetString(5), reader.IsDBNull(6) ? "" : reader.GetString(6));
                    totals[key] = totals.GetValueOrDefault(key) + seconds;
                }
            }

            foreach (var ((device, date, state, category, app, title), seconds) in totals)
            {
                using var insert = Command("""
                    INSERT INTO DailyRollups (DeviceId, Date, State, Category, App, Title, Seconds)
                    VALUES ($device, $date, $state, $category, $app, $title, $seconds)
                    ON CONFLICT (DeviceId, Date, State, Category, App, Title) DO UPDATE SET Seconds = Seconds + excluded.Seconds
                    """);
                insert.Parameters.AddWithValue("$device", device);
                insert.Parameters.AddWithValue("$date", date);
                insert.Parameters.AddWithValue("$state", state);
                insert.Parameters.AddWithValue("$category", category);
                insert.Parameters.AddWithValue("$app", app);
                insert.Parameters.AddWithValue("$title", title);
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
            Execute("DELETE FROM Segments; DELETE FROM DailyRollups; DELETE FROM StatsViews;");
        }
    }
    public UsageExport Export(DateTimeOffset now)
    {
        lock (_lock)
        {
            var segments = new List<ExportedSegment>();
            using (var cmd = Command("SELECT DeviceId, StartAt, EndAt, State, Category, App, Title FROM Segments ORDER BY StartAt"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    segments.Add(new ExportedSegment(
                        Guid.Parse(reader.GetString(0)),
                        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                        Enum.Parse<PresenceState>(reader.GetString(3)),
                        Enum.Parse<ActivityCategory>(reader.GetString(4)),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6)));
                }
            }

            var rollups = new List<ExportedRollup>();
            using (var cmd = Command("SELECT DeviceId, Date, State, Category, App, Title, Seconds FROM DailyRollups ORDER BY Date"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    rollups.Add(new ExportedRollup(
                        Guid.Parse(reader.GetString(0)),
                        DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Enum.Parse<PresenceState>(reader.GetString(2)),
                        Enum.Parse<ActivityCategory>(reader.GetString(3)),
                        NullIfEmpty(reader.GetString(4)),
                        NullIfEmpty(reader.GetString(5)),
                        reader.GetInt64(6)));
                }
            }

            return new UsageExport(UsageExport.CurrentVersion, _deviceId, now, segments, rollups);
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
            var segments = 0;
            foreach (var s in data.Segments)
            {
                if (s.End <= s.Start || s.End - s.Start > TimeSpan.FromDays(1))
                {
                    continue;
                }

                using var cmd = Command("""
                    INSERT OR IGNORE INTO Segments (DeviceId, StartAt, EndAt, State, Category, App, Title)
                    VALUES ($device, $start, $end, $state, $category, $app, $title)
                    """);
                BindSegment(cmd, s.DeviceId, s.Start, s.End, s.State, s.Category, s.App, s.Title);
                segments += cmd.ExecuteNonQuery();
            }

            var rollups = 0;
            foreach (var r in data.Rollups)
            {
                if (r.Seconds <= 0)
                {
                    continue;
                }

                using var cmd = Command("""
                    INSERT OR IGNORE INTO DailyRollups (DeviceId, Date, State, Category, App, Title, Seconds)
                    VALUES ($device, $date, $state, $category, $app, $title, $seconds)
                    """);
                cmd.Parameters.AddWithValue("$device", r.DeviceId.ToString());
                cmd.Parameters.AddWithValue("$date", DateText(r.Date));
                cmd.Parameters.AddWithValue("$state", r.State.ToString());
                cmd.Parameters.AddWithValue("$category", r.Category.ToString());
                cmd.Parameters.AddWithValue("$app", r.App ?? "");
                cmd.Parameters.AddWithValue("$title", r.Title ?? "");
                cmd.Parameters.AddWithValue("$seconds", r.Seconds);
                rollups += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return new ImportResult(segments, rollups);
        }
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

    private static void BindSegment(SqliteCommand cmd, Guid device, DateTimeOffset start, DateTimeOffset end, PresenceState state, ActivityCategory category, string? app, string? title)
    {
        cmd.Parameters.AddWithValue("$device", device.ToString());
        cmd.Parameters.AddWithValue("$start", start.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$end", end.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$state", state.ToString());
        cmd.Parameters.AddWithValue("$category", category.ToString());
        cmd.Parameters.AddWithValue("$app", (object?)app ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
    }

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

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}