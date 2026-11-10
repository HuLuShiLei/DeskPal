namespace DeskPal.Shared.Api;

public sealed record RegisterRequest(string UserName, string DisplayName, string Password);

public sealed record LoginRequest(string UserName, string Password);

public sealed record AuthResponse(string Token, Guid UserId, string UserName, string DisplayName);

public sealed record ApiError(string Error);