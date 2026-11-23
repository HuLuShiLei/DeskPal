using System.Diagnostics;
using System.Text;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// Runs a provider plugin: a program the user put in the providers folder that prints one JSON object per line
/// (<see cref="ProviderProtocol"/>). Its lines become the same signals the built-in collectors publish.
/// The plugin runs as its own process with the user's rights; DeskPal gives it no token and no settings, only reads
/// its output, and starts it again with a growing pause when it exits.
/// </summary>
public sealed class ExternalProvider(string path, ActivityBus bus, ILogger logger)
{
    private const int MaxLinesPerSecond = 50;
    private const int MaxLoggedProblems = 5;
    private static readonly int[] RestartDelaysSeconds = [1, 2, 5, 10, 30, 60];

    /// <summary>What the plugin called itself in its hello line.</summary>
    public string? Hello { get; private set; }

    /// <summary>Why the plugin is not delivering; null while it runs.</summary>
    public string? Error { get; private set; }

    /// <summary>At least one valid foreground line arrived since the last start.</summary>
    public bool Delivering { get; private set; }

    public event Action? Changed;

    /// <summary>Runs until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        var attempt = 0;
        while (!cancellation.IsCancellationRequested)
        {
            var started = Stopwatch.StartNew();
            try
            {
                await RunOnceAsync(cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Provider {Path} could not be run", path);
                Set(error: ex.Message);
            }

            // A plugin that worked for a while and then stopped gets a quick restart; one that fails at once backs off.
            attempt = started.Elapsed > TimeSpan.FromMinutes(1) ? 0 : Math.Min(attempt + 1, RestartDelaysSeconds.Length - 1);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(RestartDelaysSeconds[attempt]), cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? "",
        };
        info.Environment["DESKPAL_PROTOCOL"] = ProviderProtocol.Version.ToString();

        using var process = Process.Start(info) ?? throw new InvalidOperationException("The program did not start.");
        logger.LogInformation("Provider {Path} started (pid {Pid})", path, process.Id);
        Delivering = false;
        Set(error: null);
        process.StandardInput.Close();
        var problems = 0;
        var errors = ReadErrorsAsync(process);
        try
        {
            ForegroundSignal? lastForeground = null;
            BrowserDomainSignal? lastDomain = null;
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
                        logger.LogWarning("Provider {Path} writes more than {Max} lines a second; the extra lines are dropped", path, MaxLinesPerSecond);
                    }

                    continue;
                }

                var result = ProviderProtocol.Parse(line);
                if (result.Error is not null)
                {
                    if (problems++ < MaxLoggedProblems)
                    {
                        logger.LogWarning("Provider {Path} wrote a line DeskPal cannot use: {Error}", path, result.Error);
                    }

                    continue;
                }

                switch (result.Message)
                {
                    case ProviderHello hello:
                        logger.LogInformation("Provider {Path} is {Name} {Version}", path, hello.Name, hello.Version);
                        Hello = hello.Version is null ? hello.Name : $"{hello.Name} {hello.Version}";
                        Changed?.Invoke();
                        break;
                    case ProviderForeground foreground:
                        var signal = ProviderProtocol.ToSignal(foreground);
                        if (signal != lastForeground)
                        {
                            lastForeground = signal;
                            bus.Publish(signal);
                        }

                        if (!Delivering)
                        {
                            Delivering = true;
                            Changed?.Invoke();
                        }

                        break;
                    case ProviderDomain domain:
                        var domainSignal = new BrowserDomainSignal(domain.Domain);
                        if (domainSignal != lastDomain)
                        {
                            lastDomain = domainSignal;
                            bus.Publish(domainSignal);
                        }

                        break;
                }
            }

            await process.WaitForExitAsync(cancellation);
            var tail = await errors;
            logger.LogWarning("Provider {Path} exited with code {Code}", path, process.ExitCode);
            Delivering = false;
            Set(error: tail is { Length: > 0 } ? tail : $"exit code {process.ExitCode}");
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    logger.LogDebug(ex, "Stopping provider {Path} failed", path);
                }
            }
        }
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
                    logger.LogWarning("Provider {Path}: {Line}", path, last);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        return last;
    }

    private void Set(string? error)
    {
        Error = error;
        Changed?.Invoke();
    }
}
