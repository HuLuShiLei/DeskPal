using System.Text.RegularExpressions;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Presence;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskPal.Server.Features.Auth;

public static partial class AuthEndpoints
{
    [GeneratedRegex("^[a-zA-Z0-9_-]{3,32}$")]
    private static partial Regex UserNamePattern();

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest req, DeskPalDbContext db, TokenService tokens, SessionCache sessions, TimeProvider time, IOptions<AuthOptions> options, SignupInviteService invites) =>
        {
            var userName = req.UserName?.Trim().ToLowerInvariant() ?? "";
            var displayName = req.DisplayName?.Trim() ?? "";
            if (!UserNamePattern().IsMatch(userName))
            {
                return Fail(400, ApiErrorCodes.UserNameInvalid);
            }

            if (!IsValidDisplayName(displayName))
            {
                return Fail(400, InvalidDisplayName);
            }

            if (req.Password is null || req.Password.Length < 8)
            {
                return Fail(400, ApiErrorCodes.PasswordTooShort);
            }

            if (await db.Users.AnyAsync(u => u.UserName == userName))
            {
                return Fail(409, ApiErrorCodes.UserNameTaken);
            }

            var auth = options.Value;
            var signupCode = SignupInviteService.Normalize(req.SignupCode);
            if (auth.RequireSignupInvite && signupCode.Length == 0)
            {
                return Fail(400, ApiErrorCodes.SignupCodeRequired);
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                DisplayName = displayName,
                PasswordHash = PasswordHasher.Hash(req.Password),
                CreatedAt = time.GetUtcNow(),
                SessionId = Guid.NewGuid(),
            };
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();
                if (auth.RequireSignupInvite && !await invites.ClaimAsync(signupCode, user.Id))
                {
                    return Fail(400, ApiErrorCodes.SignupCodeInvalid);
                }

                await transaction.CommitAsync();
            }

            sessions.Set(user.Id, user.SessionId);
            return Results.Ok(new AuthResponse(tokens.Create(user), user.Id, user.UserName, user.DisplayName, IsAdmin: auth.IsAdmin(user.UserName)));
        });

        group.MapPost("/login", async (LoginRequest req, DeskPalDbContext db, TokenService tokens, SessionCache sessions, RoomNotifier notifier, IOptions<AuthOptions> options) =>
        {
            var userName = req.UserName?.Trim().ToLowerInvariant() ?? "";
            var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == userName);
            if (user is null || !PasswordHasher.Verify(req.Password ?? "", user.PasswordHash))
            {
                return Fail(401, ApiErrorCodes.WrongPassword);
            }

            user.SessionId = Guid.NewGuid();
            await db.SaveChangesAsync();
            sessions.Set(user.Id, user.SessionId);
            await notifier.SessionReplacedAsync(user.Id);
            return Results.Ok(new AuthResponse(tokens.Create(user), user.Id, user.UserName, user.DisplayName, user.ShareUsage, options.Value.IsAdmin(user.UserName)));
        });

        app.MapGet("/api/me", async (HttpContext ctx, DeskPalDbContext db, IOptions<AuthOptions> options) =>
        {
            var user = await db.Users.FindAsync(ctx.User.GetUserId());
            return user is null
                ? Fail(401, ApiErrorCodes.AccountMissing)
                : Results.Ok(new AuthResponse("", user.Id, user.UserName, user.DisplayName, user.ShareUsage, options.Value.IsAdmin(user.UserName)));
        }).RequireAuthorization();

        app.MapPut("/api/me/displayName", async (UpdateDisplayNameRequest req, HttpContext ctx, DeskPalDbContext db, RoomService rooms, RoomNotifier notifier, IOptions<AuthOptions> options) =>
        {
            var displayName = req.DisplayName?.Trim() ?? "";
            if (!IsValidDisplayName(displayName))
            {
                return Fail(400, InvalidDisplayName);
            }

            var user = await db.Users.FindAsync(ctx.User.GetUserId());
            if (user is null)
            {
                return Fail(401, ApiErrorCodes.AccountMissing);
            }

            if (user.DisplayName != displayName)
            {
                user.DisplayName = displayName;
                await db.SaveChangesAsync();
                await notifier.MemberRenamedAsync(user.Id, await rooms.GetRoomIdsForUserAsync(user.Id));
            }

            return Results.Ok(new AuthResponse("", user.Id, user.UserName, user.DisplayName, user.ShareUsage, options.Value.IsAdmin(user.UserName)));
        }).RequireAuthorization();

        var admin = app.MapGroup("/api/admin/signup-invites").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var user = await http.RequestServices.GetRequiredService<DeskPalDbContext>().Users.FindAsync(http.User.GetUserId());
            return user is not null && http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.IsAdmin(user.UserName)
                ? await next(ctx)
                : Fail(403, ApiErrorCodes.NotAdmin);
        });
        admin.MapGet("", async (SignupInviteService invites) => Results.Ok(await invites.ListAsync()));
        admin.MapPost("", async (HttpContext ctx, SignupInviteService invites) => Results.Ok(await invites.CreateAsync(ctx.User.GetUserId())));
        admin.MapDelete("/{code}", async (string code, SignupInviteService invites) =>
        {
            var result = await invites.RevokeAsync(code);
            return result.IsSuccess ? Results.NoContent() : result.ToHttp();
        });
    }

    private const string InvalidDisplayName = ApiErrorCodes.DisplayNameInvalid;

    private static bool IsValidDisplayName(string name) => name.Length is >= 1 and <= 20;

    private static IResult Fail(int status, string code) => Results.Json(new ApiError(code), statusCode: status);
}