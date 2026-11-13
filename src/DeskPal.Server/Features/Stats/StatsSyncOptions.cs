namespace DeskPal.Server.Features.Stats;

public sealed class StatsSyncOptions
{
    /// <summary>Largest encrypted day accepted.</summary>
    public int MaxDayMegabytes { get; set; } = 2;

    /// <summary>Largest total of all days of one account.</summary>
    public int MaxTotalMegabytes { get; set; } = 100;
}