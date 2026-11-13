using System.Net;
using System.Net.Http.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DeskPal.Tests.Server;

public sealed class SignupInviteTests : IClassFixture<SignupInviteTests.ServerFactory>
{
    private const string AdminName = "inviteadmin";
    private readonly ServerFactory _factory;

    public SignupInviteTests(ServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Each_signup_invite_registers_one_account()
    {
        var (http, admin) = await SetUpAsync();

        await AssertErrorAsync(HttpStatusCode.BadRequest, ApiErrorCodes.SignupCodeRequired, http, HttpMethod.Post, "/api/auth/register", Register("nocode", null), null);
        await AssertErrorAsync(HttpStatusCode.BadRequest, ApiErrorCodes.SignupCodeInvalid, http, HttpMethod.Post, "/api/auth/register", Register("badcode", "ZZZZZZZZ"), null);

        var invite = await SendAsync<SignupInviteDto>(http, HttpMethod.Post, "/api/admin/signup-invites", new { }, admin.Token);
        Assert.Matches("^[A-Z2-9]{8}$", invite.Code);

        await AssertErrorAsync(HttpStatusCode.Conflict, ApiErrorCodes.UserNameTaken, http, HttpMethod.Post, "/api/auth/register", Register(AdminName, invite.Code, unique: false), null);
        Assert.Null(Assert.Single(await ListAsync(http, admin), i => i.Code == invite.Code).UsedAt);

        var carol = await SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/register", Register("carol", $"  {invite.Code.ToLowerInvariant()} "), null);
        Assert.False(carol.IsAdmin);
        await AssertErrorAsync(HttpStatusCode.BadRequest, ApiErrorCodes.SignupCodeInvalid, http, HttpMethod.Post, "/api/auth/register", Register("dave", invite.Code), null);

        var used = Assert.Single(await ListAsync(http, admin), i => i.Code == invite.Code);
        Assert.Equal((carol.UserName, "carol"), (used.UsedByUserName, used.UsedByDisplayName));
        Assert.NotNull(used.UsedAt);
        await AssertErrorAsync(HttpStatusCode.Conflict, ApiErrorCodes.SignupCodeUsed, http, HttpMethod.Delete, $"/api/admin/signup-invites/{invite.Code}", null, admin.Token);
    }

    [Fact]
    public async Task Revoked_invite_cannot_register_and_a_failed_attempt_keeps_the_user_name_free()
    {
        var (http, admin) = await SetUpAsync();
        var revoked = await SendAsync<SignupInviteDto>(http, HttpMethod.Post, "/api/admin/signup-invites", new { }, admin.Token);
        using (var response = await RawAsync(http, HttpMethod.Delete, $"/api/admin/signup-invites/{revoked.Code}", null, admin.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.DoesNotContain(await ListAsync(http, admin), i => i.Code == revoked.Code);
        var request = Register("erin", revoked.Code);
        await AssertErrorAsync(HttpStatusCode.BadRequest, ApiErrorCodes.SignupCodeInvalid, http, HttpMethod.Post, "/api/auth/register", request, null);
        await AssertErrorAsync(HttpStatusCode.NotFound, ApiErrorCodes.SignupCodeInvalid, http, HttpMethod.Delete, $"/api/admin/signup-invites/{revoked.Code}", null, admin.Token);

        var valid = await SendAsync<SignupInviteDto>(http, HttpMethod.Post, "/api/admin/signup-invites", new { }, admin.Token);
        var erin = await SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/register", request with { SignupCode = valid.Code }, null);
        Assert.Equal(request.UserName, erin.UserName);
    }

    [Fact]
    public async Task Same_invite_used_twice_at_once_registers_one_account()
    {
        var (http, admin) = await SetUpAsync();
        var invite = await SendAsync<SignupInviteDto>(http, HttpMethod.Post, "/api/admin/signup-invites", new { }, admin.Token);

        var responses = await Task.WhenAll(
            RawAsync(http, HttpMethod.Post, "/api/auth/register", Register("race1", invite.Code), null),
            RawAsync(http, HttpMethod.Post, "/api/auth/register", Register("race2", invite.Code), null));

        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Only_admins_manage_signup_invites()
    {
        var (http, admin) = await SetUpAsync();
        Assert.True(admin.IsAdmin);
        Assert.True((await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, admin.Token)).IsAdmin);

        var open = OpenRegistration().CreateClient();
        var member = await SendAsync<AuthResponse>(open, HttpMethod.Post, "/api/auth/register", Register("member", null), null);
        Assert.False(member.IsAdmin);
        Assert.False((await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, member.Token)).IsAdmin);

        await AssertErrorAsync(HttpStatusCode.Forbidden, ApiErrorCodes.NotAdmin, http, HttpMethod.Get, "/api/admin/signup-invites", null, member.Token);
        await AssertErrorAsync(HttpStatusCode.Forbidden, ApiErrorCodes.NotAdmin, http, HttpMethod.Post, "/api/admin/signup-invites", new { }, member.Token);
        using var anonymous = await RawAsync(http, HttpMethod.Get, "/api/admin/signup-invites", null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    /// <summary>The admin registers while invites are not required yet, as on the production server before the switch is turned on.</summary>
    private async Task<(HttpClient Http, AuthResponse Admin)> SetUpAsync()
    {
        var open = OpenRegistration().CreateClient();
        var http = _factory.CreateClient();
        using var login = await RawAsync(http, HttpMethod.Post, "/api/auth/login", new LoginRequest(AdminName, "password123"), null);
        var admin = login.IsSuccessStatusCode
            ? (await login.Content.ReadFromJsonAsync<AuthResponse>(DeskPalJson.Options))!
            : await SendAsync<AuthResponse>(open, HttpMethod.Post, "/api/auth/register", Register(AdminName, null, unique: false), null);
        return (http, admin);
    }

    private WebApplicationFactory<Program> OpenRegistration() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Auth:RequireSignupInvite", "false"));

    private static RegisterRequest Register(string name, string? code, bool unique = true) =>
        new(unique ? $"{name}{Guid.NewGuid():N}"[..20] : name, name, "password123", code);

    private static Task<List<SignupInviteDto>> ListAsync(HttpClient http, AuthResponse admin) =>
        SendAsync<List<SignupInviteDto>>(http, HttpMethod.Get, "/api/admin/signup-invites", null, admin.Token);

    private static async Task AssertErrorAsync(HttpStatusCode status, string code, HttpClient http, HttpMethod method, string path, object? body, string? token)
    {
        using var response = await RawAsync(http, method, path, body, token);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>(DeskPalJson.Options))!.Code);
    }

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
            builder.UseSetting("Auth:RequireSignupInvite", "true");
            builder.UseSetting("Auth:AdminUserNames:0", AdminName);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(_dbPath);
        }
    }
}