using System.Collections.Concurrent;
using System.Diagnostics;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Providers;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskPal.Tests.Providers;

/// <summary>
/// Runs real plugin programs (Python scripts) to check their standard input: closed unless the hello asks to read the
/// room, and then the room line after the hello and after each change. Needs Python 3 on PATH (python3, or python on
/// Windows); without it the tests pass without checking anything.
/// </summary>
public sealed class ExternalProviderInputTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>Reads its input to the end, then says what it found as a window.</summary>
    private const string Ignores = """
        import json, sys, time
        print(json.dumps({"v": 1, "type": "hello", "name": "ignores", "provides": ["foreground"]}), flush=True)
        data = sys.stdin.read()
        print(json.dumps({"type": "foreground", "process": "eof" if data == "" else "data"}), flush=True)
        time.sleep(60)
        """;

    /// <summary>Asks for the room and reports every line it gets as a window: member count as process, room as title.</summary>
    private const string Reads = """
        import json, sys, time
        print(json.dumps({"v": 1, "type": "hello", "name": "reads", "provides": ["foreground"], "reads": ["room"]}), flush=True)
        for line in sys.stdin:
            message = json.loads(line)
            room = message["room"]["name"] if message["room"] else ""
            print(json.dumps({"type": "foreground", "process": "room-%d" % len(message["members"]), "title": room}), flush=True)
        time.sleep(60)
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deskpal-plugin-tests-" + Guid.NewGuid().ToString("N"));

    public ExternalProviderInputTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_plugin_that_does_not_ask_gets_a_closed_input()
    {
        if (!HasPython())
        {
            return;
        }

        var run = Start(Ignores, knownReads: null, new FakeRoom(RoomFeed.Build(null, Guid.Empty, _ => null, null)));

        Assert.Equal("eof", (await run.NextAsync()).ProcessName);
        await run.StopAsync();
    }

    [Fact]
    public async Task A_plugin_that_asks_gets_the_room_after_hello_and_on_changes()
    {
        if (!HasPython())
        {
            return;
        }

        var room = new FakeRoom(RoomFeed.Build(null, Guid.Empty, _ => null, null));
        IReadOnlySet<string>? announced = null;

        // Never ran before: started with a closed input, its hello asks for the room, so it is started again.
        var run = Start(Reads, knownReads: null, room, provider => provider.ProvidesAnnounced += (_, _, reads) => announced = reads);

        var first = await run.NextAsync();
        Assert.Equal("room-0", first.ProcessName);
        Assert.Equal(new HashSet<string> { ProviderReads.Room }, announced);
        Assert.Contains(run.Provider.Notes, n => n.Key == "plugin.readsRoom");

        var me = Guid.NewGuid();
        room.Set(RoomFeed.Build(
            new RoomDto(Guid.NewGuid(), "测试房间", me, 20, DateTimeOffset.UtcNow,
                [new RoomMemberDto(me, "我", RoomRole.Owner, DateTimeOffset.UtcNow), new RoomMemberDto(Guid.NewGuid(), "张三", RoomRole.Member, DateTimeOffset.UtcNow)]),
            me, _ => null, null));

        var second = await run.NextAsync();
        Assert.Equal(("room-2", "测试房间"), (second.ProcessName, second.WindowTitle));
        await run.StopAsync();
    }

    [Fact]
    public async Task A_plugin_that_stops_asking_gets_a_closed_input_again()
    {
        if (!HasPython())
        {
            return;
        }

        // Asked for the room last time; this version does not, so it is started again with a closed input.
        var run = Start(Ignores, knownReads: new HashSet<string> { ProviderReads.Room }, new FakeRoom("""{"v":1,"type":"room","room":null,"members":[]}"""));

        Assert.Equal("eof", (await run.NextAsync()).ProcessName);
        Assert.Empty(run.Provider.Reads);
        await run.StopAsync();
    }

    private Run Start(string script, IReadOnlySet<string>? knownReads, FakeRoom room, Action<ExternalProvider>? setUp = null)
    {
        var path = Path.Combine(_folder, $"plugin-{Guid.NewGuid():N}.py");
        File.WriteAllText(path, (OperatingSystem.IsWindows() ? "" : "#!/usr/bin/env python3\n") + script.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        }

        var provider = new ExternalProvider(path, null, null, () => TimeSpan.FromMinutes(5), NullLogger.Instance, knownReads, room);
        setUp?.Invoke(provider);
        var signals = new BlockingCollection<ForegroundSignal>();
        var stop = new CancellationTokenSource();
        var task = provider.RunAsync(new SignalSink(s =>
        {
            if (s is ForegroundSignal foreground)
            {
                signals.Add(foreground);
            }
        }), stop.Token);
        return new Run(provider, signals, stop, task);
    }

    private static bool HasPython()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "python.exe" : "python3", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record Run(ExternalProvider Provider, BlockingCollection<ForegroundSignal> Signals, CancellationTokenSource Stop, Task Task)
    {
        public Task<ForegroundSignal> NextAsync() => Task.Run(() =>
        {
            Assert.True(Signals.TryTake(out var signal, Wait), "The plugin wrote nothing in time.");
            return signal!;
        });

        public async Task StopAsync()
        {
            await Stop.CancelAsync();
            await Task.WhenAny(Task, Task.Delay(Wait));
            Stop.Dispose();
        }
    }

    private sealed class FakeRoom(string line) : IRoomLineSource
    {
        private volatile string _line = line;

        public string Line => _line;

        public event Action? Changed;

        public void Set(string line)
        {
            _line = line;
            Changed?.Invoke();
        }
    }
}
