namespace DeskPal.Shared.Api;

public sealed record RegisterRequest(string UserName, string DisplayName, string Password);

public sealed record LoginRequest(string UserName, string Password);

/// <param name="ShareUsage">null until the user has answered whether to share usage data.</param>
public sealed record AuthResponse(string Token, Guid UserId, string UserName, string DisplayName, bool? ShareUsage = null);

public sealed record UpdateDisplayNameRequest(string DisplayName);

/// <summary>
/// Body of a failed request. The server sends no text the user reads: the client turns <paramref name="Code"/> into a message in its own language.
/// </summary>
/// <param name="Code">One of <see cref="ApiErrorCodes"/>.</param>
/// <param name="Args">Values the message needs, e.g. <c>limit</c> for <see cref="ApiErrorCodes.RoomFull"/>.</param>
public sealed record ApiError(string Code, IReadOnlyDictionary<string, string>? Args = null);