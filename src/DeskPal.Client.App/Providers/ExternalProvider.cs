using System.Diagnostics;
using System.Text;
using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Ui;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// A plugin the user put in the plugins folder: a program that prints one JSON object per line
/// (<see cref="ProviderProtocol"/>). Its lines become the same signals the official plugins publish.
/// It runs as its own process with the user's rights; DeskPal gives it no token and no settings, only reads its output,
/// and starts it again with a growing pause when it exits.
/// </summary>
public sealed class ExternalProvider : IActivityProvider
{
    private const int MaxLinesPerSecond = 50;
    private const int MaxLoggedProblems = 5;
    private static readonly int[] RestartDelaysSeconds = [1, 2, 5, 10, 30, 60];
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(5);

    private readonly string _path;
    private readonly Func<TimeSpan> _idleThreshold;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private string? _helloName;
    private string? _version;
    private IReadOnlySet<ProviderCapability> _provides;
    private IReadOnlySet<string> _apps;
    private ProviderHealth _health = ProviderHealth.Starting;
    private string? _error;

    /// <param name="knownProvides">What the plugin said it supplies the last time it ran; null when it never said.</param>
    /// <param name="knownApps">The apps its detail covered the last time it ran.</param>
    public ExternalProvider(
        string path,
        IReadOnlySet<ProviderCapability>? knownProvides,
        IReadOnlySet<string>? knownApps,
        Func<TimeSpan> idleThreshold,
        ILogger logger)
    {
        _path = path;
        _idleThreshold = idleThreshold;
        _logger = logger;
        _provides = knownProvides ?? ProviderCapabilities.PluginDefault;
        _apps = knownApps ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        File = Path.GetFileName(path);
    }

    public static string IdFor(string file) => "user:" + file;

    public string File { get; }

    public string Id => IdFor(File);

    public string Name => _helloName ?? File;

    public string? Version => _version;

    public bool Official => false;

    public IReadOnlySet<ProviderCapability> Provides => _provides;

    public IReadOnlySet<string> Apps => _apps;

    public ProviderHealth Health => _health;

    public IReadOnlyList<PluginNote> Notes => _error is null ? [] : [new PluginNote("plugin.error", _error, "warn")];

    public IReadOnlyList<string> Actions => [];

    public event Action? Changed;

    /// <summary>Raised when the hello line changed what the plugin supplies or the apps it covers, so the host can remember it.</summary>
    public event Action<IReadOnlySet<ProviderCapability>, IReadOnlySet<string>>? ProvidesAnnounced;

    public Task<CommandResult> RunActionAsync(string action) => Task.FromResult(CommandResult.Fail(Strings.Plugin_UnknownAction));

    /// <summary>Runs until cancelled.</summary>
    public async Task RunAsync(SignalSink sink, CancellationToken cancellation)
    {
        var attempt = 0;
        while (!cancellation.IsCancellationRequested)
        {
            var started = Stopwatch.StartNew();
            try
            {
                await RunOnceAsync(sink, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plugin {Path} could not be run", _path);
                SetHealth(ProviderHealth.Failed, ex.Message);
            }

            // A plugin that worked for a while and then stopped gets a quick restart; one that fails at once backs off.
            attempt = started.Elapsed > TimeSpan.FromMinutes(1) ? 0 : Math.Min(attempt + 1, RestartDelaysSeconds.Length - 1);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(RestartDelaysSeconds[attempt]), cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Turned off: next time it starts fresh.
        SetHealth(ProviderHealth.Starting, null);
    }

    private async Task RunOnceAsync(SignalSink sink, CancellationToken cancellation)
    {
        var info = PluginFiles.StartInfo(_path);
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.CreateNoWindow = true;
        info.WorkingDirectory = Path.GetDirectoryName(_path) ?? "";
        info.Environment["DESKPAL_PROTOCOL"] = ProviderProtocol.Version.ToString();
        info.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = Process.Start(info) ?? throw new InvalidOperationException("The program did not start.");
        _logger.LogInformation("Plugin {Path} started (pid {Pid})", _path, process.Id);
        process.StandardInput.Close();
        var errors = ReadErrorsAsync(process);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var idle = new IdleTracker();
        var idleLoop = RunIdleAsync(idle, sink, stop.Token);
        try
        {
            await ReadLinesAsync(process, sink, idle, cancellation);
            await process.WaitForExitAsync(cancellation);
            var tail = await errors;
            _logger.LogWarning("Plugin {Path} exited with code {Code}", _path, process.ExitCode);
            SetHealth(ProviderHealth.Failed, tail is { Length: > 0 } ? tail : $"exit code {process.ExitCode}");
        }
        finally
        {
            stop.Cancel();
            await idleLoop;
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _logger.LogDebug(ex, "Stopping plugin {Path} failed", _path);
                }
            }
        }
    }

    private async Task ReadLinesAsync(Process process, SignalSink sink, IdleTracker idle, CancellationToken cancellation)
    {
        var problems = 0;
        ActivitySignal? lastForeground = null, lastLock = null, lastMedia = null;
        var window = Stopwatch.StartNew();
        var inWindow = 0;
        var dropped = 0L;
        while (await process.StandardOutput.ReadLineAsync(cancellation) is { } line)
        {
            if (window.ElapsedMilliseconds >= 1000)
            {
                window.Restart();
                inWindow = 0;
            }

            if (++inWindow > MaxLinesPerSecond)
            {
                if (dropped++ == 0)
                {
                    _logger.LogWarning("Plugin {Path} writes more than {Max} lines a second; the extra lines are dropped", _path, MaxLinesPerSecond);
                }

                continue;
            }

            var result = ProviderProtocol.Parse(line);
            if (result.Error is not null)
            {
                if (problems++ < MaxLoggedProblems)
                {
                    _logger.LogWarning("Plugin {Path} wrote a line DeskPal cannot use: {Error}", _path, result.Error);
                }

                continue;
            }

            if (result.Message is null)
            {
                continue;
            }

            if (result.Message is ProviderHello hello)
            {
                OnHello(hello);
                continue;
            }

            switch (result.Message)
            {
                case ProviderForeground foreground:
                    Emit(sink, ProviderProtocol.ToSignal(foreground), ref lastForeground);
                    break;
                case ProviderDomain domain:
                    // Not deduplicated: after every browser window change the pipeline waits for the site to be read
                    // again (ActivitySnapshot.DomainPending), and a repeat of the same site is that answer.
                    if (_provides.Contains(ProviderCapability.Domain))
                    {
                        sink.Publish(new BrowserDomainSignal(domain.Domain));
                    }

                    break;
                case ProviderLock locked:
                    Emit(sink, new LockSignal(locked.Locked), ref lastLock);
                    break;
                case ProviderMedia media:
                    Emit(sink, ProviderProtocol.ToSignal(media), ref lastMedia);
                    break;
                case ProviderDetail detail:
                    // Not deduplicated, like domain: the host keeps the last report and compares it with the window.
                    if (_provides.Contains(ProviderCapability.Detail))
                    {
                        sink.Publish(new DetailSignal(detail.Process, detail.Window, detail.Title));
                    }

                    break;
                case ProviderIdle report:
                    lock (_lock)
                    {
                        idle.Report(report.Seconds, DateTimeOffset.UtcNow);
                    }

                    break;
            }

            // Any valid fact means it works; a plugin may never report a window (one that only supplies media).
            if (_health != ProviderHealth.Running)
            {
                SetHealth(ProviderHealth.Running, null);
            }
        }
    }

    /// <summary>Publishes a fact once when it changes. Facts outside what the plugin declared are not used.</summary>
    private void Emit(SignalSink sink, ActivitySignal signal, ref ActivitySignal? last)
    {
        if (signal == last)
        {
            return;
        }

        last = signal;
        if (_provides.Contains(ProviderCapabilities.Of(signal)))
        {
            sink.Publish(signal);
        }
    }

    /// <summary>Checks the idle reports against the user's setting every few seconds and publishes the changes.</summary>
    private async Task RunIdleAsync(IdleTracker idle, SignalSink sink, CancellationToken cancellation)
    {
        bool? last = null;
        try
        {
            using var timer = new PeriodicTimer(IdleCheck);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                bool now;
                lock (_lock)
                {
                    now = idle.IsIdle(DateTimeOffset.UtcNow, _idleThreshold());
                }

                if (now != last && _provides.Contains(ProviderCapability.Idle))
                {
                    last = now;
                    sink.Publish(new IdleSignal(now));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnHello(ProviderHello hello)
    {
        _logger.LogInformation("Plugin {Path} is {Name} {Version}, supplies {Provides}, apps {Apps}",
            _path, hello.Name, hello.Version, hello.Provides is null ? "(default)" : string.Join(",", hello.Provides.Select(c => c.Name())),
            hello.Apps is null ? "(none)" : string.Join(",", hello.Apps));
        _helloName = hello.Name;
        _version = hello.Version;
        var provides = hello.Provides ?? ProviderCapabilities.PluginDefault;
        var apps = hello.Apps ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!provides.SetEquals(_provides) || !apps.SetEquals(_apps))
        {
            _provides = provides;
            _apps = apps;
            ProvidesAnnounced?.Invoke(provides, apps);
        }

        Changed?.Invoke();
    }

    /// <summary>The plugin's error output goes to the log; the last line is shown in Settings when it exits.</summary>
    private async Task<string?> ReadErrorsAsync(Process process)
    {
        string? last = null;
        var logged = 0;
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                last = line.Length > 200 ? line[..200] : line;
                if (logged++ < 20)
                {
                    _logger.LogWarning("Plugin {Path}: {Line}", _path, last);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        return last;
    }

    private void SetHealth(ProviderHealth health, string? error)
    {
        if (_health == health && _error == error)
        {
            return;
        }

        _health = health;
        _error = error;
        Changed?.Invoke();
    }
}
