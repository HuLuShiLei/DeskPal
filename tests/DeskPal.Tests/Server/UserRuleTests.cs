using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DeskPal.Server.Features.Rules;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DeskPal.Tests.Server;

public sealed class UserRuleTests : IClassFixture<UserRuleTests.ServerFactory>
{
    private const string MaintainerToken = "test-maintainer-token";
    private readonly ServerFactory _factory;

    public UserRuleTests(ServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Personal_rules_follow_the_account_and_stay_private()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        var bob = await RegisterAsync(http, "bob");
        var room = await SendAsync<RoomDto>(http, HttpMethod.Post, "/api/rooms", new CreateRoomRequest("Home"), alice.Token);
        var invite = await SendAsync<InviteDto>(http, HttpMethod.Post, $"/api/rooms/{room.Id}/invites", new { }, alice.Token);
        await SendAsync<RoomDto>(http, HttpMethod.Post, "/api/rooms/join", new JoinRoomRequest(invite.Code), bob.Token);

        var (_, aliceInbox) = await ConnectAsync(alice.Token);
        var (_, bobInbox) = await ConnectAsync(bob.Token);
        await EnablePersonalSyncAsync(http, alice.Token);
        await NextAsync(aliceInbox, e => e.Type == MessageTypes.PersonalRulesChanged);

        var saved = await SendAsync<UserRuleDto>(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "WeChat.exe", ActivityCategory.Work, "微信"), alice.Token);
        Assert.Equal("wechat", saved.Pattern);
        await NextAsync(aliceInbox, e => e.Type == MessageTypes.PersonalRulesChanged);

        Assert.Single(await SendAsync<List<UserRuleDto>>(http, HttpMethod.Get, "/api/me/rules", null, alice.Token));
        using (var bobRules = await RawAsync(http, HttpMethod.Get, "/api/me/rules", null, bob.Token))
        {
            Assert.Equal(HttpStatusCode.Forbidden, bobRules.StatusCode);
        }

        await Task.Delay(300);
        while (bobInbox.TryRead(out var envelope))
        {
            Assert.NotEqual(MessageTypes.PersonalRulesChanged, envelope.Type);
        }

        var updated = await SendAsync<UserRuleDto>(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "wechat", ActivityCategory.Communication, "微信"), alice.Token);
        Assert.Equal(ActivityCategory.Communication, updated.Category);
        Assert.Single(await SendAsync<List<UserRuleDto>>(http, HttpMethod.Get, "/api/me/rules", null, alice.Token));

        using var bad = await RawAsync(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "x", ActivityCategory.Music, null), alice.Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        using var deleted = await RawAsync(http, HttpMethod.Delete, "/api/me/rules?kind=Process&pattern=WeChat.exe", null, alice.Token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await SendAsync<List<UserRuleDto>>(http, HttpMethod.Get, "/api/me/rules", null, alice.Token));
    }

    [Fact]
    public async Task Candidates_count_only_users_who_agreed_to_share()
    {
        var http = _factory.CreateClient();
        var users = new List<AuthResponse>();
        for (var i = 0; i < 3; i++)
        {
            users.Add(await RegisterAsync(http, $"share{i}"));
        }

        var silent = await RegisterAsync(http, "silent");
        Assert.Null((await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, silent.Token)).ShareUsage);

        using var refused = await RawAsync(http, HttpMethod.Post, "/api/me/usage", new UsageReportRequest([new UnknownAppItem(RuleKind.Process, "toolx", "ToolX", 30)]), silent.Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var toolX = new UnknownAppItem(RuleKind.Process, "ToolX.exe", "ToolX", 30);
        await EnablePersonalSyncAsync(http, users[0].Token);
        foreach (var user in users)
        {
            await SendAsync<object>(http, HttpMethod.Put, "/api/me/sharing", new SetSharingRequest(true), user.Token);
        }

        await SendAsync<UserRuleDto>(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "feishu-lite", ActivityCategory.Work, "飞书轻量版"), users[0].Token);
        await SendAsync<object>(http, HttpMethod.Post, "/api/me/usage", new UsageReportRequest([toolX]), users[0].Token);
        foreach (var user in users.Skip(1))
        {
            await SendAsync<object>(http, HttpMethod.Post, "/api/me/usage", new UsageReportRequest([toolX], [new SharedRuleItem("feishu-lite", ActivityCategory.Work, "飞书轻量版")]), user.Token);
        }

        await EnablePersonalSyncAsync(http, silent.Token);
        await SendAsync<UserRuleDto>(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "feishu-lite", ActivityCategory.Game, null), silent.Token);
        Assert.True((await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, users[0].Token)).ShareUsage);

        using var anonymous = await RawAsync(http, HttpMethod.Get, "/api/rules/candidates", null, null);
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rules/candidates");
        request.Headers.Add(RuleEndpoints.MaintainerHeader, MaintainerToken);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var candidates = (await response.Content.ReadFromJsonAsync<RuleCandidatesResponse>(DeskPalJson.Options))!;

        var consensus = Assert.Single(candidates.Consensus, c => c.Pattern == "feishu-lite");
        Assert.Equal((ActivityCategory.Work, 3, 3, "飞书轻量版"), (consensus.Category, consensus.Users, consensus.Agreeing, consensus.Name));
        var unknown = Assert.Single(candidates.Unknown, c => c.Pattern == "toolx");
        Assert.Equal((3, 90, "ToolX"), (unknown.Users, unknown.TotalMinutes, unknown.DisplayName));
        Assert.DoesNotContain(candidates.Unknown, c => c.Pattern == "feishu-lite");

        await SendAsync<object>(http, HttpMethod.Put, "/api/me/sharing", new SetSharingRequest(false), users[2].Token);
        Assert.DoesNotContain((await CandidatesAsync(http)).Consensus, c => c.Pattern == "feishu-lite");
    }

    [Fact]
    public async Task Candidates_include_guessed_games_and_single_changes()
    {
        var http = _factory.CreateClient();
        var users = new List<AuthResponse>();
        for (var i = 0; i < 3; i++)
        {
            var user = await RegisterAsync(http, $"guess{i}");
            await SendAsync<object>(http, HttpMethod.Put, "/api/me/sharing", new SetSharingRequest(true), user.Token);
            users.Add(user);
        }

        var stray = new UnknownAppItem(RuleKind.Process, "Stray-Win64-Shipping", "Stray", 40, ActivityCategory.Game);
        foreach (var user in users)
        {
            await SendAsync<object>(http, HttpMethod.Post, "/api/me/usage", new UsageReportRequest([stray]), user.Token);
        }

        await SendAsync<object>(http, HttpMethod.Post, "/api/me/usage", new UsageReportRequest(
            [stray, new UnknownAppItem(RuleKind.Process, "rare-tool", "Rare Tool", 15), new UnknownAppItem(RuleKind.Process, "rare-game", "Rare", 20, ActivityCategory.Game)],
            [new SharedRuleItem("codex", ActivityCategory.Other, "Codex")]), users[0].Token);

        var candidates = await CandidatesAsync(http);

        var guessed = Assert.Single(candidates.Guessed!, g => g.Pattern == "stray-win64-shipping");
        Assert.Equal(("Stray", 3, ActivityCategory.Game), (guessed.DisplayName, guessed.Users, guessed.Guessed!.Value));
        Assert.DoesNotContain(candidates.Unknown, u => u.Pattern == "stray-win64-shipping");
        Assert.Contains(candidates.Unknown, u => u.Pattern == "rare-tool" && u.Users == 1 && u.Guessed is null);
        Assert.Contains(candidates.Unknown, u => u.Pattern == "rare-game" && u.Guessed == ActivityCategory.Game);
        var signal = Assert.Single(candidates.Signals!, s => s.Pattern == "codex");
        Assert.Equal((ActivityCategory.Other, ActivityCategory.Work, 1), (signal.Category, signal.Builtin!.Value, signal.Users));
        Assert.DoesNotContain(candidates.Consensus, c => c.Pattern == "codex");

        var builtin = await SendAsync<BuiltinRulesResponse>(http, HttpMethod.Get, "/api/rules/builtin?version=0", null, users[0].Token);
        Assert.Contains(builtin.Folders!, f => f.Folder == @"\steamapps\common\" && f.Category == ActivityCategory.Game);
    }

    private static async Task<RuleCandidatesResponse> CandidatesAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rules/candidates");
        request.Headers.Add(RuleEndpoints.MaintainerHeader, MaintainerToken);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RuleCandidatesResponse>(DeskPalJson.Options))!;
    }

    private static Task<PersonalDataDto> EnablePersonalSyncAsync(HttpClient http, string token) =>
        SendAsync<PersonalDataDto>(http, HttpMethod.Post, "/api/me/personal-sync", new PersonalDataDto([], []), token);

    private async Task<(HubConnection Hub, ChannelReader<Envelope> Inbox)> ConnectAsync(string token)
    {
        var inbox = Channel.CreateUnbounded<Envelope>();
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, HubContract.Path.TrimStart('/')), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        hub.On<Envelope>(HubContract.Receive, e => inbox.Writer.TryWrite(e));
        await hub.StartAsync();
        return (hub, inbox.Reader);
    }

    private static async Task<Envelope> NextAsync(ChannelReader<Envelope> inbox, Func<Envelope, bool> match)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var envelope = await inbox.ReadAsync(cts.Token);
            if (match(envelope))
            {
                return envelope;
            }
        }
    }

    private static Task<AuthResponse> RegisterAsync(HttpClient http, string name) =>
        SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/register", new RegisterRequest($"{name}{Guid.NewGuid():N}"[..20], name, "password123"), null);

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, object? body, string? token)
    {
        using var response = await RawAsync(http, method, path, body, token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(DeskPalJson.Options))!;
    }

    private static Task<HttpResponseMessage> RawAsync(HttpClient http, HttpMethod method, string path, object? body, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: DeskPalJson.Options);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        return http.SendAsync(request);
    }

    public sealed class ServerFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deskpal-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=False");
            builder.UseSetting("Rules:MinUsers", "3");
            builder.UseSetting("Rules:MinAccountAgeDays", "0");
            builder.UseSetting("Rules:MaintainerToken", MaintainerToken);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(_dbPath);
        }
    }
}
