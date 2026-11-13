namespace DeskPal.Shared.Api;

/// <summary>
/// Usage stats a user chose to keep on the server, one encrypted item per day. The key comes from a PIN that never
/// leaves the user's computers; the server only stores the salt, a key check and the encrypted days.
/// </summary>
/// <param name="Salt">Key derivation parameters and salt, opaque to the server. null when sync is off.</param>
/// <param name="KeyCheck">A known text encrypted with the key, so a client can tell whether a PIN is right.</param>
public sealed record StatsSyncStatusDto(bool Enabled, string? Salt, string? KeyCheck, IReadOnlyList<StatsSyncDayInfo> Days);

/// <param name="Revision">Increases on every upload of the account, so a client downloads only days that changed since it last saw them.</param>
public sealed record StatsSyncDayInfo(DateOnly Date, long Revision);

public sealed record EnableStatsSyncRequest(string Salt, string KeyCheck);

/// <param name="Data">Base64 of the encrypted day.</param>
public sealed record UploadStatsDayRequest(string Data);

public sealed record StatsSyncDayDto(DateOnly Date, long Revision, string Data);