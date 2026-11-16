namespace DeskPal.Server.Features.Auth;

public sealed class AuthOptions
{
    /// <summary>Registering needs an unused signup invite. Existing accounts sign in as before.</summary>
    public bool RequireSignupInvite { get; set; }

    /// <summary>
    /// User names that may create and revoke signup invites. Each entry may list several names
    /// separated by commas. Set from Auth__AdminUserNames__0 (Stack variable DESKPAL_ADMIN_USER).
    /// </summary>
    public string[] AdminUserNames { get; set; } = [];

    public bool IsAdmin(string userName) =>
        AdminUserNames
            .SelectMany(entry => entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(name => string.Equals(name, userName, StringComparison.OrdinalIgnoreCase));
}