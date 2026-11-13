using System.Security.Claims;
using System.Text;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DeskPal.Server.Features.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "deskpal";
    public string Key { get; set; } = "";
    public int LifetimeDays { get; set; } = 30;

    public SymmetricSecurityKey GetSigningKey()
    {
        var bytes = Encoding.UTF8.GetBytes(Key);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes. Set it through the Jwt__Key environment variable.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

public sealed class TokenService(JwtOptions options, TimeProvider time)
{
    public string Create(User user)
    {
        List<Claim> claims =
        [
            new Claim(ClaimsPrincipalExtensions.UserIdClaim, user.Id.ToString()),
            new Claim("name", user.DisplayName),
            new Claim("preferred_username", user.UserName),
        ];
        if (user.SessionId is { } session)
        {
            claims.Add(new Claim(SessionCache.Claim, session.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Issuer,
            Expires = time.GetUtcNow().AddDays(options.LifetimeDays).UtcDateTime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(options.GetSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}