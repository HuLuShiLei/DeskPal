using System.Text.RegularExpressions;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Presence;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Auth;

public static partial class AuthEndpoints
{
    [GeneratedRegex("^[a-zA-Z0-9_-]{3,32}$")]
    private static partial Regex UserNamePattern();

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest req, DeskPalDbContext db, TokenService tokens, TimeProvider time) =>
        {
            var userName = req.UserName?.Trim().ToLowerInvariant() ?? "";
            var displayName = req.DisplayName?.Trim() ?? "";
            if (!UserNamePattern().IsMatch(userName))
            {
                return Fail(400, "用户名需要 3 到 32 位，只能包含字母、数字、下划线和短横线");
            }

            if (!IsValidDisplayName(displayName))
            {
                return Fail(400, InvalidDisplayName);
            }

            if (req.Password is null || req.Password.Length < 8)
            {
                return Fail(400, "密码至少 8 位");
            }

            if (await db.Users.AnyAsync(u => u.UserName == userName))
            {
                return Fail(409, "用户名已被占用");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                DisplayName = displayName,
                PasswordHash = PasswordHasher.Hash(req.Password),
                CreatedAt = time.GetUtcNow(),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Ok(new AuthResponse(tokens.Create(user), user.Id, user.UserName, user.DisplayName));
        });

        group.MapPost("/login", async (LoginRequest req, DeskPalDbContext db, TokenService tokens) =>
        {
            var userName = req.UserName?.Trim().ToLowerInvariant() ?? "";
            var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == userName);
            if (user is null || !PasswordHasher.Verify(req.Password ?? "", user.PasswordHash))
            {
                return Fail(401, "用户名或密码错误");
            }

            return Results.Ok(new AuthResponse(tokens.Create(user), user.Id, user.UserName, user.DisplayName, user.ShareUsage));
        });

        app.MapGet("/api/me", async (HttpContext ctx, DeskPalDbContext db) =>
        {
            var user = await db.Users.FindAsync(ctx.User.GetUserId());
            return user is null
                ? Fail(401, "账号不存在")
                : Results.Ok(new AuthResponse("", user.Id, user.UserName, user.DisplayName, user.ShareUsage));
        }).RequireAuthorization();

        app.MapPut("/api/me/displayName", async (UpdateDisplayNameRequest req, HttpContext ctx, DeskPalDbContext db, RoomService rooms, RoomNotifier notifier) =>
        {
            var displayName = req.DisplayName?.Trim() ?? "";
            if (!IsValidDisplayName(displayName))
            {
                return Fail(400, InvalidDisplayName);
            }

            var user = await db.Users.FindAsync(ctx.User.GetUserId());
            if (user is null)
            {
                return Fail(401, "账号不存在");
            }

            if (user.DisplayName != displayName)
            {
                user.DisplayName = displayName;
                await db.SaveChangesAsync();
                await notifier.MemberRenamedAsync(user.Id, await rooms.GetRoomIdsForUserAsync(user.Id));
            }

            return Results.Ok(new AuthResponse("", user.Id, user.UserName, user.DisplayName, user.ShareUsage));
        }).RequireAuthorization();
    }

    private const string InvalidDisplayName = "显示名称需要 1 到 20 个字符";

    private static bool IsValidDisplayName(string name) => name.Length is >= 1 and <= 20;

    private static IResult Fail(int status, string error) => Results.Json(new ApiError(error), statusCode: status);
}