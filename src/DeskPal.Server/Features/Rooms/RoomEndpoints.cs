using DeskPal.Server.Common;
using DeskPal.Server.Features.Presence;
using DeskPal.Shared.Api;

namespace DeskPal.Server.Features.Rooms;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rooms").RequireAuthorization();

        group.MapGet("/", async (HttpContext ctx, RoomService rooms) =>
            Results.Ok(await rooms.ListForUserAsync(ctx.User.GetUserId())));

        group.MapPost("/", async (CreateRoomRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rooms.CreateAsync(userId, req.Name);
            if (result.IsSuccess)
            {
                await notifier.MemberJoinedAsync(result.Value!.Id, userId);
            }

            return result.ToHttp();
        });

        group.MapPost("/{roomId:guid}/invites", async (Guid roomId, HttpContext ctx, RoomService rooms) =>
            (await rooms.CreateInviteAsync(ctx.User.GetUserId(), roomId)).ToHttp());

        group.MapPost("/join", async (JoinRoomRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rooms.JoinAsync(userId, req.Code);
            if (result.IsSuccess)
            {
                await notifier.MemberJoinedAsync(result.Value!.Id, userId);
            }

            return result.ToHttp();
        });

        group.MapPost("/{roomId:guid}/leave", async (Guid roomId, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rooms.LeaveAsync(userId, roomId);
            if (result.IsSuccess)
            {
                await notifier.MemberLeftAsync(roomId, userId);
            }

            return result.ToHttp();
        });

        group.MapPost("/{roomId:guid}/transfer", async (Guid roomId, TransferOwnerRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var result = await rooms.TransferOwnerAsync(ctx.User.GetUserId(), roomId, req.NewOwnerUserId);
            if (result.IsSuccess)
            {
                await notifier.OwnerChangedAsync(roomId, req.NewOwnerUserId);
            }

            return result.ToHttp();
        });

        group.MapDelete("/{roomId:guid}", async (Guid roomId, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var result = await rooms.DissolveAsync(ctx.User.GetUserId(), roomId);
            if (result.IsSuccess)
            {
                await notifier.DissolvedAsync(roomId, result.Value!);
            }

            return result.ToHttp();
        });

        group.MapGet("/{roomId:guid}/privacy", async (Guid roomId, HttpContext ctx, RoomService rooms) =>
            (await rooms.GetPrivacyAsync(ctx.User.GetUserId(), roomId)).ToHttp());

        group.MapPut("/{roomId:guid}/privacy", async (Guid roomId, PrivacyProfileDto dto, HttpContext ctx, RoomService rooms) =>
            (await rooms.SetPrivacyAsync(ctx.User.GetUserId(), roomId, dto)).ToHttp());
    }
}