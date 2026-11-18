using System.Net;
using System.Net.Http.Json;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Stats;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DeskPal.Tests.Server;

public sealed class StatsSyncEndpointTests : IClassFixture<StatsSyncEndpointTests.ServerFactory>
{
    private const string Base = "/api/me/stats-sync";
    private static readonly DateOnly Day = new(2026, 11, 11);
    private readonly ServerFactory _factory;

    public StatsSyncEndpointTests(ServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Days_are_stored_as_sent_and_only_for_the_own_account()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterListedAsync(http, "sync-days");
        var bob = await RegisterListedAsync(http, "sync-bob");

        var off = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token);
        Assert.False(off.Enabled);
        Assert.True(off.Allowed);
        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([1, 2, 3]), alice.Token, HttpStatusCode.Conflict, ApiErrorCodes.StatsSyncNotEnabled);

        await ExpectErrorAsync(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("", "check"), alice.Token, HttpStatusCode.BadRequest, ApiErrorCodes.StatsSyncInvalid);
        var on = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-a", "check-a"), alice.Token);
        Assert.True(on.Enabled);
        Assert.Equal("salt-a", on.Salt);
        await ExpectErrorAsync(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-b", "check-b"), alice.Token, HttpStatusCode.Conflict, ApiErrorCodes.StatsSyncAlreadyEnabled);

        var first = await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([1, 2, 3]), alice.Token);
        var second = await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-12", Upload([4]), alice.Token);
        var again = await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([5, 6]), alice.Token);
        Assert.True(first.Revision < second.Revision && second.Revision < again.Revision);
        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/days/2026-11-11", new UploadStatsDayRequest("not base64!"), alice.Token, HttpStatusCode.BadRequest, ApiErrorCodes.StatsSyncInvalid);

        var day = await SendAsync<StatsSyncDayDto>(http, HttpMethod.Get, $"{Base}/days/2026-11-11", null, alice.Token);
        Assert.Equal(new byte[] { 5, 6 }, Convert.FromBase64String(day.Data));
        var status = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token);
        Assert.Equal([new StatsSyncDayInfo(Day, again.Revision), new StatsSyncDayInfo(Day.AddDays(1), second.Revision)], status.Days);

        var bobStatus = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, bob.Token);
        Assert.False(bobStatus.Enabled);
        Assert.Empty(bobStatus.Days);
        using (var missing = await RawAsync(http, HttpMethod.Get, $"{Base}/days/2026-11-11", null, bob.Token))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var anonymous = await RawAsync(http, HttpMethod.Get, Base, null, null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    [Fact]
    public async Task Turning_off_deletes_every_day()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterListedAsync(http, "sync-off");
        await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-a", "check-a"), alice.Token);
        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([2]), alice.Token);
        using (var disabled = await RawAsync(http, HttpMethod.Delete, Base, null, alice.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        }

        var afterOff = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token);
        Assert.Equal(new StatsSyncStatusDto(false, null, null, [], Allowed: true), afterOff with { Days = [] });
        Assert.Empty(afterOff.Days);
        using (var gone = await RawAsync(http, HttpMethod.Get, $"{Base}/days/2026-11-11", null, alice.Token))
        {
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }

        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([3]), alice.Token, HttpStatusCode.Conflict, ApiErrorCodes.StatsSyncNotEnabled);
        var reopened = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-b", "check-b"), alice.Token);
        Assert.Equal("salt-b", reopened.Salt);
        Assert.Empty(reopened.Days);
    }

    [Fact]
    public async Task A_day_over_the_old_limit_is_accepted()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterListedAsync(http, "sync-size");
        await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-a", "check-a"), alice.Token);
        var big = new byte[1024 * 1024 + 1];

        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload(big), alice.Token);
        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-12", Upload(big), alice.Token);

        Assert.Equal(2, (await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token)).Days.Count);
    }

    [Fact]
    public async Task Accounts_not_listed_cannot_use_sync()
    {
        var http = _factory.CreateClient();
        var carol = await RegisterAsync(http, "carol");

        var status = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, carol.Token);
        Assert.Equal(new StatsSyncStatusDto(false, null, null, [], Allowed: false), status with { Days = [] });
        await ExpectErrorAsync(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-c", "check-c"), carol.Token, HttpStatusCode.Forbidden, ApiErrorCodes.StatsSyncNotAllowed);
        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([1]), carol.Token, HttpStatusCode.Forbidden, ApiErrorCodes.StatsSyncNotAllowed);
        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/settings", new UploadStatsSettingsRequest(Convert.ToBase64String([1])), carol.Token, HttpStatusCode.Forbidden, ApiErrorCodes.StatsSyncNotAllowed);
        using (var missing = await RawAsync(http, HttpMethod.Get, $"{Base}/days/2026-11-11", null, carol.Token))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task Accounts_removed_from_the_list_are_turned_off_and_their_copy_deleted()
    {
        var http = _factory.CreateClient();
        var gone = await RegisterListedAsync(http, "sync-gone");
        var kept = await RegisterListedAsync(http, "sync-kept");
        foreach (var user in new[] { gone, kept })
        {
            await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt", "check"), user.Token);
            await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([1]), user.Token);
            await SendAsync<StatsSyncSettingsInfo>(http, HttpMethod.Put, $"{Base}/settings", new UploadStatsSettingsRequest(Convert.ToBase64String([2])), user.Token);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeskPalDbContext>();
            var options = Options.Create(new StatsSyncOptions { AllowedUserNames = "sync-days,sync-bob,sync-off,sync-size,sync-settings,sync-kept" });
            Assert.Equal(1, await new StatsSyncService(db, TimeProvider.System, options).DisableUnlistedAsync());
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "sync-gone");
            Assert.Equal((false, null, null, null), (user.StatsSyncEnabled, user.StatsSyncSalt, user.StatsSyncKeyCheck, user.StatsSyncSettings));
            Assert.False(await db.StatsSyncDays.AnyAsync(d => d.UserId == gone.UserId));
        }

        var keptStatus = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, kept.Token);
        Assert.True(keptStatus.Enabled);
        Assert.Single(keptStatus.Days);
        Assert.NotNull(keptStatus.SettingsRevision);
    }

    [Fact]
    public async Task Settings_are_kept_while_sync_is_on()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterListedAsync(http, "sync-settings");
        var settings = new UploadStatsSettingsRequest(Convert.ToBase64String([7, 8]));
        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/settings", settings, alice.Token, HttpStatusCode.Conflict, ApiErrorCodes.StatsSyncNotEnabled);

        await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-a", "check-a"), alice.Token);
        Assert.Null((await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token)).SettingsRevision);
        var day = await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([1]), alice.Token);
        var saved = await SendAsync<StatsSyncSettingsInfo>(http, HttpMethod.Put, $"{Base}/settings", settings, alice.Token);
        Assert.True(saved.Revision > day.Revision);
        Assert.Equal(saved.Revision, (await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token)).SettingsRevision);
        Assert.Equal(new StatsSyncSettingsDto(saved.Revision, settings.Data), await SendAsync<StatsSyncSettingsDto>(http, HttpMethod.Get, $"{Base}/settings", null, alice.Token));
        await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/settings", new UploadStatsSettingsRequest(Convert.ToBase64String(new byte[64 * 1024 + 1])), alice.Token, HttpStatusCode.BadRequest, ApiErrorCodes.StatsSyncInvalid);

        using (await RawAsync(http, HttpMethod.Delete, Base, null, alice.Token))
        {
        }

        using (var gone = await RawAsync(http, HttpMethod.Get, $"{Base}/settings", null, alice.Token))
        {
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }

        await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-b", "check-b"), alice.Token);
        Assert.Null((await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token)).SettingsRevision);
    }

    [Fact]
    public async Task Away_time_follows_the_account_and_rejects_other_values()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        Assert.Null(alice.IdleMinutes);
        Assert.Null((await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, alice.Token)).IdleMinutes);

        await ExpectErrorAsync(http, HttpMethod.Put, "/api/me/idle-minutes", new SetIdleMinutesRequest(7), alice.Token, HttpStatusCode.BadRequest, ApiErrorCodes.IdleMinutesInvalid);
        using (var saved = await RawAsync(http, HttpMethod.Put, "/api/me/idle-minutes", new SetIdleMinutesRequest(15), alice.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        Assert.Equal(15, (await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, alice.Token)).IdleMinutes);
        var again = await SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/login", new LoginRequest(alice.UserName, "password123"), null);
        Assert.Equal(15, again.IdleMinutes);
    }

    [Fact]
    public async Task Preferences_follow_the_account_and_change_only_the_fields_sent()
    {
        const string path = "/api/me/preferences";
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        Assert.Null(alice.Preferences);

        await ExpectErrorAsync(http, HttpMethod.Patch, path, new UserPreferences(SwitchDelaySeconds: 7), alice.Token, HttpStatusCode.BadRequest, ApiErrorCodes.PreferencesInvalid);
        await ExpectErrorAsync(http, HttpMethod.Patch, path, new UserPreferences(Theme: "pink"), alice.Token, HttpStatusCode.BadRequest, ApiErrorCodes.PreferencesInvalid);

        var room = await SendAsync<RoomDto>(http, HttpMethod.Post, "/api/rooms", new CreateRoomRequest("Room"), alice.Token);
        var other = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await SendAsync<UserPreferences>(http, HttpMethod.Patch, path, new UserPreferences(0, "dark", MemberOrder: new() { [room.Id] = [bob], [other] = [bob] }), alice.Token);
        var saved = await SendAsync<UserPreferences>(http, HttpMethod.Patch, path, new UserPreferences(Language: "en", Topmost: true), alice.Token);
        Assert.Equal((0, "dark", "en", (string?)null, true), (saved.SwitchDelaySeconds, saved.Theme, saved.Language, saved.CloseAction, saved.Topmost));
        Assert.Equal([room.Id], saved.MemberOrder!.Keys);

        var again = await SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/login", new LoginRequest(alice.UserName, "password123"), null);
        Assert.Equal(saved, again.Preferences! with { MemberOrder = saved.MemberOrder });
        Assert.Equal([bob], again.Preferences!.MemberOrder![room.Id]);

        var reset = await SendAsync<UserPreferences>(http, HttpMethod.Patch, path, new UserPreferences(MemberOrder: new() { [room.Id] = [] }), again.Token);
        Assert.Empty(reset.MemberOrder!);
    }

    private static UploadStatsDayRequest Upload(byte[] data) => new(Convert.ToBase64String(data));

    private static async Task<ApiError> ExpectErrorAsync(HttpClient http, HttpMethod method, string path, object? body, string token, HttpStatusCode status, string code)
    {
        using var response = await RawAsync(http, method, path, body, token);
        Assert.Equal(status, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>(DeskPalJson.Options))!;
        Assert.Equal(code, error.Code);
        return error;
    }

    private static Task<AuthResponse> RegisterAsync(HttpClient http, string name) =>
        SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/register", new RegisterRequest($"{name}{Guid.NewGuid():N}"[..20], name, "password123"), null);

    /// <param name="userName">One of the names in <see cref="ServerFactory"/>'s StatsSync:AllowedUserNames, each used by one test.</param>
    private static Task<AuthResponse> RegisterListedAsync(HttpClient http, string userName) =>
        SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/register", new RegisterRequest(userName, userName, "password123"), null);

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
            builder.UseSetting("StatsSync:AllowedUserNames", " Sync-Days, sync-bob,sync-off ,SYNC-SIZE,sync-settings,, sync-gone,sync-kept");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(_dbPath);
        }
    }
}