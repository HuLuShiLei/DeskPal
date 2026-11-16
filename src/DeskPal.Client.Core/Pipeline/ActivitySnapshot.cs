namespace DeskPal.Client.Core.Pipeline;

/// <summary>Latest value of every signal kind, folded from the bus.</summary>
public sealed record ActivitySnapshot
{
    public ForegroundSignal? Foreground { get; init; }
    public string? BrowserDomain { get; init; }

    /// <summary>The browser's window or tab changed and its address has not been read since; <see cref="BrowserDomain"/> may be the previous site.</summary>
    public bool DomainPending { get; init; }

    public bool IsIdle { get; init; }
    public bool IsLocked { get; init; }
    public MediaSignal Media { get; init; } = new(false, null, null, null);

    public static readonly ActivitySnapshot Empty = new();

    public ActivitySnapshot Apply(ActivitySignal signal) => signal switch
    {
        ForegroundSignal f => this with
        {
            Foreground = f,
            BrowserDomain = f.IsBrowser && Foreground?.ProcessName == f.ProcessName ? BrowserDomain : null,
            DomainPending = f.IsBrowser && (DomainPending || Foreground?.ProcessName != f.ProcessName || Foreground.WindowTitle != f.WindowTitle),
        },
        BrowserDomainSignal d => this with { BrowserDomain = Foreground?.IsBrowser == true ? d.Domain : null, DomainPending = false },
        IdleSignal i => this with { IsIdle = i.IsIdle },
        LockSignal l => this with { IsLocked = l.IsLocked },
        MediaSignal m => this with { Media = m },
        _ => this,
    };
}
