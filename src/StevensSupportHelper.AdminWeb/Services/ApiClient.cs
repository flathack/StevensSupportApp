using System.Net.Http.Json;
using System.Text.Json;

namespace StevensSupportHelper.AdminWeb.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private string? _accessToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = configuration["Api:BaseUrl"] ?? "http://localhost:5000";
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int>("Api:TimeoutSeconds", 30));
    }

    public void SetAccessToken(string? token)
    {
        _accessToken = token;
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public string? AccessToken => _accessToken;

    public async Task<LoginResponse?> LoginAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    public async Task<UserInfoResponse?> GetCurrentUserAsync()
    {
        var response = await _httpClient.GetAsync("/api/auth/me");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<UserInfoResponse>(JsonOptions);
    }

    public async Task<List<UserInfoResponse>?> GetUsersAsync()
    {
        var response = await _httpClient.GetAsync("/api/auth/users");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<UserInfoResponse>>(JsonOptions);
    }

    public async Task<ApiMessageResponse?> CreateUserAsync(CreateUserRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/register", request);
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        var payload = await response.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions);
        return new ApiMessageResponse(true, payload?.Message ?? "Benutzer wurde erstellt.");
    }

    public async Task<ApiMessageResponse?> UpdateUserRolesAsync(Guid userId, IReadOnlyList<string> roles)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/auth/users/{userId}/roles", new { Roles = roles });
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions) ?? new ApiMessageResponse(true, "Rollen wurden aktualisiert.");
    }

    public async Task<ApiMessageResponse?> ResetUserPasswordAsync(Guid userId, string newPassword)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/auth/users/{userId}/reset-password", new { NewPassword = newPassword });
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions) ?? new ApiMessageResponse(true, "Passwort wurde zurückgesetzt.");
    }

    public async Task<ApiMessageResponse?> DeleteUserAsync(Guid userId)
    {
        var response = await _httpClient.DeleteAsync($"/api/auth/users/{userId}");
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions) ?? new ApiMessageResponse(true, "Benutzer wurde gelöscht.");
    }

    public async Task<ApiMessageResponse?> ChangeOwnPasswordAsync(string oldPassword, string newPassword)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/change-password", new { OldPassword = oldPassword, NewPassword = newPassword });
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions) ?? new ApiMessageResponse(true, "Passwort wurde geändert.");
    }

    public async Task<List<ClientSummaryResponse>?> GetClientsAsync()
    {
        var response = await _httpClient.GetAsync("/api/admin/clients");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<ClientSummaryResponse>>(JsonOptions);
    }

    public async Task<ClientDetailResponse?> GetClientAsync(Guid clientId)
    {
        var response = await _httpClient.GetAsync($"/api/admin/clients/{clientId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ClientDetailResponse>(JsonOptions);
    }

    public async Task<SupportRequestResponse?> CreateSupportRequestAsync(Guid clientId, string adminDisplayName, string preferredChannel, string reason)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/admin/clients/{clientId}/support-requests", new
        {
            AdminDisplayName = adminDisplayName,
            PreferredChannel = preferredChannel,
            Reason = reason
        });
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<SupportRequestResponse>(JsonOptions);
    }

    public async Task<EndSessionResponse?> EndSessionAsync(Guid clientId)
    {
        var response = await _httpClient.PostAsync($"/api/admin/clients/{clientId}/active-session/end", null);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<EndSessionResponse>(JsonOptions);
    }

    public async Task<List<AuditEntryResponse>?> GetAuditEntriesAsync(int limit = 100)
    {
        var response = await _httpClient.GetAsync($"/api/admin/audit?take={limit}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<AuditEntryResponse>>(JsonOptions);
    }

    public async Task<List<RemoteActionResponse>?> GetRemoteActionsAsync()
    {
        var response = await _httpClient.GetAsync("/api/admin/remote-actions");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<RemoteActionResponse>>(JsonOptions);
    }

    public async Task<ScriptResultResponse?> ExecuteRemoteActionAsync(Guid clientId, string scriptName)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/admin/clients/{clientId}/agent-jobs/script-execution", new
        {
            ScriptName = scriptName
        });
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ScriptResultResponse>(JsonOptions);
    }

    private static async Task<ApiMessageResponse?> ReadErrorResponseAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions);
            if (error is not null)
            {
                return error;
            }
        }
        catch
        {
        }

        return new ApiMessageResponse(false, $"Anfrage fehlgeschlagen ({(int)response.StatusCode}).");
    }
}

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserInfoResponse User);

public sealed record UserInfoResponse(
    Guid Id,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool IsMfaEnabled,
    DateTime? LastLoginAtUtc);

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string DisplayName,
    List<string> Roles);

public sealed record RegisterResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string Message);

public sealed record ClientSummaryResponse(
    Guid ClientId,
    string DeviceName,
    string MachineName,
    string CurrentUser,
    bool IsOnline,
    string AgentVersion,
    DateTimeOffset LastSeenAtUtc,
    string Notes);

public sealed record SupportRequestResponse(
    Guid RequestId,
    string Status,
    string Message);

public sealed record EndSessionResponse(
    Guid SessionId,
    string Status,
    string Message);

public sealed record ClientDetailResponse(
    Guid ClientId,
    string DeviceName,
    string MachineName,
    string CurrentUser,
    bool HasInteractiveUser,
    bool IsAtLogonScreen,
    string AgentVersion,
    string? OsDescription,
    DateTimeOffset? LastBootAtUtc,
    bool IsOnline,
    bool ConsentRequired,
    bool TailscaleConnected,
    IReadOnlyList<string> TailscaleIpAddresses,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string? Notes,
    string? RustDeskId,
    SupportRequestResponse? PendingSupportRequest,
    SupportSessionResponse? ActiveSession);

public sealed record SupportSessionResponse(
    Guid SessionId,
    string AdminDisplayName,
    string Channel,
    DateTimeOffset StartedAtUtc);

public sealed record AuditEntryResponse(
    Guid AuditEntryId,
    string EventType,
    Guid? ClientId,
    string DeviceName,
    string Actor,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record RemoteActionResponse(
    string Name,
    string Description,
    bool RequiresElevation);

public sealed record ScriptResultResponse(
    Guid JobId,
    string Status,
    string Message);

public sealed record ApiMessageResponse(
    bool Success,
    string Message);
