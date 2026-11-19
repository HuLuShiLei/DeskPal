using System.Net;
using System.Net.Http.Json;
using DeskPal.Server.Data;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DeskPal.Tests.Server;

public sealed class PersonalSyncTests : IClassFixture<PersonalSyncTests.ServerFactory>
{
    private readonly ServerFactory _factory;

    public PersonalSyncTests(ServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Sync_is_off_by_default_and_the_account_keeps_nothing()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        Assert.False(alice.PersonalSync);

        await ExpectErrorAsync(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "wechat", ActivityCategory.Work, null), alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await ExpectErrorAsync(http, HttpMethod.Delete, "/api/me/rules?kind=Process&pattern=wechat", null, alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await ExpectErrorAsync(http, HttpMethod.Put, "/api/me/no-title-rules", new UpsertNoTitleRuleRequest(NoTitleKind.App, "向日葵"), alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await ExpectErrorAsync(http, HttpMethod.Get, "/api/me/rules", null, alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await ExpectErrorAsync(http, HttpMethod.Get, "/api/me/no-title-rules", null, alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await ExpectErrorAsync(http, HttpMethod.Put, "/api/me/idle-minutes", new SetIdleMinutesRequest(15), alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await ExpectErrorAsync(http, HttpMethod.Patch, "/api/me/preferences", new UserPreferences(Theme: "dark"), alice.Token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);

        var me = await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, alice.Token);
        Assert.Equal((false, (int?)null, (UserPreferences?)null), (me.PersonalSync, me.IdleMinutes, me.Preferences));
    }

    [Fact]
    public async Task Turning_on_merges_this_computer_and_turning_off_deletes_the_server_copy()
    {
        var http = _factory.CreateClient();
        var alice = await RegisterAsync(http, "alice");
        var earlier = DateTimeOffset.UtcNow.AddHours(-1);
        var local = new PersonalDataDto(
            [new UserRuleDto(RuleKind.Process, "WeChat.exe", ActivityCategory.Communication, "微信", earlier)],
            [new NoTitleRuleDto(NoTitleKind.Process, "SunloginClient.exe", earlier), new NoTitleRuleDto(NoTitleKind.Domain, "https://", earlier)],
            new UserPreferences(Theme: "dark"),
            15);

        var merged = await SendAsync<PersonalDataDto>(http, HttpMethod.Post, "/api/me/personal-sync", local, alice.Token);
        Assert.Equal(("wechat", ActivityCategory.Communication), (merged.Rules.Single().Pattern, merged.Rules.Single().Category));
        Assert.Equal("sunloginclient", merged.NoTitleRules.Single().Pattern);
        Assert.Equal(("dark", 15), (merged.Preferences!.Theme, merged.IdleMinutes));

        var login = await SendAsync<AuthResponse>(http, HttpMethod.Post, "/api/auth/login", new LoginRequest(alice.UserName, "password123"), null);
        Assert.Equal((true, (int?)15, "dark"), (login.PersonalSync, login.IdleMinutes, login.Preferences!.Theme));
        var token = login.Token;

        await SendAsync<NoTitleRuleDto>(http, HttpMethod.Put, "/api/me/no-title-rules", new UpsertNoTitleRuleRequest(NoTitleKind.Process, "SUNLOGINCLIENT"), token);
        await SendAsync<NoTitleRuleDto>(http, HttpMethod.Put, "/api/me/no-title-rules", new UpsertNoTitleRuleRequest(NoTitleKind.Title, "识别码"), token);
        await ExpectErrorAsync(http, HttpMethod.Put, "/api/me/no-title-rules", new UpsertNoTitleRuleRequest(NoTitleKind.Title, " "), token, HttpStatusCode.BadRequest, ApiErrorCodes.NoTitleInvalid);
        Assert.Equal(2, (await SendAsync<List<NoTitleRuleDto>>(http, HttpMethod.Get, "/api/me/no-title-rules", null, token)).Count);

        using (var deleted = await RawAsync(http, HttpMethod.Delete, "/api/me/no-title-rules?kind=Title&pattern=识别码", null, token))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await SendAsync<UserRuleDto>(http, HttpMethod.Put, "/api/me/rules", new UpsertUserRuleRequest(RuleKind.Process, "wechat", ActivityCategory.Work, "微信"), token);
        var again = await SendAsync<PersonalDataDto>(http, HttpMethod.Post, "/api/me/personal-sync", local with { Preferences = new UserPreferences(Theme: "light"), IdleMinutes = 30 }, token);
        Assert.Equal(ActivityCategory.Work, again.Rules.Single().Category);
        Assert.Equal(("dark", 15), (again.Preferences!.Theme, again.IdleMinutes));

        using (var off = await RawAsync(http, HttpMethod.Delete, "/api/me/personal-sync", null, token))
        {
            Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        }

        var me = await SendAsync<AuthResponse>(http, HttpMethod.Get, "/api/me", null, token);
        Assert.Equal((false, (int?)null, (UserPreferences?)null), (me.PersonalSync, me.IdleMinutes, me.Preferences));
        await ExpectErrorAsync(http, HttpMethod.Get, "/api/me/no-title-rules", null, token, HttpStatusCode.Forbidden, ApiErrorCodes.PersonalSyncOff);
        await SendAsync<PersonalDataDto>(http, HttpMethod.Post, "/api/me/personal-sync", new PersonalDataDto([], []), token);
        Assert.Empty(await SendAsync<List<UserRuleDto>>(http, HttpMethod.Get, "/api/me/rules", null, token));
        Assert.Empty(await SendAsync<List<NoTitleRuleDto>>(http, HttpMethod.Get, "/api/me/no-title-rules", null, token));
    }

    [Fact]
    public void Migration_turns_sync_on_only_for_accounts_that_kept_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deskpal-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DeskPalDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            using var db = new DeskPalDbContext(options);
            var migrator = db.GetService<IMigrator>();
            migrator.Migrate("20261118062909_AddUserPreferences");

            Guid Insert(string name, string? preferences, int? idle)
            {
                var id = Guid.NewGuid();
                db.Database.ExecuteSql($"""
                    INSERT INTO "Users" ("Id", "CreatedAt", "DisplayName", "PasswordHash", "StatsSyncEnabled", "StatsSyncRevision", "UserName", "Preferences", "IdleMinutes")
                    VALUES ({id}, {DateTimeOffset.UtcNow}, {name}, 'x', 0, 0, {name}, {preferences}, {idle})
                    """);
                return id;
            }

            var empty = Insert("empty", null, null);
            var prefs = Insert("prefs", """{"theme":"dark"}""", null);
            var idle = Insert("idle", null, 15);
            var rules = Insert("rules", null, null);
            db.Database.ExecuteSql($"""
                INSERT INTO "UserRules" ("Id", "Category", "Kind", "Pattern", "UpdatedAt", "UserId")
                VALUES ({Guid.NewGuid()}, 1, 0, 'wechat', {DateTimeOffset.UtcNow}, {rules})
                """);

            migrator.Migrate();

            var on = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.PersonalSync);
            Assert.Equal((false, true, true, true), (on[empty], on[prefs], on[idle], on[rules]));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task ExpectErrorAsync(HttpClient http, HttpMethod method, string path, object? body, string token, HttpStatusCode status, string code)
    {
        using var response = await RawAsync(http, method, path, body, token);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>(DeskPalJson.Options))!.Code);
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
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            File.Delete(_dbPath);
        }
    }
}