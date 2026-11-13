using DeskPal.Server.Common;
using DeskPal.Shared.Api;

namespace DeskPal.Server.Features.Stats;

public static class StatsSyncEndpoints
{
    public static void MapStatsSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapGroup("/api/me/stats-sync").RequireAuthorization();

        sync.MapGet("", async (HttpContext ctx, StatsSyncService service) => Results.Ok(await service.GetStatusAsync(ctx.User.GetUserId())));

        sync.MapPut("", async (EnableStatsSyncRequest req, HttpContext ctx, StatsSyncService service) =>
            (await service.EnableAsync(ctx.User.GetUserId(), req)).ToHttp());

        sync.MapDelete("", async (HttpContext ctx, StatsSyncService service) =>
        {
            await service.DisableAsync(ctx.User.GetUserId());
            return Results.NoContent();
        });

        sync.MapPut("/days/{date}", async (DateOnly date, UploadStatsDayRequest req, HttpContext ctx, StatsSyncService service) =>
            (await service.UploadAsync(ctx.User.GetUserId(), date, req)).ToHttp());

        sync.MapGet("/days/{date}", async (DateOnly date, HttpContext ctx, StatsSyncService service) =>
            await service.DownloadAsync(ctx.User.GetUserId(), date) is { } day ? Results.Ok(day) : Results.NotFound());

    }
}