namespace DeskPal.Server.Features.Stats;

public sealed class StatsSyncOptions
{
    /// <summary>Comma-separated user names that may use server sync. Set from StatsSync__AllowedUserNames; empty allows nobody.</summary>
    public string AllowedUserNames { get; set; } = "";

    public bool IsAllowed(string userName) =>
        AllowedUserNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(n => string.Equals(n, userName, StringComparison.OrdinalIgnoreCase));
}