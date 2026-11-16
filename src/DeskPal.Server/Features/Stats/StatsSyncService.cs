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

    /// <summary>A few hundred no-title rules, encrypted. Far more than the panel lets anyone add.</summary>
    private const int MaxSettingsBytes = 64 * 1024;

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
        if (!options.Value.IsAllowed(user.UserName))
        {
            return ServiceResult<StatsSyncStatusDto>.Fail(StatusCodes.Status403Forbidden, ApiErrorCodes.StatsSyncNotAllowed);
        }

        if (user.StatsSyncEnabled)
        {
            return ServiceResult<StatsSyncStatusDto>.Fail(StatusCodes.Status409Conflict, ApiErrorCodes.StatsSyncAlreadyEnabled);
        }

        await db.StatsSyncDays.Where(d => d.UserId == userId).ExecuteDeleteAsync();
        user.StatsSyncEnabled = true;
        user.StatsSyncSalt = req.Salt;
        user.StatsSyncKeyCheck = req.KeyCheck;
        user.StatsSyncSettings = null;
        user.StatsSyncSettingsRevision = null;
        await db.SaveChangesAsync();
        return ServiceResult<StatsSyncStatusDto>.Ok(await StatusAsync(user));
    }

    /// <summary>Turns sync off and deletes every stored day, the settings, the salt and the key check in one transaction.</summary>
    public async Task DisableAsync(Guid userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.StatsSyncDays.Where(d => d.UserId == userId).ExecuteDeleteAsync();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.StatsSyncEnabled = false;
        user.StatsSyncSalt = null;
        user.StatsSyncKeyCheck = null;
        user.StatsSyncSettings = null;
        user.StatsSyncSettingsRevision = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Turns sync off, deleting the stored copy, for every account that has it on but is no longer allowed.</summary>
    /// <returns>Number of accounts turned off.</returns>
    public async Task<int> DisableUnlistedAsync()
    {
        var enabled = await db.Users.AsNoTracking().Where(u => u.StatsSyncEnabled).Select(u => new { u.Id, u.UserName }).ToListAsync();
        var unlisted = enabled.Where(u => !options.Value.IsAllowed(u.UserName)).Select(u => u.Id).ToList();
        foreach (var userId in unlisted)
        {
            await DisableAsync(userId);
        }

        return unlisted.Count;
    }

    public async Task<ServiceResult<StatsSyncDayInfo>> UploadAsync(Guid userId, DateOnly date, UploadStatsDayRequest req)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (!options.Value.IsAllowed(user.UserName))
        {
            return ServiceResult<StatsSyncDayInfo>.Fail(StatusCodes.Status403Forbidden, ApiErrorCodes.StatsSyncNotAllowed);
        }

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
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.UserName, u.StatsSyncEnabled }).SingleAsync();
        var day = user.StatsSyncEnabled && options.Value.IsAllowed(user.UserName) ? await db.StatsSyncDays.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == userId && d.Date == date) : null;
        return day is null ? null : new StatsSyncDayDto(day.Date, day.Revision, Convert.ToBase64String(day.Data));
    }

    public async Task<ServiceResult<StatsSyncSettingsInfo>> UploadSettingsAsync(Guid userId, UploadStatsSettingsRequest req)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (!options.Value.IsAllowed(user.UserName))
        {
            return ServiceResult<StatsSyncSettingsInfo>.Fail(StatusCodes.Status403Forbidden, ApiErrorCodes.StatsSyncNotAllowed);
        }

        if (!user.StatsSyncEnabled)
        {
            return ServiceResult<StatsSyncSettingsInfo>.Fail(StatusCodes.Status409Conflict, ApiErrorCodes.StatsSyncNotEnabled);
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(req.Data ?? "");
        }
        catch (FormatException)
        {
            return ServiceResult<StatsSyncSettingsInfo>.Fail(StatusCodes.Status400BadRequest, ApiErrorCodes.StatsSyncInvalid);
        }

        if (data.Length is 0 or > MaxSettingsBytes)
        {
            return ServiceResult<StatsSyncSettingsInfo>.Fail(StatusCodes.Status400BadRequest, ApiErrorCodes.StatsSyncInvalid);
        }

        user.StatsSyncRevision++;
        user.StatsSyncSettings = data;
        user.StatsSyncSettingsRevision = user.StatsSyncRevision;
        await db.SaveChangesAsync();
        return ServiceResult<StatsSyncSettingsInfo>.Ok(new StatsSyncSettingsInfo(user.StatsSyncRevision));
    }

    /// <returns>null when sync is off or no settings are stored.</returns>
    public async Task<StatsSyncSettingsDto?> DownloadSettingsAsync(Guid userId)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        return user is { StatsSyncEnabled: true, StatsSyncSettings: { } data, StatsSyncSettingsRevision: { } revision } && options.Value.IsAllowed(user.UserName)
            ? new StatsSyncSettingsDto(revision, Convert.ToBase64String(data))
            : null;
    }


    private async Task<StatsSyncStatusDto> StatusAsync(User user)
    {
        var allowed = options.Value.IsAllowed(user.UserName);
        if (!user.StatsSyncEnabled || !allowed)
        {
            return new StatsSyncStatusDto(false, null, null, [], Allowed: allowed);
        }

        var days = await db.StatsSyncDays.Where(d => d.UserId == user.Id)
            .OrderBy(d => d.Date)
            .Select(d => new StatsSyncDayInfo(d.Date, d.Revision))
            .ToListAsync();
        return new StatsSyncStatusDto(true, user.StatsSyncSalt, user.StatsSyncKeyCheck, days, user.StatsSyncSettingsRevision, Allowed: true);
    }

    private static bool Valid(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;
}