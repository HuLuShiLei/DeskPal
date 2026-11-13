using System.Security.Cryptography;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Auth;

public sealed class SignupInviteService(DeskPalDbContext db, TimeProvider time)
{
    private const int ListLimit = 100;

    public static string Normalize(string? code) => code?.Trim().ToUpperInvariant() ?? "";

    public async Task<List<SignupInviteDto>> ListAsync()
    {
        var invites = await db.SignupInvites.Where(i => i.RevokedAt == null).ToListAsync();
        var userIds = invites.Where(i => i.UsedBy != null).Select(i => i.UsedBy!.Value).ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        return invites
            .OrderBy(i => i.UsedBy is null ? 0 : 1)
            .ThenByDescending(i => i.UsedAt ?? i.CreatedAt)
            .Take(ListLimit)
            .Select(i => users.TryGetValue(i.UsedBy ?? Guid.Empty, out var user)
                ? new SignupInviteDto(i.Code, i.CreatedAt, i.UsedAt, user.UserName, user.DisplayName)
                : new SignupInviteDto(i.Code, i.CreatedAt, i.UsedAt))
            .ToList();
    }

    public async Task<SignupInviteDto> CreateAsync(Guid adminId)
    {
        string code;
        do
        {
            code = RandomNumberGenerator.GetString(RoomService.InviteAlphabet, 8);
        }
        while (await db.SignupInvites.AnyAsync(i => i.Code == code));

        var invite = new SignupInvite { Id = Guid.NewGuid(), Code = code, CreatedBy = adminId, CreatedAt = time.GetUtcNow() };
        db.SignupInvites.Add(invite);
        await db.SaveChangesAsync();
        return new SignupInviteDto(invite.Code, invite.CreatedAt);
    }

    public async Task<ServiceResult<Unit>> RevokeAsync(string? code)
    {
        code = Normalize(code);
        var invite = await db.SignupInvites.SingleOrDefaultAsync(i => i.Code == code);
        if (invite is null || invite.RevokedAt is not null)
        {
            return ServiceResult<Unit>.Fail(404, ApiErrorCodes.SignupCodeInvalid);
        }

        if (invite.UsedBy is not null)
        {
            return ServiceResult<Unit>.Fail(409, ApiErrorCodes.SignupCodeUsed);
        }

        invite.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    /// <summary>
    /// Marks the invite used by <paramref name="userId"/> in one conditional UPDATE, so two registrations with the same code
    /// cannot both succeed. Call inside the transaction that creates the user.
    /// </summary>
    /// <returns>false when the code is unknown, already used or revoked.</returns>
    public async Task<bool> ClaimAsync(string code, Guid userId)
    {
        var now = time.GetUtcNow();
        return await db.SignupInvites
            .Where(i => i.Code == code && i.UsedBy == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedBy, userId).SetProperty(i => i.UsedAt, now)) == 1;
    }
}