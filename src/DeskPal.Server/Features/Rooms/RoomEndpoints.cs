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

        group.MapPut("/{roomId:guid}", async (Guid roomId, RenameRoomRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var result = await rooms.RenameAsync(ctx.User.GetUserId(), roomId, req.Name);
            if (!result.IsSuccess)
            {
                return result.ToHttp();
            }

            if (result.Value!.Changed)
            {
                await notifier.RenamedAsync(roomId);
            }

            return Results.Ok(result.Value.Room);
        });

        group.MapPost("/{roomId:guid}/invites", async (Guid roomId, HttpContext ctx, RoomService rooms) =>
            (await rooms.CreateInviteAsync(ctx.User.GetUserId(), roomId)).ToHttp());

        group.MapPost("/join", async (JoinRoomRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rooms.JoinAsync(userId, req.Code);
            if (result.IsSuccess)
            {
                var roomId = result.Value!.Id;
                await notifier.MemberJoinedAsync(roomId, userId, await rooms.GetBlockPeersAsync(roomId, userId));
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

        group.MapPost("/{roomId:guid}/kick", async (Guid roomId, KickMemberRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var result = await rooms.KickAsync(ctx.User.GetUserId(), roomId, req.UserId);
            if (result.IsSuccess)
            {
                await notifier.KickedAsync(roomId, req.UserId);
                await notifier.MemberLeftAsync(roomId, req.UserId);
            }

            return result.ToHttp();
        });

        group.MapPost("/{roomId:guid}/blocks", async (Guid roomId, BlockMemberRequest req, HttpContext ctx, RoomService rooms, RoomNotifier notifier, TimeProvider time) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rooms.BlockAsync(userId, roomId, req.UserId);
            if (!result.IsSuccess)
            {
                return result.ToHttp();
            }

            if (result.Value)
            {
                await notifier.BlockedAsync(roomId, userId, req.UserId, time.GetUtcNow());
            }

            return Results.NoContent();
        });

        group.MapDelete("/{roomId:guid}/blocks/{userId:guid}", async (Guid roomId, Guid userId, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var me = ctx.User.GetUserId();
            var result = await rooms.UnblockAsync(me, roomId, userId);
            if (!result.IsSuccess)
            {
                return result.ToHttp();
            }

            if (result.Value)
            {
                var stillBlocked = (await rooms.GetBlockPeersAsync(roomId, me)).Contains(userId);
                await notifier.UnblockedAsync(roomId, me, userId, stillBlocked);
            }

            return Results.NoContent();
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

        group.MapPut("/{roomId:guid}/privacy", async (Guid roomId, PrivacyProfileDto dto, HttpContext ctx, RoomService rooms, RoomNotifier notifier) =>
        {
            var userId = ctx.User.GetUserId();
            var result = await rooms.SetPrivacyAsync(userId, roomId, dto);
            if (!result.IsSuccess)
            {
                return ServiceResult<PrivacyProfileDto>.Fail(result.Status, result.Error!).ToHttp();
            }

            if (result.Value!.SharingChanged)
            {
                await notifier.MemberSharingChangedAsync(roomId, userId);
            }

            return Results.Ok(result.Value.Profile);
        });
    }
}
