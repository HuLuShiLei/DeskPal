using System.Security.Cryptography;
using System.Text;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Presence;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskPal.Server.Features.Rules;

public static class RuleEndpoints
{
    public const string MaintainerHeader = "X-DeskPal-Maintainer";

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

        app.MapGet("/api/rules/candidates", async (HttpContext ctx, RuleCandidateService candidates, IOptions<RuleOptions> options) =>
        {
            var expected = options.Value.MaintainerToken;
            var given = ctx.Request.Headers[MaintainerHeader].ToString();
            if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
            {
                return Results.NotFound();
            }

            return Results.Ok(await candidates.GetCandidatesAsync());
        });

        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("/rules", async (HttpContext ctx, UserRuleService rules) => Results.Ok(await rules.ListAsync(ctx.User.GetUserId())));

        me.MapPut("/rules", async (UpsertUserRuleRequest req, HttpContext ctx, UserRuleService rules, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rules.UpsertAsync(userId, req);
            if (result.IsSuccess)
            {
                await notifier.PersonalRulesChangedAsync(userId);
            }

            return result.ToHttp();
        });

        me.MapDelete("/rules", async (RuleKind kind, string pattern, HttpContext ctx, UserRuleService rules, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            if (await rules.DeleteAsync(userId, kind, pattern))
            {
                await notifier.PersonalRulesChangedAsync(userId);
            }

            return Results.NoContent();
        });

        me.MapPut("/sharing", async (SetSharingRequest req, HttpContext ctx, RuleCandidateService candidates) =>
            (await candidates.SetSharingAsync(ctx.User.GetUserId(), req.Enabled)).ToHttp());

        me.MapPost("/usage", async (UsageReportRequest req, HttpContext ctx, RuleCandidateService candidates) =>
            (await candidates.ReportAsync(ctx.User.GetUserId(), req)).ToHttp());
    }
}
