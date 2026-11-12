using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Services;

public sealed class ApiException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class ApiClient(AppSettings settings)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public Task<AuthResponse> RegisterAsync(RegisterRequest req) => SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/register", req, auth: false);

    public Task<AuthResponse> LoginAsync(LoginRequest req) => SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/login", req, auth: false);

    public Task<AuthResponse> MeAsync() => SendAsync<AuthResponse>(HttpMethod.Get, "/api/me");

    public Task<List<RoomDto>> GetRoomsAsync() => SendAsync<List<RoomDto>>(HttpMethod.Get, "/api/rooms");

    public Task<RoomDto> CreateRoomAsync(string name) => SendAsync<RoomDto>(HttpMethod.Post, "/api/rooms", new CreateRoomRequest(name));

    public Task<RoomDto> RenameRoomAsync(Guid roomId, string name) =>
        SendAsync<RoomDto>(HttpMethod.Put, $"/api/rooms/{roomId}", new RenameRoomRequest(name));

    public Task<InviteDto> CreateInviteAsync(Guid roomId) => SendAsync<InviteDto>(HttpMethod.Post, $"/api/rooms/{roomId}/invites", new { });

    public Task<RoomDto> JoinRoomAsync(string code) => SendAsync<RoomDto>(HttpMethod.Post, "/api/rooms/join", new JoinRoomRequest(code));

    public Task LeaveRoomAsync(Guid roomId) => SendAsync<object>(HttpMethod.Post, $"/api/rooms/{roomId}/leave", new { });

    public Task KickMemberAsync(Guid roomId, Guid userId) =>
        SendAsync<object>(HttpMethod.Post, $"/api/rooms/{roomId}/kick", new KickMemberRequest(userId));

    public Task<RoomDto> TransferOwnerAsync(Guid roomId, Guid newOwner) =>
        SendAsync<RoomDto>(HttpMethod.Post, $"/api/rooms/{roomId}/transfer", new TransferOwnerRequest(newOwner));

    public Task DissolveRoomAsync(Guid roomId) => SendAsync<object>(HttpMethod.Delete, $"/api/rooms/{roomId}");

    public Task<PrivacyProfileDto> GetPrivacyAsync(Guid roomId) => SendAsync<PrivacyProfileDto>(HttpMethod.Get, $"/api/rooms/{roomId}/privacy");

    public Task<PrivacyProfileDto> SetPrivacyAsync(Guid roomId, PrivacyProfileDto dto) =>
        SendAsync<PrivacyProfileDto>(HttpMethod.Put, $"/api/rooms/{roomId}/privacy", dto);

    public Task<BuiltinRulesResponse> GetBuiltinRulesAsync(int knownVersion) =>
        SendAsync<BuiltinRulesResponse>(HttpMethod.Get, $"/api/rules/builtin?version={knownVersion}");

    public Task<List<UserRuleDto>> GetMyRulesAsync() => SendAsync<List<UserRuleDto>>(HttpMethod.Get, "/api/me/rules");

    public Task<UserRuleDto> UpsertMyRuleAsync(UpsertUserRuleRequest req) => SendAsync<UserRuleDto>(HttpMethod.Put, "/api/me/rules", req);

    public Task DeleteMyRuleAsync(RuleKind kind, string pattern) =>
        SendAsync<object?>(HttpMethod.Delete, $"/api/me/rules?kind={kind}&pattern={Uri.EscapeDataString(pattern)}");

    public Task SetSharingAsync(bool enabled) => SendAsync<object>(HttpMethod.Put, "/api/me/sharing", new SetSharingRequest(enabled));

    public Task ReportUsageAsync(UsageReportRequest req) => SendAsync<object>(HttpMethod.Post, "/api/me/usage", req);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool auth = true)
    {
        using var request = new HttpRequestMessage(method, settings.ServerUrl.TrimEnd('/') + path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: DeskPalJson.Options);
        }

        if (auth && settings.GetToken() is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException($"无法连接服务器 {settings.ServerUrl}：{ex.Message}", 0);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                ApiError? error = null;
                try
                {
                    error = await response.Content.ReadFromJsonAsync<ApiError>(DeskPalJson.Options);
                }
                catch
                {
                }

                throw new ApiException(error?.Error ?? $"请求失败（HTTP {(int)response.StatusCode}）", (int)response.StatusCode);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return default!;
            }

            return (await response.Content.ReadFromJsonAsync<T>(DeskPalJson.Options))!;
        }
    }
}
