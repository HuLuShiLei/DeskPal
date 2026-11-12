using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Classification;

public sealed record UnclassifiedApp(RuleKind Kind, string Pattern, string DisplayName, TimeSpan Today);

/// <summary>
/// Today's foreground time of desktop apps that no rule recognized. Kept in memory only and cleared at local midnight.
/// The panel lists the busiest ones so the user can classify them; with consent the totals are also reported.
/// </summary>
public sealed class UnclassifiedUsage
{
    /// <summary>Longer gaps (sleep, a stalled loop) are not counted.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, UnclassifiedApp> _apps = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private CorrectionTarget? _current;
    private DateTimeOffset _since;
    private DateOnly _day;

    /// <param name="target">What is in front now, or null when it should not count (recognized, browser, idle, locked, paused).</param>
    public void Record(CorrectionTarget? target, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        lock (_lock)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local).DateTime);
            if (day != _day)
            {
                _apps.Clear();
                _day = day;
            }
            else if (_current is { } previous && now > _since)
            {
                var elapsed = now - _since;
                if (elapsed <= MaxGap)
                {
                    var app = _apps.GetValueOrDefault(previous.Pattern) ?? new UnclassifiedApp(previous.Kind, previous.Pattern, previous.DisplayName, TimeSpan.Zero);
                    _apps[previous.Pattern] = app with { DisplayName = previous.DisplayName, Today = app.Today + elapsed };
                }
            }

            _current = target is { Kind: RuleKind.Process } ? target : null;
            _since = now;
        }
    }

    /// <summary>Drops an app after the user has classified it.</summary>
    public void Forget(string pattern)
    {
        lock (_lock)
        {
            _apps.Remove(pattern);
            if (_current?.Pattern == pattern)
            {
                _current = null;
            }
        }
    }

    public IReadOnlyList<UnclassifiedApp> Top(TimeSpan atLeast, int count)
    {
        lock (_lock)
        {
            return _apps.Values.Where(a => a.Today >= atLeast).OrderByDescending(a => a.Today).Take(count).ToList();
        }
    }
}
