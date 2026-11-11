namespace DeskPal.Client.Core.Pipeline;

/// <summary>Raw facts published by collectors. Consumers (presence, later the game module) subscribe to the bus.</summary>
public abstract record ActivitySignal;

public sealed record ForegroundSignal(
    string ProcessName,
    string? ProcessPath,
    string AppDisplayName,
    string WindowTitle,
    bool IsFullscreen,
    bool IsBrowser) : ActivitySignal;

/// <summary>Host of the active tab of the foreground browser window, or null when it cannot be read.</summary>
public sealed record BrowserDomainSignal(string? Domain) : ActivitySignal;

public sealed record IdleSignal(bool IsIdle) : ActivitySignal;

public sealed record LockSignal(bool IsLocked) : ActivitySignal;

public sealed record MediaSignal(bool IsPlaying, string? Title, string? Artist, string? SourceApp) : ActivitySignal;