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
/// It runs as its own process with the user's rights; DeskPal gives it no token and no settings, reads its output,
/// and starts it again with a growing pause when it exits.
/// Its standard input is closed, unless its hello line asks to read the room (<see cref="ProviderReads.Room"/>): then
/// DeskPal writes the current <see cref="RoomFeed"/> line after the hello and again when the room changes, at most one
/// line a second. Whether to open the input is decided when the program starts, from the last hello it wrote; when a
/// hello asks for something else, the program is started again at once.
/// </summary>
public sealed class ExternalProvider : IActivityProvider
{
    private const int MaxLinesPerSecond = 50;
    private const int MaxLoggedProblems = 5;
    private static readonly int[] RestartDelaysSeconds = [1, 2, 5, 10, 30, 60];
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RoomInterval = TimeSpan.FromSeconds(1);

    private readonly string _path;
    private readonly Func<TimeSpan> _idleThreshold;
    private readonly ILogger _logger;
    private readonly IRoomLineSource? _room;
    private readonly Lock _lock = new();
    private string? _helloName;
    private string? _version;
    private IReadOnlySet<ProviderCapability> _provides;
    private IReadOnlySet<string> _apps;
    private IReadOnlySet<string> _reads;
    private ProviderHealth _health = ProviderHealth.Starting;
    private string? _error;

    /// <param name="knownProvides">What the plugin said it supplies the last time it ran; null when it never said.</param>
    /// <param name="knownApps">The apps its detail covered the last time it ran.</param>
    /// <param name="knownReads">What it asked to read the last time it ran.</param>
    /// <param name="room">Where the room line comes from; null gives no plugin anything to read.</param>
    public ExternalProvider(
        string path,
        IReadOnlySet<ProviderCapability>? knownProvides,
        IReadOnlySet<string>? knownApps,
        Func<TimeSpan> idleThreshold,
        ILogger logger,
        IReadOnlySet<string>? knownReads = null,
        IRoomLineSource? room = null)
    {
        _path = path;
        _idleThreshold = idleThreshold;
        _logger = logger;
        _room = room;
        _provides = knownProvides ?? ProviderCapabilities.PluginDefault;
        _apps = knownApps ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _reads = knownReads ?? ProviderReads.None;
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

    /// <summary>What it asked to read in its last hello line (<see cref="ProviderReads"/>).</summary>
    public IReadOnlySet<string> Reads => _reads;

    public ProviderHealth Health => _health;

    public IReadOnlyList<PluginNote> Notes
    {
        get
        {
            var notes = new List<PluginNote>();
            if (_reads.Contains(ProviderReads.Room))
            {
                notes.Add(new PluginNote("plugin.readsRoom"));
            }

            if (_error is not null)
            {
                notes.Add(new PluginNote("plugin.error", _error, "warn"));
            }

            return notes;
        }
    }

    public IReadOnlyList<string> Actions => [];

    public event Action? Changed;

    /// <summary>
    /// Raised when the hello line changed what the plugin supplies, the apps it covers or what it reads, so the host
    /// can remember it.
    /// </summary>
    public event Action<IReadOnlySet<ProviderCapability>, IReadOnlySet<string>, IReadOnlySet<string>>? ProvidesAnnounced;

    public Task<CommandResult> RunActionAsync(string action) => Task.FromResult(CommandResult.Fail(Strings.Plugin_UnknownAction));

    /// <summary>Runs until cancelled.</summary>
    public async Task RunAsync(SignalSink sink, CancellationToken cancellation)
    {
        var attempt = 0;
        var restartedForInput = false;
        while (!cancellation.IsCancellationRequested)
        {
            var started = Stopwatch.StartNew();
            try
            {
                // Its hello asked for a different standard input than it was started with: start it again at once,
                // only once in a row, so a plugin that keeps changing its mind still backs off.
                var restart = await RunOnceAsync(sink, cancellation);
                if (restart && !restartedForInput)
                {
                    restartedForInput = true;
                    continue;
                }

                restartedForInput = false;
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

    /// <summary>Runs the program once. Returns true when it was stopped to be started again with a different input.</summary>
    private async Task<bool> RunOnceAsync(SignalSink sink, CancellationToken cancellation)
    {
        var run = new RunState(_room is not null && _reads.Contains(ProviderReads.Room));
        var info = PluginFiles.StartInfo(_path);
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        info.CreateNoWindow = true;
        info.WorkingDirectory = Path.GetDirectoryName(_path) ?? "";
        info.Environment["DESKPAL_PROTOCOL"] = ProviderProtocol.Version.ToString();
        info.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = Process.Start(info) ?? throw new InvalidOperationException("The program did not start.");
        _logger.LogInformation("Plugin {Path} started (pid {Pid})", _path, process.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var input = Task.CompletedTask;
        Action? onRoom = null;
        if (run.Input)
        {
            var stdin = process.StandardInput;
            stdin.NewLine = "\n";
            run.Writer = new LatestLineWriter(
                async (line, token) =>
                {
                    await stdin.WriteLineAsync(line.AsMemory(), token);
                    await stdin.FlushAsync(token);
                },
                RoomInterval,
                ex => _logger.LogDebug(ex, "Writing to plugin {Path} failed", _path));
            input = run.Writer.RunAsync(stop.Token);
            onRoom = () => Post(run);
            _room!.Changed += onRoom;
        }
        else
        {
            process.StandardInput.Close();
        }

        var errors = ReadErrorsAsync(process);
        var idle = new IdleTracker();
        var idleLoop = RunIdleAsync(idle, sink, stop.Token);
        try
        {
            await ReadLinesAsync(process, sink, idle, run, cancellation);
            if (run.Restart)
            {
                return true;
            }

            await process.WaitForExitAsync(cancellation);
            var tail = await errors;
            _logger.LogWarning("Plugin {Path} exited with code {Code}", _path, process.ExitCode);
            SetHealth(ProviderHealth.Failed, tail is { Length: > 0 } ? tail : $"exit code {process.ExitCode}");
            return false;
        }
        finally
        {
            if (onRoom is not null)
            {
                _room!.Changed -= onRoom;
            }

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

            // A write stuck on a full pipe ends with the program; do not wait long for it.
            await Task.WhenAny(input, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
        }
    }

    /// <summary>Hands the current room line to the plugin, once it said hello.</summary>
    private void Post(RunState run)
    {
        if (run.Writer is { } writer && run.HelloSeen && _room is { } room)
        {
            try
            {
                writer.Post(room.Line);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Building the room line for plugin {Path} failed", _path);
            }
        }
    }

    private async Task ReadLinesAsync(Process process, SignalSink sink, IdleTracker idle, RunState run, CancellationToken cancellation)
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
                if (_room is not null && _reads.Contains(ProviderReads.Room) != run.Input)
                {
                    _logger.LogInformation("Plugin {Path} {Change} reading the room; starting it again", _path, run.Input ? "stopped" : "started");
                    run.Restart = true;
                    return;
                }

                run.HelloSeen = true;
                Post(run);
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
        _logger.LogInformation("Plugin {Path} is {Name} {Version}, supplies {Provides}, apps {Apps}, reads {Reads}",
            _path, hello.Name, hello.Version, hello.Provides is null ? "(default)" : string.Join(",", hello.Provides.Select(c => c.Name())),
            hello.Apps is null ? "(none)" : string.Join(",", hello.Apps),
            hello.Reads is null ? "(nothing)" : string.Join(",", hello.Reads));
        _helloName = hello.Name;
        _version = hello.Version;
        var provides = hello.Provides ?? ProviderCapabilities.PluginDefault;
        var apps = hello.Apps ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reads = hello.Reads ?? ProviderReads.None;
        if (!provides.SetEquals(_provides) || !apps.SetEquals(_apps) || !reads.SetEquals(_reads))
        {
            _provides = provides;
            _apps = apps;
            _reads = reads;
            ProvidesAnnounced?.Invoke(provides, apps, reads);
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

    /// <summary>One start of the program.</summary>
    private sealed class RunState(bool input)
    {
        /// <summary>Its standard input is open and gets the room line.</summary>
        public bool Input { get; } = input;

        public LatestLineWriter? Writer { get; set; }

        /// <summary>Set after the first hello; the room line is only written after it.</summary>
        public volatile bool HelloSeen;

        /// <summary>Stopped because the hello asked for a different input.</summary>
        public bool Restart { get; set; }
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
