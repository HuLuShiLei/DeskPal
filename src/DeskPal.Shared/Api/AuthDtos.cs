namespace DeskPal.Shared.Api;

/// <param name="SignupCode">Required when the server has Auth:RequireSignupInvite on; ignored otherwise.</param>
public sealed record RegisterRequest(string UserName, string DisplayName, string Password, string? SignupCode = null);

public sealed record LoginRequest(string UserName, string Password);

/// <param name="ShareUsage">null until the user has answered whether to share usage data.</param>
/// <param name="IsAdmin">Listed in the server's Auth:AdminUserNames; may create signup invites.</param>
/// <param name="IdleMinutes">null until the account has chosen one. One of <see cref="AccountIdle.MinuteOptions"/>.</param>
public sealed record AuthResponse(string Token, Guid UserId, string UserName, string DisplayName, bool? ShareUsage = null, bool IsAdmin = false, int? IdleMinutes = null);

public sealed record UpdateDisplayNameRequest(string DisplayName);

public sealed record SetIdleMinutesRequest(int Minutes);

/// <summary>
/// Body of a failed request. The server sends no text the user reads: the client turns <paramref name="Code"/> into a message in its own language.
/// </summary>
/// <param name="Code">One of <see cref="ApiErrorCodes"/>.</param>
/// <param name="Args">Values the message needs, e.g. <c>limit</c> for <see cref="ApiErrorCodes.RoomFull"/>.</param>
public sealed record ApiError(string Code, IReadOnlyDictionary<string, string>? Args = null);

/// <summary>A signup invite as the admin sees it. Revoked invites are not listed.</summary>
/// <param name="UsedAt">null while unused.</param>
public sealed record SignupInviteDto(string Code, DateTimeOffset CreatedAt, DateTimeOffset? UsedAt = null, string? UsedByUserName = null, string? UsedByDisplayName = null);