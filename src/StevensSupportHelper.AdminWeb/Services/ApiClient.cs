using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.AdminWeb.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private string? _accessToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
        _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    public string? AccessToken => _accessToken;

    public async Task<LoginResponse?> LoginAsync(string username, string password)
        => await PostAsJsonAsync<LoginResponse>("/api/auth/login", new { Username = username, Password = password });

    public async Task<UserInfoResponse?> GetCurrentUserAsync()
        => await GetAsync<UserInfoResponse>("/api/auth/me");

    public async Task<AdminSessionInfoResponse?> GetAdminSessionAsync()
        => await GetAsync<AdminSessionInfoResponse>("/api/admin/session");

    public async Task<List<UserInfoResponse>?> GetUsersAsync()
        => await GetAsync<List<UserInfoResponse>>("/api/auth/users");

    public async Task<HardcodedSuperAdminStateResponse?> GetHardcodedSuperAdminStateAsync()
        => await GetAsync<HardcodedSuperAdminStateResponse>("/api/auth/hardcoded-super-admin");

    public async Task<ApiMessageResponse?> UpdateHardcodedSuperAdminStateAsync(bool enabled)
        => await PostForMessageAsync(
            "/api/auth/hardcoded-super-admin",
            new { Enabled = enabled },
            enabled
                ? "Der fest eingebaute Super-Administrator wurde aktiviert."
                : "Der fest eingebaute Super-Administrator wurde deaktiviert.");

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
        => await PutForMessageAsync($"/api/auth/users/{userId}/roles", new { Roles = roles }, "Rollen wurden aktualisiert.");

    public async Task<ApiMessageResponse?> ResetUserPasswordAsync(Guid userId, string newPassword)
        => await PostForMessageAsync($"/api/auth/users/{userId}/reset-password", new { NewPassword = newPassword }, "Passwort wurde zurückgesetzt.");

    public async Task<ApiMessageResponse?> DeleteUserAsync(Guid userId)
    {
        var response = await _httpClient.DeleteAsync($"/api/auth/users/{userId}");
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions)
            ?? new ApiMessageResponse(true, "Benutzer wurde gelöscht.");
    }

    public async Task<ApiMessageResponse?> ChangeOwnPasswordAsync(string oldPassword, string newPassword)
        => await PostForMessageAsync("/api/auth/change-password", new { OldPassword = oldPassword, NewPassword = newPassword }, "Passwort wurde geändert.");

    public async Task<List<ClientSummaryResponse>?> GetClientsAsync()
        => await GetAsync<List<ClientSummaryResponse>>("/api/admin/clients");

    public async Task<ClientDetailResponse?> GetClientAsync(Guid clientId)
        => await GetAsync<ClientDetailResponse>($"/api/admin/clients/{clientId}");

    public async Task<SupportRequestResponse?> CreateSupportRequestAsync(Guid clientId, string adminDisplayName, string preferredChannel, string reason)
    {
        if (!Enum.TryParse<RemoteChannel>(preferredChannel, ignoreCase: true, out var channel))
        {
            channel = RemoteChannel.RustDesk;
        }

        return await PostAsJsonAsync<SupportRequestResponse>(
            $"/api/admin/clients/{clientId}/support-requests",
            new CreateSupportRequestRequest(adminDisplayName, channel, reason));
    }

    public async Task<EndSessionResponse?> EndSessionAsync(Guid clientId)
        => await PostAsync<EndSessionResponse>($"/api/admin/clients/{clientId}/active-session/end");

    public async Task<List<AuditEntryResponse>?> GetAuditEntriesAsync(int limit = 100)
        => await GetAsync<List<AuditEntryResponse>>($"/api/admin/audit?take={limit}");

    public async Task<List<RemoteActionResponse>?> GetRemoteActionsAsync()
        => await GetAsync<List<RemoteActionResponse>>("/api/admin/remote-actions");

    public async Task<List<ChatMessageDto>?> GetChatMessagesAsync(Guid clientId)
        => await GetAsync<List<ChatMessageDto>>($"/api/admin/clients/{clientId}/chat-messages");

    public async Task<ChatMessageDto?> SendChatMessageAsync(Guid clientId, string message)
        => await PostAsJsonAsync<ChatMessageDto>($"/api/admin/clients/{clientId}/chat-messages", new SendAdminChatMessageRequest(message));

    public async Task<ApiMessageResponse?> UpdateClientMetadataAsync(Guid clientId, string notes, string rustDeskId, string rustDeskPassword, string remoteUserName, string remotePassword)
        => await PutForMessageAsync(
            $"/api/admin/clients/{clientId}/metadata",
            new UpdateAdminClientMetadataRequest(
                string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                string.IsNullOrWhiteSpace(rustDeskId) ? null : rustDeskId.Trim(),
                string.IsNullOrWhiteSpace(rustDeskPassword) ? null : rustDeskPassword.Trim(),
                string.IsNullOrWhiteSpace(remoteUserName) ? null : remoteUserName.Trim(),
                string.IsNullOrWhiteSpace(remotePassword) ? null : remotePassword.Trim()),
            "Client-Metadaten wurden gespeichert.");

    public async Task<QueueFileTransferResponse?> QueueFileUploadAsync(Guid clientId, string fileName, string targetRelativePath, string contentBase64)
        => await PostAsJsonAsync<QueueFileTransferResponse>(
            $"/api/admin/clients/{clientId}/file-transfers/upload",
            new QueueFileUploadRequest(fileName, targetRelativePath, contentBase64));

    public async Task<QueueFileTransferResponse?> QueueFileDownloadAsync(Guid clientId, string sourceRelativePath)
        => await PostAsJsonAsync<QueueFileTransferResponse>(
            $"/api/admin/clients/{clientId}/file-transfers/download",
            new QueueFileDownloadRequest(sourceRelativePath));

    public async Task<FileTransferDto?> GetFileTransferAsync(Guid transferId)
        => await GetAsync<FileTransferDto>($"/api/admin/file-transfers/{transferId}");

    public async Task<FileTransferContentResponse?> GetFileTransferContentAsync(Guid transferId)
        => await GetAsync<FileTransferContentResponse>($"/api/admin/file-transfers/{transferId}/content");

    public async Task<QueueAgentJobResponse?> QueueProcessSnapshotAsync(Guid clientId)
        => await PostAsync<QueueAgentJobResponse>($"/api/admin/clients/{clientId}/agent-jobs/process-snapshot");

    public async Task<QueueAgentJobResponse?> QueueWindowsUpdateScanAsync(Guid clientId)
        => await PostAsync<QueueAgentJobResponse>($"/api/admin/clients/{clientId}/agent-jobs/windows-update-scan");

    public async Task<QueueAgentJobResponse?> QueueWindowsUpdateInstallAsync(Guid clientId)
        => await PostAsync<QueueAgentJobResponse>($"/api/admin/clients/{clientId}/agent-jobs/windows-update-install");

    public async Task<QueueAgentJobResponse?> QueueRegistrySnapshotAsync(Guid clientId, string registryPath)
        => await PostAsJsonAsync<QueueAgentJobResponse>(
            $"/api/admin/clients/{clientId}/agent-jobs/registry-snapshot",
            new AgentRegistrySnapshotRequest(registryPath));

    public async Task<QueueAgentJobResponse?> QueueServiceSnapshotAsync(Guid clientId)
        => await PostAsync<QueueAgentJobResponse>($"/api/admin/clients/{clientId}/agent-jobs/service-snapshot");

    public async Task<QueueAgentJobResponse?> QueueServiceControlAsync(Guid clientId, string serviceName, string action)
        => await PostAsJsonAsync<QueueAgentJobResponse>(
            $"/api/admin/clients/{clientId}/agent-jobs/service-control",
            new AgentServiceControlRequest(serviceName, action));

    public async Task<QueueAgentJobResponse?> QueueScriptExecutionAsync(Guid clientId, string scriptContent)
        => await PostAsJsonAsync<QueueAgentJobResponse>(
            $"/api/admin/clients/{clientId}/agent-jobs/script-execution",
            new { ScriptContent = scriptContent });

    public async Task<QueueAgentJobResponse?> ExecuteRemoteActionAsync(Guid clientId, string actionName)
        => await PostAsJsonAsync<QueueAgentJobResponse>(
            $"/api/admin/clients/{clientId}/agent-jobs/remote-action",
            new { Name = actionName });

    public async Task<QueueAgentJobResponse?> QueuePowerPlanSnapshotAsync(Guid clientId)
        => await PostAsync<QueueAgentJobResponse>($"/api/admin/clients/{clientId}/agent-jobs/power-plan-snapshot");

    public async Task<QueueAgentJobResponse?> QueuePowerPlanActivateAsync(Guid clientId, string powerPlanGuid)
        => await PostAsJsonAsync<QueueAgentJobResponse>(
            $"/api/admin/clients/{clientId}/agent-jobs/power-plan-activate",
            new AgentPowerPlanActivateRequest(powerPlanGuid));

    public async Task<AgentJobDto?> GetAgentJobAsync(Guid jobId)
        => await GetAsync<AgentJobDto>($"/api/admin/agent-jobs/{jobId}");

    public async Task<DeploymentSnapshotResponse?> GetDeploymentSnapshotAsync()
        => await GetAsync<DeploymentSnapshotResponse>("/api/admin/deployment");

    public async Task<ApiResult<DeploymentSettingsResponse>?> SaveDeploymentSettingsAsync(DeploymentSettingsRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/admin/deployment/settings", request);
        return await ReadApiResultAsync<DeploymentSettingsEnvelope, DeploymentSettingsResponse>(response, static payload => payload.Settings);
    }

    public async Task<ApiResult<DeploymentAssetResponse>?> UploadDeploymentAssetAsync(string assetKind, Stream contentStream, string fileName, string contentType)
    {
        using var multipartContent = new MultipartFormDataContent();
        using var streamContent = new StreamContent(contentStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        multipartContent.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync($"/api/admin/deployment/assets/{assetKind}", multipartContent);
        return await ReadApiResultAsync<DeploymentAssetEnvelope, DeploymentAssetResponse>(response, static payload => payload.Asset);
    }

    public async Task<ApiResult<DeploymentProfileResponse>?> SaveDeploymentProfileAsync(DeploymentProfileRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/admin/deployment/profiles", request);
        return await ReadApiResultAsync<DeploymentProfileEnvelope, DeploymentProfileResponse>(response, static payload => payload.Profile);
    }

    public async Task<ApiMessageResponse?> DeleteDeploymentProfileAsync(Guid profileId)
    {
        var response = await _httpClient.DeleteAsync($"/api/admin/deployment/profiles/{profileId}");
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions)
            ?? new ApiMessageResponse(true, "Kundenprofil wurde gelöscht.");
    }

    public async Task<DeploymentConfigResponse?> GetDeploymentProfileConfigAsync(Guid profileId)
        => await GetAsync<DeploymentConfigResponse>($"/api/admin/deployment/profiles/{profileId}/config");

    public async Task<PackageDownloadResponse?> DownloadDeploymentPackageAsync(Guid profileId)
    {
        var response = await _httpClient.GetAsync($"/api/admin/deployment/profiles/{profileId}/export");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "StevensSupportHelper-Paket.zip";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/zip";
        var content = await response.Content.ReadAsByteArrayAsync();
        return new PackageDownloadResponse(fileName, contentType, content);
    }

    private async Task<T?> GetAsync<T>(string requestUri)
        where T : class
    {
        var response = await _httpClient.GetAsync(requestUri);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private async Task<T?> PostAsync<T>(string requestUri)
        where T : class
    {
        var response = await _httpClient.PostAsync(requestUri, null);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private async Task<T?> PostAsJsonAsync<T>(string requestUri, object payload)
        where T : class
    {
        var response = await _httpClient.PostAsJsonAsync(requestUri, payload);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private async Task<ApiMessageResponse?> PostForMessageAsync(string requestUri, object payload, string fallbackMessage)
    {
        var response = await _httpClient.PostAsJsonAsync(requestUri, payload);
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions)
            ?? new ApiMessageResponse(true, fallbackMessage);
    }

    private async Task<ApiMessageResponse?> PutForMessageAsync(string requestUri, object payload, string fallbackMessage)
    {
        var response = await _httpClient.PutAsJsonAsync(requestUri, payload);
        if (!response.IsSuccessStatusCode)
        {
            return await ReadErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(JsonOptions)
            ?? new ApiMessageResponse(true, fallbackMessage);
    }

    private static async Task<ApiResult<TData>?> ReadApiResultAsync<TEnvelope, TData>(
        HttpResponseMessage response,
        Func<TEnvelope, TData?> dataSelector)
        where TEnvelope : class
        where TData : class
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorResponseAsync(response);
            return error is null ? null : new ApiResult<TData>(error.Success, error.Message, null);
        }

        var payload = await response.Content.ReadFromJsonAsync<TEnvelope>(JsonOptions);
        if (payload is null)
        {
            return new ApiResult<TData>(false, "Die Serverantwort konnte nicht gelesen werden.", null);
        }

        var message = payload is IApiMessageEnvelope envelope ? envelope.Message : "Operation erfolgreich.";
        return new ApiResult<TData>(true, message, dataSelector(payload));
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

public sealed record HardcodedSuperAdminStateResponse(
    string Username,
    string DisplayName,
    bool Enabled);

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
    bool AutoApproveSupportRequests,
    bool TailscaleConnected,
    IReadOnlyList<string> TailscaleIpAddresses,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string? Notes,
    string? RustDeskId,
    string? RustDeskPassword = null,
    string? RemoteUserName = null,
    string? RemotePassword = null,
    IReadOnlyList<RemoteChannel>? SupportedChannels = null,
    SupportRequestResponse? PendingSupportRequest = null,
    SupportSessionResponse? ActiveSession = null,
    int UnreadClientChatCount = 0,
    DateTimeOffset? LastClientChatMessageAtUtc = null);

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

public sealed record ApiMessageResponse(
    bool Success,
    string Message);

public sealed record ApiResult<T>(
    bool Success,
    string Message,
    T? Data)
    where T : class;

public interface IApiMessageEnvelope
{
    string Message { get; }
}

public sealed record DeploymentSnapshotResponse(
    DeploymentSettingsResponse Settings,
    IReadOnlyList<DeploymentAssetResponse> Assets,
    IReadOnlyList<DeploymentProfileResponse> Profiles);

public sealed record DeploymentSettingsRequest(
    string ServerUrl,
    string ApiKey,
    string ServerProjectPath,
    string RustDeskPath,
    string RustDeskPassword,
    string ClientInstallerPath,
    string RemoteActionsPath,
    string PackageGeneratorPath,
    string RemoteUserName,
    string RemotePassword,
    string PreferredChannel,
    string Reason,
    string DefaultRegistrationSharedKey,
    string DefaultInstallRoot,
    string DefaultServiceName);

public sealed record DeploymentSettingsResponse(
    string ServerUrl,
    string ApiKey,
    string ServerProjectPath,
    string RustDeskPath,
    string RustDeskPassword,
    string ClientInstallerPath,
    string RemoteActionsPath,
    string PackageGeneratorPath,
    string RemoteUserName,
    string RemotePassword,
    string PreferredChannel,
    string Reason,
    string DefaultRegistrationSharedKey,
    string DefaultInstallRoot,
    string DefaultServiceName);

public sealed record DeploymentAssetResponse(
    Guid Id,
    string Kind,
    string OriginalFileName,
    string StoredFileName,
    string ContentType,
    long FileSizeBytes,
    string Sha256,
    DateTimeOffset UploadedAtUtc);

public sealed record DeploymentProfileRequest(
    Guid Id,
    string CustomerName,
    string DeviceName,
    string Notes,
    string ServerUrl,
    string RegistrationSharedKey,
    string InstallRoot,
    string ServiceName,
    bool InstallRustDesk,
    bool InstallTailscale,
    string TailscaleAuthKey,
    bool EnableAutoApprove,
    bool EnableRdp,
    bool CreateServiceUser,
    bool ServiceUserIsAdministrator,
    string ServiceUserName,
    string ServiceUserPassword,
    string RustDeskId,
    string RustDeskPassword,
    List<string> TailscaleIpAddresses,
    bool Silent);

public sealed record DeploymentProfileResponse(
    Guid Id,
    string CustomerName,
    string DeviceName,
    string Notes,
    string ServerUrl,
    string RegistrationSharedKey,
    string InstallRoot,
    string ServiceName,
    bool InstallRustDesk,
    bool InstallTailscale,
    string TailscaleAuthKey,
    bool EnableAutoApprove,
    bool EnableRdp,
    bool CreateServiceUser,
    bool ServiceUserIsAdministrator,
    string ServiceUserName,
    string ServiceUserPassword,
    string RustDeskId,
    string RustDeskPassword,
    IReadOnlyList<string> TailscaleIpAddresses,
    bool Silent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DeploymentConfigResponse(
    Guid ProfileId,
    string ConfigText);

public sealed record PackageDownloadResponse(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record DeploymentSettingsEnvelope(
    bool Success,
    string Message,
    DeploymentSettingsResponse Settings) : IApiMessageEnvelope;

public sealed record DeploymentAssetEnvelope(
    bool Success,
    string Message,
    DeploymentAssetResponse Asset) : IApiMessageEnvelope;

public sealed record DeploymentProfileEnvelope(
    bool Success,
    string Message,
    DeploymentProfileResponse Profile) : IApiMessageEnvelope;
