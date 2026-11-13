using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Stats;

/// <summary>
/// Aggregated usage of one person over a range of local days. Built on the owner's computer; never contains raw segments.
/// When it is sent to a room member it has been reduced by that room's privacy levels first.
/// </summary>
/// <param name="Days">Seconds per day, state and category. Includes away and locked time, which is not counted as usage.</param>
/// <param name="Hours">Seconds per hour of the day. Only filled when the range is a single day.</param>
/// <param name="Timeline">State spans of a single day, in seconds since local midnight. Only filled when the range is a single day.</param>
/// <param name="Apps">Usage per category and app, longest first.</param>
/// <param name="Titles">Usage per category, app and title, longest first.</param>
/// <param name="PreviousSeconds">Usage in the period of the same length right before; null when it is outside what can be shown.</param>
/// <param name="SharedDays">How many recent days the owner shares. Only set on reports received from a room member.</param>
public sealed record UsageReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<UsageTotal> Days,
    IReadOnlyList<UsageTotal> Hours,
    IReadOnlyList<UsageSpan> Timeline,
    IReadOnlyList<UsageItem> Apps,
    IReadOnlyList<UsageItem> Titles,
    long? PreviousSeconds = null,
    int? SharedDays = null);

/// <param name="Hour">0 to 23 for hourly totals, -1 for daily totals.</param>
public sealed record UsageTotal(DateOnly Date, int Hour, PresenceState State, ActivityCategory Category, long Seconds);

public sealed record UsageSpan(int Start, int End, PresenceState State);

/// <param name="Category">None when the owner shows only the state for this category.</param>
/// <param name="App">null when the app is not shared or not known.</param>
/// <param name="Title">null when the title is not shared or not recorded.</param>
public sealed record UsageItem(PresenceState State, ActivityCategory Category, string? App, string? Title, long Seconds);

/// <summary>Payload of stats.request. The requester's one-time public key protects the response from the server.</summary>
/// <param name="PublicKey">ECDH P-256 SubjectPublicKeyInfo, Base64.</param>
/// <param name="PreviousFrom">Start of the period to compare with. The owner leaves it out when it is not within the shared days.</param>
public sealed record StatsRequestPayload(Guid RequestId, DateOnly From, DateOnly To, string PublicKey, DateOnly? PreviousFrom = null, DateOnly? PreviousTo = null);

/// <summary>Payload of stats.response. Either Error, or the owner's one-time public key with the encrypted report.</summary>
/// <param name="Cipher">AES-GCM ciphertext followed by the 16-byte tag, Base64. The plain text is a Brotli-compressed <see cref="UsageReport"/> in JSON.</param>
/// <param name="Error">Text in the owner's language, for requesters that do not know <paramref name="ErrorCode"/>.</param>
/// <param name="ErrorCode">One of <see cref="StatsErrorCodes"/>; the requester shows it in its own language.</param>
public sealed record StatsResponsePayload(
    Guid RequestId,
    string? PublicKey = null,
    string? Nonce = null,
    string? Cipher = null,
    string? Error = null,
    string? ErrorCode = null);

public static class StatsErrorCodes
{
    /// <summary>The owner does not share stats in this room.</summary>
    public const string NotShared = "not-shared";

    /// <summary>The requested days are older than the owner shares.</summary>
    public const string OutOfRange = "out-of-range";
}