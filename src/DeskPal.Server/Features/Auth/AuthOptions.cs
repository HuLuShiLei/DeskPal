namespace DeskPal.Server.Features.Auth;

public sealed class AuthOptions
{
    /// <summary>Registering needs an unused signup invite. Existing accounts sign in as before.</summary>
    public bool RequireSignupInvite { get; set; }

    /// <summary>User names that may create and revoke signup invites. Set from Auth__AdminUserNames__0 and so on.</summary>
    public string[] AdminUserNames { get; set; } = [];

    public bool IsAdmin(string userName) => AdminUserNames.Any(n => string.Equals(n.Trim(), userName, StringComparison.OrdinalIgnoreCase));
}