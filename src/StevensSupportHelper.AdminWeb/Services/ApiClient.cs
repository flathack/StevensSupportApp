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
        var response = await _httpClient.GetAsync($"/api/admin/audit-entries?limit={limit}");
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