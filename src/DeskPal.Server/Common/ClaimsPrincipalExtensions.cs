using System.Security.Claims;

namespace DeskPal.Server.Common;

public static class ClaimsPrincipalExtensions
{
    public const string UserIdClaim = "sub";

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(UserIdClaim) ?? throw new InvalidOperationException("Missing sub claim."));
}