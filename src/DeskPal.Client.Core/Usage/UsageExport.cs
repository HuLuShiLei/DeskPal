using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>File written by "导出" and read by "导入". Every item carries the computer it was recorded on, so importing twice does not count twice.</summary>
/// <param name="DeviceId">Computer that wrote the file.</param>
public sealed record UsageExport(
    int SchemaVersion,
    Guid DeviceId,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportedSegment> Segments,
    IReadOnlyList<ExportedRollup> Rollups)
{
    public const int CurrentVersion = 1;

    /// <summary>Larger files are refused before reading.</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;
}

public sealed record ExportedSegment(Guid DeviceId, DateTimeOffset Start, DateTimeOffset End, PresenceState State, ActivityCategory Category, string? App, string? Title);

public sealed record ExportedRollup(Guid DeviceId, DateOnly Date, PresenceState State, ActivityCategory Category, string? App, string? Title, long Seconds);

/// <summary>Someone in a room viewed my stats. Kept on this computer for <see cref="SqliteUsageStore.ViewsKeptFor"/>.</summary>
public sealed record StatsViewRecord(Guid RoomId, string RoomName, Guid ViewerId, string ViewerName, DateTimeOffset At);

public sealed record ImportResult(int Segments, int Rollups);