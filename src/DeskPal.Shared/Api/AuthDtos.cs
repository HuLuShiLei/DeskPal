namespace DeskPal.Shared.Api;

public sealed record RegisterRequest(string UserName, string DisplayName, string Password);

public sealed record LoginRequest(string UserName, string Password);

/// <param name="ShareUsage">null until the user has answered whether to share usage data.</param>
public sealed record AuthResponse(string Token, Guid UserId, string UserName, string DisplayName, bool? ShareUsage = null);

public sealed record UpdateDisplayNameRequest(string DisplayName);

public sealed record ApiError(string Error);