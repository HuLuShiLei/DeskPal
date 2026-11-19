using DeskPal.Server.Common;
using DeskPal.Server.Features.Presence;
using DeskPal.Shared.Api;

namespace DeskPal.Server.Features.Personal;

public static class PersonalEndpoints
{
    public static void MapPersonalEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapPost("/personal-sync", async (PersonalDataDto req, HttpContext ctx, PersonalSyncService personal, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await personal.EnableAsync(userId, req);
            if (result.IsSuccess)
            {
                await notifier.PersonalRulesChangedAsync(userId);
            }

            return result.ToHttp();
        });

        me.MapDelete("/personal-sync", async (HttpContext ctx, PersonalSyncService personal, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await personal.DisableAsync(userId);
            if (!result.IsSuccess)
            {
                return result.ToHttp();
            }

            await notifier.PersonalRulesChangedAsync(userId);
            return Results.NoContent();
        });

        me.MapGet("/no-title-rules", async (HttpContext ctx, NoTitleRuleService rules) => (await rules.ListAsync(ctx.User.GetUserId())).ToHttp());

        me.MapPut("/no-title-rules", async (UpsertNoTitleRuleRequest req, HttpContext ctx, NoTitleRuleService rules, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rules.UpsertAsync(userId, req);
            if (result.IsSuccess)
            {
                await notifier.PersonalRulesChangedAsync(userId);
            }

            return result.ToHttp();
        });

        me.MapDelete("/no-title-rules", async (NoTitleKind kind, string pattern, HttpContext ctx, NoTitleRuleService rules, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rules.DeleteAsync(userId, kind, pattern);
            if (!result.IsSuccess)
            {
                return result.ToHttp();
            }

            if (result.Value)
            {
                await notifier.PersonalRulesChangedAsync(userId);
            }

            return Results.NoContent();
        });
    }
}