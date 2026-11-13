using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskPal.Server.Features.Stats;

/// <summary>Stores the encrypted days of users who turned on usage stats sync. Never reads what is inside.</summary>
public sealed class StatsSyncService(DeskPalDbContext db, TimeProvider time, IOptions<StatsSyncOptions> options)
{
    private const int MaxTextLength = 200;

    public async Task<StatsSyncStatusDto> GetStatusAsync(Guid userId)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        return await StatusAsync(user);
    }

    public async Task<ServiceResult<StatsSyncStatusDto>> EnableAsync(Guid userId, EnableStatsSyncRequest req)
    {
        if (!Valid(req.Salt) || !Valid(req.KeyCheck))
        {
            return ServiceResult<StatsSyncStatusDto>.Fail(StatusCodes.Status400BadRequest, ApiErrorCodes.StatsSyncInvalid);
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (user.StatsSyncEnabled)
        {
            return ServiceResult<StatsSyncStatusDto>.Fail(StatusCodes.Status409Conflict, ApiErrorCodes.StatsSyncAlreadyEnabled);
        }

        await db.StatsSyncDays.Where(d => d.UserId == userId).ExecuteDeleteAsync();
        user.StatsSyncEnabled = true;
        user.StatsSyncSalt = req.Salt;
        user.StatsSyncKeyCheck = req.KeyCheck;
        await db.SaveChangesAsync();
        return ServiceResult<StatsSyncStatusDto>.Ok(await StatusAsync(user));
    }

    /// <summary>Turns sync off and deletes every stored day, the salt and the key check in one transaction.</summary>
    public async Task DisableAsync(Guid userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.StatsSyncDays.Where(d => d.UserId == userId).ExecuteDeleteAsync();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.StatsSyncEnabled = false;
        user.StatsSyncSalt = null;
        user.StatsSyncKeyCheck = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<ServiceResult<StatsSyncDayInfo>> UploadAsync(Guid userId, DateOnly date, UploadStatsDayRequest req)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (!user.StatsSyncEnabled)
        {
            return ServiceResult<StatsSyncDayInfo>.Fail(StatusCodes.Status409Conflict, ApiErrorCodes.StatsSyncNotEnabled);
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(req.Data ?? "");
        }
        catch (FormatException)
        {
            return ServiceResult<StatsSyncDayInfo>.Fail(StatusCodes.Status400BadRequest, ApiErrorCodes.StatsSyncInvalid);
        }

        if (data.Length == 0)
        {
            return ServiceResult<StatsSyncDayInfo>.Fail(StatusCodes.Status400BadRequest, ApiErrorCodes.StatsSyncInvalid);
        }

        var limits = options.Value;
        if (data.Length > Megabytes(limits.MaxDayMegabytes))
        {
            return ServiceResult<StatsSyncDayInfo>.Fail(StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.StatsSyncDayTooLarge, limits.MaxDayMegabytes);
        }

        var others = await db.StatsSyncDays.Where(d => d.UserId == userId && d.Date != date).SumAsync(d => (long)d.Data.Length);
        if (others + data.Length > Megabytes(limits.MaxTotalMegabytes))
        {
            return ServiceResult<StatsSyncDayInfo>.Fail(StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.StatsSyncQuotaExceeded, limits.MaxTotalMegabytes);
        }

        var day = await db.StatsSyncDays.SingleOrDefaultAsync(d => d.UserId == userId && d.Date == date);
        if (day is null)
        {
            day = new StatsSyncDay { UserId = userId, Date = date, Data = data };
            db.StatsSyncDays.Add(day);
        }

        user.StatsSyncRevision++;
        day.Data = data;
        day.Revision = user.StatsSyncRevision;
        day.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return ServiceResult<StatsSyncDayInfo>.Ok(new StatsSyncDayInfo(date, day.Revision));
    }

    /// <returns>null when sync is off or the day is not stored.</returns>
    public async Task<StatsSyncDayDto?> DownloadAsync(Guid userId, DateOnly date)
    {
        var enabled = await db.Users.Where(u => u.Id == userId).Select(u => u.StatsSyncEnabled).SingleAsync();
        var day = enabled ? await db.StatsSyncDays.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == userId && d.Date == date) : null;
        return day is null ? null : new StatsSyncDayDto(day.Date, day.Revision, Convert.ToBase64String(day.Data));
    }


    private async Task<StatsSyncStatusDto> StatusAsync(User user)
    {
        if (!user.StatsSyncEnabled)
        {
            return new StatsSyncStatusDto(false, null, null, []);
        }

        var days = await db.StatsSyncDays.Where(d => d.UserId == user.Id)
            .OrderBy(d => d.Date)
            .Select(d => new StatsSyncDayInfo(d.Date, d.Revision))
            .ToListAsync();
        return new StatsSyncStatusDto(true, user.StatsSyncSalt, user.StatsSyncKeyCheck, days);
    }

    private static bool Valid(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;

    private static long Megabytes(int value) => value * 1024L * 1024L;
}