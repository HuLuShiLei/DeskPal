namespace DeskPal.Client.Core.Pipeline;

/// <summary>Latest value of every signal kind, folded from the bus.</summary>
public sealed record ActivitySnapshot
{
    public ForegroundSignal? Foreground { get; init; }
    public string? BrowserDomain { get; init; }
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
        },
        BrowserDomainSignal d => this with { BrowserDomain = Foreground?.IsBrowser == true ? d.Domain : null },
        IdleSignal i => this with { IsIdle = i.IsIdle },
        LockSignal l => this with { IsLocked = l.IsLocked },
        MediaSignal m => this with { Media = m },
        _ => this,
    };
}