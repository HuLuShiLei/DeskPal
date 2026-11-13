using System.Net;
using System.Net.Http.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
        var alice = await RegisterAsync(http, "alice");
        var bob = await RegisterAsync(http, "bob");

        var off = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token);
        Assert.False(off.Enabled);
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
        var alice = await RegisterAsync(http, "alice");
        await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-a", "check-a"), alice.Token);
        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload([2]), alice.Token);
        using (var disabled = await RawAsync(http, HttpMethod.Delete, Base, null, alice.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        }

        var afterOff = await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Get, Base, null, alice.Token);
        Assert.Equal(new StatsSyncStatusDto(false, null, null, []), afterOff with { Days = [] });
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
    public async Task Size_limits_reject_the_upload_with_the_limit()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        await SendAsync<StatsSyncStatusDto>(http, HttpMethod.Put, Base, new EnableStatsSyncRequest("salt-a", "check-a"), alice.Token);
        var big = new byte[700 * 1024];

        var tooLarge = await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/days/2026-11-11", Upload(new byte[1024 * 1024 + 1]), alice.Token, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.StatsSyncDayTooLarge);
        Assert.Equal("1", tooLarge.Args!["limit"]);

        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-01", Upload(big), alice.Token);
        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-02", Upload(big), alice.Token);
        var full = await ExpectErrorAsync(http, HttpMethod.Put, $"{Base}/days/2026-11-03", Upload(big), alice.Token, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.StatsSyncQuotaExceeded);
        Assert.Equal("2", full.Args!["limit"]);

        await SendAsync<StatsSyncDayInfo>(http, HttpMethod.Put, $"{Base}/days/2026-11-02", Upload(big), alice.Token);
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
            builder.UseSetting("StatsSync:MaxDayMegabytes", "1");
            builder.UseSetting("StatsSync:MaxTotalMegabytes", "2");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(_dbPath);
        }
    }
}