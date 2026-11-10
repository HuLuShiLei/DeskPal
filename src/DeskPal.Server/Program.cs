using System.Text.Json.Serialization;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Auth;
using DeskPal.Server.Features.Presence;
using DeskPal.Server.Features.Rooms;
using DeskPal.Server.Features.Rules;
using DeskPal.Shared.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = jwt.GetSigningKey();

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=storage/deskpal.db";
var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource));
if (!string.IsNullOrEmpty(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.Configure<RoomOptions>(builder.Configuration.GetSection("Rooms"));
builder.Services.AddDbContext<DeskPalDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddScoped<RoomService>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<PresenceStore>();
builder.Services.AddSingleton<RoomNotifier>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Issuer,
            IssuerSigningKey = signingKey,
            NameClaimType = "name",
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(HubContract.Path))
                {
                    ctx.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DeskPalDbContext>();
    await db.Database.MigrateAsync();
    var rulesPath = Path.Combine(AppContext.BaseDirectory, "Features", "Rules", "builtin-rules.json");
    await BuiltinRuleSeeder.SeedAsync(db, rulesPath, app.Logger);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapRoomEndpoints();
app.MapRuleEndpoints();
app.MapHub<PresenceHub>(HubContract.Path);

app.Run();

public partial class Program;