using System.Collections.Concurrent;
using DeskPal.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Auth;

/// <summary>Remembers each user's current session so checking a token rarely reads the database.</summary>
public sealed class SessionCache
{
    public const string Claim = "sid";

    private readonly ConcurrentDictionary<Guid, Guid?> _current = new();

    public void Set(Guid userId, Guid? sessionId) => _current[userId] = sessionId;

    public async Task<bool> IsCurrentAsync(Guid userId, string? tokenSession, DeskPalDbContext db)
    {
        if (!_current.TryGetValue(userId, out var current))
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.SessionId }).SingleOrDefaultAsync();
            if (user is null)
            {
                return false;
            }

            current = _current.GetOrAdd(userId, user.SessionId);
        }

        return current is null || (Guid.TryParse(tokenSession, out var session) && session == current);
    }
}