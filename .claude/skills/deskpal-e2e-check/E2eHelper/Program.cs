using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

// 端到端自检的辅助程序：建测试账号和房间、模拟房间成员、写入和恢复测试客户端的设置。三个平台通用。
// 所有状态放在仓库的 .local/tmp/e2e/，token 只写进 accounts.json，不打印。
Console.OutputEncoding = System.Text.Encoding.UTF8;

var root = FindRepoRoot();
var work = Path.Combine(root, ".local", "tmp", "e2e");
Directory.CreateDirectory(work);
var accountsPath = Path.Combine(work, "accounts.json");
var backupPath = Path.Combine(work, "settings-backup");
var server = (Environment.GetEnvironmentVariable("DESKPAL_E2E_SERVER") ?? "http://localhost:5180").TrimEnd('/');
var json = DeskPalJson.Options;

try
{
    return args.FirstOrDefault() switch
    {
        "setup" => await Setup(),
        "member" when args.Length >= 3 => await Member(args[1], int.Parse(args[2]), args.ElementAtOrDefault(3) ?? "video"),
        "block" when args.Length == 3 => await Block(args[1], args[2], block: true),
        "unblock" when args.Length == 3 => await Block(args[1], args[2], block: false),
        "client-settings" => ClientSettings(),
        "restore-settings" => RestoreSettings(),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
{
    Console.Error.WriteLine($"失败：{ex.Message}");
    return 1;
}

int Usage()
{
    Console.WriteLine("""
        用法（在仓库根目录运行 dotnet run --project .claude/skills/deskpal-e2e-check/E2eHelper -- <命令>）：
          setup                          注册 alice、bob、carol，alice 建房间，bob、carol 加入，写 .local/tmp/e2e/accounts.json
          member <bob|carol> <秒> [状态]  以该成员连上 Hub，发一条状态后打印收到的消息。状态：video（默认）、working、paused、none（只收不发）
          block <屏蔽者> <被屏蔽者>       例如 block alice bob；unblock 同理
          client-settings                把本机客户端设置目录移到备份，写入 alice 的测试设置（连本地 Server）
          restore-settings               删除测试设置，把备份移回去
        Server 地址默认 http://localhost:5180，可用环境变量 DESKPAL_E2E_SERVER 改。
        """);
    return 2;
}

async Task<int> Setup()
{
    using var http = new HttpClient { BaseAddress = new Uri(server) };
    var suffix = Random.Shared.Next(10000, 99999);
    var accounts = new Accounts();
    foreach (var (name, display) in new[] { ("alice", "Alice"), ("bob", "Bob"), ("carol", "Carol") })
    {
        var auth = await Post<AuthResponse>(http, "/api/auth/register",
            new RegisterRequest($"e2e_{name}{suffix}", display, "password123"), null);
        accounts.Members[name] = new Account(auth.UserId, auth.UserName, auth.DisplayName, auth.Token);
    }

    var alice = accounts.Members["alice"];
    var room = await Post<RoomDto>(http, "/api/rooms", new CreateRoomRequest("测试房间"), alice.Token);
    accounts.RoomId = room.Id;
    foreach (var name in new[] { "bob", "carol" })
    {
        var invite = await Post<InviteDto>(http, $"/api/rooms/{room.Id}/invites", new { }, alice.Token);
        await Post<JsonElement>(http, "/api/rooms/join", new JoinRoomRequest(invite.Code), accounts.Members[name].Token);
    }

    File.WriteAllText(accountsPath, JsonSerializer.Serialize(accounts, json));
    Console.WriteLine($"房间 {room.Id}：{string.Join("、", accounts.Members.Select(m => $"{m.Key}={m.Value.UserName}"))}");
    return 0;
}

async Task<int> Member(string name, int seconds, string state)
{
    var accounts = LoadAccounts();
    var me = accounts.Members[name];
    var names = accounts.Members.ToDictionary(m => m.Value.UserId, m => m.Key);
    var hub = new HubConnectionBuilder()
        .WithUrl(server + HubContract.Path, o => o.AccessTokenProvider = () => Task.FromResult<string?>(me.Token))
        .AddJsonProtocol(o => o.PayloadSerializerOptions = new JsonSerializerOptions(json))
        .Build();
    hub.On<Envelope>(HubContract.Receive, e =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {name} RECV {e.Type} from={names.GetValueOrDefault(e.SenderId, e.SenderId.ToString())} payload={e.Payload}"));
    await hub.StartAsync();

    PresenceSnapshot? snapshot = state switch
    {
        "video" => new PresenceSnapshot
        {
            State = PresenceState.Slacking,
            Category = ActivityCategory.Video,
            AppDisplayName = "哔哩哔哩",
            Detail = "【猫猫】合集",
            Since = DateTimeOffset.UtcNow.AddMinutes(-12),
        },
        "working" => new PresenceSnapshot
        {
            State = PresenceState.Working,
            Category = ActivityCategory.Work,
            AppDisplayName = "Visual Studio Code",
            Since = DateTimeOffset.UtcNow.AddMinutes(-3),
        },
        "paused" => new PresenceSnapshot { State = PresenceState.Paused, Since = DateTimeOffset.UtcNow },
        "none" => null,
        _ => throw new InvalidOperationException($"不认识的状态 {state}"),
    };
    if (snapshot is not null)
    {
        await hub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, accounts.RoomId, snapshot));
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {name} published {state}");
    }

    await Task.Delay(TimeSpan.FromSeconds(seconds));
    await hub.DisposeAsync();
    return 0;
}

async Task<int> Block(string blocker, string target, bool block)
{
    var accounts = LoadAccounts();
    using var http = new HttpClient { BaseAddress = new Uri(server) };
    var token = accounts.Members[blocker].Token;
    var targetId = accounts.Members[target].UserId;
    if (block)
    {
        await Post<JsonElement>(http, $"/api/rooms/{accounts.RoomId}/blocks", new BlockMemberRequest(targetId), token);
    }
    else
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/rooms/{accounts.RoomId}/blocks/{targetId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    Console.WriteLine($"{blocker} {(block ? "屏蔽了" : "取消屏蔽")} {target}");
    return 0;
}

int ClientSettings()
{
    var accounts = LoadAccounts();
    var dir = SettingsStore.Directory;
    if (Directory.Exists(backupPath))
    {
        throw new InvalidOperationException($"已有备份 {backupPath}，先运行 restore-settings");
    }

    Directory.CreateDirectory(backupPath);
    if (Directory.Exists(dir))
    {
        MoveDirectory(dir, Path.Combine(backupPath, "DeskPal"));
    }

    // 和各平台客户端启动时设置的一致：Windows 用 DPAPI，macOS、Linux 用设置目录里的 local.key。
    SecretProtection.Current = OperatingSystem.IsWindows() ? new DpapiProtector() : new FileKeyProtector(dir);
    var alice = accounts.Members["alice"];
    var settings = new AppSettings
    {
        ServerUrl = server,
        UserId = alice.UserId,
        UserName = alice.UserName,
        DisplayName = alice.DisplayName,
        SelectedRoomId = accounts.RoomId,
        IdleMinutes = 5,
    };
    settings.SetToken(alice.Token);
    new SettingsStore().Save(settings);
    Console.WriteLine($"已备份原设置到 {backupPath}，写入 alice 的测试设置到 {dir}");
    return 0;
}

int RestoreSettings()
{
    var dir = SettingsStore.Directory;
    if (!Directory.Exists(backupPath))
    {
        throw new InvalidOperationException("没有备份，什么都没做");
    }

    if (Directory.Exists(dir))
    {
        Directory.Delete(dir, recursive: true);
    }

    var saved = Path.Combine(backupPath, "DeskPal");
    if (Directory.Exists(saved))
    {
        MoveDirectory(saved, dir);
    }

    Directory.Delete(backupPath, recursive: true);
    Console.WriteLine($"已恢复 {dir}");
    return 0;
}

async Task<T> Post<T>(HttpClient http, string path, object body, string? token)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: json) };
    if (token is not null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException($"POST {path} 返回 {(int)response.StatusCode}：{text}");
    }

    return text.Length == 0 ? default! : JsonSerializer.Deserialize<T>(text, json)!;
}

Accounts LoadAccounts() => File.Exists(accountsPath)
    ? JsonSerializer.Deserialize<Accounts>(File.ReadAllText(accountsPath), json)!
    : throw new InvalidOperationException("还没有 accounts.json，先运行 setup");

static void MoveDirectory(string from, string to)
{
    try
    {
        Directory.Move(from, to);
    }
    catch (IOException)
    {
        // 跨文件系统时 Move 会失败，改为复制后删除。
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        Directory.Delete(from, recursive: true);
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeskPal.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("请在 DeskPal 仓库里运行");
}

sealed class Accounts
{
    public Guid RoomId { get; set; }
    public Dictionary<string, Account> Members { get; set; } = [];
}

sealed record Account(Guid UserId, string UserName, string DisplayName, string Token);

sealed class DpapiProtector : ISecretProtector
{
#pragma warning disable CA1416 // 只在 Windows 上创建
    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
}
