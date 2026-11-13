using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Auth;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DeskPal.Tests.Server;

public sealed class PresenceHubTests : IClassFixture<PresenceHubTests.ServerFactory>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly ServerFactory _factory;

    public PresenceHubTests(ServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Presence_offline_and_leave_flow()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        var bob = await RegisterAsync(http, "bob");
        var carol = await RegisterAsync(http, "carol");
        var room = await PostAsync<RoomDto>(http, "/api/rooms", new CreateRoomRequest("Home"), alice.Token);
        var invite = await PostAsync<InviteDto>(http, $"/api/rooms/{room.Id}/invites", new { }, alice.Token);
        await PostAsync<RoomDto>(http, "/api/rooms/join", new JoinRoomRequest(invite.Code), bob.Token);

        var (aliceHub, _) = await ConnectAsync(alice.Token);
        var (bobHub, bobInbox) = await ConnectAsync(bob.Token);

        var video = new PresenceSnapshot { State = PresenceState.Slacking, Category = ActivityCategory.Video, AppDisplayName = "哔哩哔哩", Since = DateTimeOffset.UtcNow };
        await aliceHub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, room.Id, video) with { SenderId = carol.UserId });
        var received = await NextAsync(bobInbox, e => e.Type == MessageTypes.PresenceUpdate);
        Assert.Equal(alice.UserId, received.SenderId);
        Assert.Equal(ActivityCategory.Video, received.ReadPayload<PresenceSnapshot>()!.Category);

        await SendAsync<RoomDto>(http, HttpMethod.Put, $"/api/rooms/{room.Id}", new RenameRoomRequest("我们家"), alice.Token);
        Assert.Equal(room.Id, (await NextAsync(bobInbox, e => e.Type == MessageTypes.RoomRenamed)).RoomId);

        var (carolHub, _) = await ConnectAsync(carol.Token);
        await Assert.ThrowsAsync<HubException>(() => carolHub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, room.Id, video)));
        await Assert.ThrowsAsync<HubException>(() => aliceHub.InvokeAsync(HubContract.Publish, Envelope.Create("interaction.poke", room.Id, new RoomEventPayload(bob.UserId))));
        await Assert.ThrowsAsync<HubException>(() => aliceHub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.RoomDissolved, room.Id, new RoomEventPayload(null))));

        var (_, bobInbox2) = await ConnectAsync(bob.Token);
        var replayed = await NextAsync(bobInbox2, e => e.Type == MessageTypes.PresenceUpdate);
        Assert.Equal(PresenceState.Slacking, replayed.ReadPayload<PresenceSnapshot>()!.State);

        await aliceHub.DisposeAsync();
        var offline = await NextAsync(bobInbox, e => e.Type == MessageTypes.PresenceUpdate && e.ReadPayload<PresenceSnapshot>()!.State == PresenceState.Offline);
        Assert.Equal(alice.UserId, offline.SenderId);

        var (aliceHub2, _) = await ConnectAsync(alice.Token);
        await PostAsync<object>(http, $"/api/rooms/{room.Id}/leave", new { }, bob.Token);
        await NextAsync(bobInbox, e => e.Type == MessageTypes.RoomMemberLeft);
        await aliceHub2.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, room.Id, video));
        await Task.Delay(500);
        var leaked = new List<Envelope>();
        while (bobInbox.TryRead(out var envelope))
        {
            leaked.Add(envelope);
        }

        Assert.DoesNotContain(leaked, e => e.Type == MessageTypes.PresenceUpdate && e.RoomId == room.Id);

        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Blocking_stops_presence_both_ways_without_telling_the_blocked_member()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        var bob = await RegisterAsync(http, "bob");
        var carol = await RegisterAsync(http, "carol");
        var room = await PostAsync<RoomDto>(http, "/api/rooms", new CreateRoomRequest("Home"), alice.Token);
        var invite = await PostAsync<InviteDto>(http, $"/api/rooms/{room.Id}/invites", new { }, alice.Token);
        await PostAsync<RoomDto>(http, "/api/rooms/join", new JoinRoomRequest(invite.Code), bob.Token);
        await PostAsync<RoomDto>(http, "/api/rooms/join", new JoinRoomRequest(invite.Code), carol.Token);
        var (aliceHub, aliceInbox) = await ConnectAsync(alice.Token);
        var (bobHub, bobInbox) = await ConnectAsync(bob.Token);
        var (_, carolInbox) = await ConnectAsync(carol.Token);
        var working = new PresenceSnapshot { State = PresenceState.Working, Category = ActivityCategory.Work, Since = DateTimeOffset.UtcNow };

        await SendNoContentAsync(http, HttpMethod.Post, $"/api/rooms/{room.Id}/blocks", new BlockMemberRequest(bob.UserId), alice.Token);

        var shownOffline = await NextAsync(bobInbox, e => e.Type == MessageTypes.PresenceUpdate);
        Assert.Equal(alice.UserId, shownOffline.SenderId);
        Assert.Equal(PresenceState.Offline, shownOffline.ReadPayload<PresenceSnapshot>()!.State);
        Assert.Equal(bob.UserId, (await NextAsync(aliceInbox, e => e.Type == MessageTypes.RoomBlocksChanged)).ReadPayload<RoomEventPayload>()!.UserId);

        await aliceHub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, room.Id, working));
        await bobHub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, room.Id, working));
        Assert.Equal(alice.UserId, (await NextAsync(carolInbox, e => e.Type == MessageTypes.PresenceUpdate)).SenderId);
        Assert.Equal(bob.UserId, (await NextAsync(carolInbox, e => e.Type == MessageTypes.PresenceUpdate)).SenderId);
        await Task.Delay(500);
        Assert.DoesNotContain(Drain(bobInbox), e => e.Type is MessageTypes.PresenceUpdate or MessageTypes.RoomBlocksChanged);
        Assert.DoesNotContain(Drain(aliceInbox), e => e.Type == MessageTypes.PresenceUpdate);

        var (_, bobInbox2) = await ConnectAsync(bob.Token);
        await Task.Delay(500);
        Assert.DoesNotContain(Drain(bobInbox2), e => e.Type == MessageTypes.PresenceUpdate && e.SenderId == alice.UserId);
        Assert.Empty((await GetAsync<List<RoomDto>>(http, "/api/rooms", bob.Token)).Single().Blocked!);
        Assert.Equal([bob.UserId], (await GetAsync<List<RoomDto>>(http, "/api/rooms", alice.Token)).Single().Blocked);

        await SendNoContentAsync(http, HttpMethod.Delete, $"/api/rooms/{room.Id}/blocks/{bob.UserId}", null, alice.Token);
        Assert.Equal(PresenceState.Working, (await NextAsync(bobInbox, e => e.Type == MessageTypes.PresenceUpdate && e.SenderId == alice.UserId)).ReadPayload<PresenceSnapshot>()!.State);
        Assert.Equal(PresenceState.Working, (await NextAsync(aliceInbox, e => e.Type == MessageTypes.PresenceUpdate && e.SenderId == bob.UserId)).ReadPayload<PresenceSnapshot>()!.State);

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Signing_in_again_signs_out_the_other_computer()
    {
        var http = _factory.CreateClient();
        var name = $"solo{Guid.NewGuid():N}"[..20];
        var first = await PostAsync<AuthResponse>(http, "/api/auth/register", new RegisterRequest(name, "solo", "password123"), null);
        var (firstHub, firstInbox) = await ConnectAsync(first.Token);
        var closed = new TaskCompletionSource();
        firstHub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var second = await PostAsync<AuthResponse>(http, "/api/auth/login", new LoginRequest(name, "password123"), null);

        Assert.Equal(first.UserId, (await NextAsync(firstInbox, e => e.Type == MessageTypes.SessionReplaced)).ReadPayload<RoomEventPayload>()!.UserId);
        await closed.Task.WaitAsync(Wait);
        using var stale = new HttpRequestMessage(HttpMethod.Get, "/api/me") { Headers = { Authorization = new("Bearer", first.Token) } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(stale)).StatusCode);
        var reconnect = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync(first.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, reconnect.StatusCode);

        Assert.Equal(second.UserId, (await GetAsync<AuthResponse>(http, "/api/me", second.Token)).UserId);
        var (secondHub, _) = await ConnectAsync(second.Token);
        await secondHub.DisposeAsync();
    }

    [Fact]
    public async Task Tokens_issued_before_sessions_work_until_the_next_sign_in()
    {
        var http = _factory.CreateClient();
        var name = $"old{Guid.NewGuid():N}"[..20];
        var registered = await PostAsync<AuthResponse>(http, "/api/auth/register", new RegisterRequest(name, "old", "password123"), null);
        string legacyToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeskPalDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == registered.UserId);
            user.SessionId = null;
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<SessionCache>().Set(user.Id, null);
            legacyToken = scope.ServiceProvider.GetRequiredService<TokenService>().Create(user);
        }

        Assert.Equal(registered.UserId, (await GetAsync<AuthResponse>(http, "/api/me", legacyToken)).UserId);

        await PostAsync<AuthResponse>(http, "/api/auth/login", new LoginRequest(name, "password123"), null);
        using var stale = new HttpRequestMessage(HttpMethod.Get, "/api/me") { Headers = { Authorization = new("Bearer", legacyToken) } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(stale)).StatusCode);
    }
    private static List<Envelope> Drain(ChannelReader<Envelope> inbox)
    {
        var items = new List<Envelope>();
        while (inbox.TryRead(out var envelope))
        {
            items.Add(envelope);
        }

        return items;
    }

    private static async Task<T> GetAsync<T>(HttpClient http, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Authorization = new("Bearer", token) } };
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(DeskPalJson.Options))!;
    }

    private static async Task SendNoContentAsync(HttpClient http, HttpMethod method, string path, object? body, string token)
    {
        using var request = new HttpRequestMessage(method, path) { Headers = { Authorization = new("Bearer", token) } };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: DeskPalJson.Options);
        }

        using var response = await http.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_tells_room_members_and_allows_duplicate_names()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        var bob = await RegisterAsync(http, "bob");
        var room = await PostAsync<RoomDto>(http, "/api/rooms", new CreateRoomRequest("Home"), alice.Token);
        var invite = await PostAsync<InviteDto>(http, $"/api/rooms/{room.Id}/invites", new { }, alice.Token);
        await PostAsync<RoomDto>(http, "/api/rooms/join", new JoinRoomRequest(invite.Code), bob.Token);
        var (_, aliceInbox) = await ConnectAsync(alice.Token);
        var (bobHub, bobInbox) = await ConnectAsync(bob.Token);

        var renamed = await SendAsync<AuthResponse>(http, HttpMethod.Put, "/api/me/displayName", new UpdateDisplayNameRequest(" bob "), alice.Token);

        Assert.Equal("bob", renamed.DisplayName);
        Assert.Equal(alice.UserId, (await NextAsync(bobInbox, e => e.Type == MessageTypes.MemberRenamed)).ReadPayload<RoomEventPayload>()!.UserId);
        Assert.Equal(room.Id, (await NextAsync(aliceInbox, e => e.Type == MessageTypes.MemberRenamed)).RoomId);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rooms") { Headers = { Authorization = new("Bearer", bob.Token) } };
        var rooms = await (await http.SendAsync(request)).Content.ReadFromJsonAsync<List<RoomDto>>(DeskPalJson.Options);
        Assert.All(rooms!.Single().Members, m => Assert.Equal("bob", m.DisplayName));

        using var invalid = new HttpRequestMessage(HttpMethod.Put, "/api/me/displayName")
        {
            Content = JsonContent.Create(new UpdateDisplayNameRequest(new string('名', 21)), options: DeskPalJson.Options),
            Headers = { Authorization = new("Bearer", alice.Token) },
        };
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await http.SendAsync(invalid)).StatusCode);

        await bobHub.DisposeAsync();
    }

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
        using var cts = new CancellationTokenSource(Wait);
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
        PostAsync<AuthResponse>(http, "/api/auth/register", new RegisterRequest($"{name}{Guid.NewGuid():N}"[..20], name, "password123"), null);

    private static Task<T> PostAsync<T>(HttpClient http, string path, object body, string? token) =>
        SendAsync<T>(http, HttpMethod.Post, path, body, token);

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, object body, string? token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, body.GetType(), options: DeskPalJson.Options) };
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(DeskPalJson.Options))!;
    }

    public sealed class ServerFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deskpal-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=False");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(_dbPath);
        }
    }
}