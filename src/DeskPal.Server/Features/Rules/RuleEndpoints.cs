using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Presence;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Rules;

public static class RuleEndpoints
{
    public static void MapRuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rules/builtin", async (int? version, DeskPalDbContext db) =>
        {
            var current = await BuiltinRuleSeeder.GetStoredVersionAsync(db);
            if (version == current)
            {
                return Results.Ok(new BuiltinRulesResponse(current, null));
            }

            var rules = await db.BuiltinRules.OrderBy(r => r.Id)
                .Select(r => new RuleDto(r.Kind, r.Pattern, r.Category, r.Name))
                .ToListAsync();
            return Results.Ok(new BuiltinRulesResponse(current, rules));
        }).RequireAuthorization();

        var group = app.MapGroup("/api/rooms/{roomId:guid}/rules").RequireAuthorization();

        group.MapGet("/", async (Guid roomId, HttpContext ctx, RoomService rooms, DeskPalDbContext db) =>
        {
            if (!await rooms.IsMemberAsync(ctx.User.GetUserId(), roomId))
            {
                return ServiceResult<Unit>.Fail(404, "Room 不存在，或者你不是该 Room 的成员").ToHttp();
            }

            var rules = await db.RoomRules.Where(r => r.RoomId == roomId).ToListAsync();
            return Results.Ok(rules
                .OrderByDescending(r => r.UpdatedAt)
                .Select(r => new RoomRuleDto(r.Kind, r.Pattern, r.Category, r.Name, r.CreatedBy, r.UpdatedAt))
                .ToList());
        });

        group.MapPut("/", async (Guid roomId, UpsertRoomRuleRequest req, HttpContext ctx, RoomService rooms,
            DeskPalDbContext db, RoomNotifier notifier, TimeProvider time) =>
        {
            var userId = ctx.User.GetUserId();
            if (!await rooms.IsMemberAsync(userId, roomId))
            {
                return ServiceResult<Unit>.Fail(404, "Room 不存在，或者你不是该 Room 的成员").ToHttp();
            }

            var pattern = RulePattern.Normalize(req.Kind, req.Pattern);
            if (pattern.Length is < 1 or > 200)
            {
                return ServiceResult<Unit>.Fail(400, "规则内容不能为空，且不超过 200 个字符").ToHttp();
            }

            var rule = await db.RoomRules.SingleOrDefaultAsync(r => r.RoomId == roomId && r.Kind == req.Kind && r.Pattern == pattern);
            if (rule is null)
            {
                rule = new RoomRule { Id = Guid.NewGuid(), RoomId = roomId, Kind = req.Kind, Pattern = pattern };
                db.RoomRules.Add(rule);
            }

            rule.Category = req.Category;
            rule.Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();
            rule.CreatedBy = userId;
            rule.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync();
            await notifier.RulesChangedAsync(roomId, userId);
            return Results.Ok(new RoomRuleDto(rule.Kind, rule.Pattern, rule.Category, rule.Name, rule.CreatedBy, rule.UpdatedAt));
        });
    }
}