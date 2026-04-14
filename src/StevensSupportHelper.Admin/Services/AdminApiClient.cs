using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin.Services;

public sealed class AdminApiClient
{
    private const string ApiKeyHeaderName = "X-Admin-ApiKey";
    private const string MfaHeaderName = "X-Admin-Totp";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private HttpClient? _cachedClient;
    private string? _cachedServerBaseUrl;
    private string? _cachedApiKey;
    private string? _cachedMfaCode;

    public async Task<AdminSessionInfoResponse> GetSessionInfoAsync(string serverBaseUrl, string apiKey, string? mfaCode, CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync("/api/admin/session", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminSessionInfoResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no admin session payload.");
    }

    public async Task<IReadOnlyList<AdminClientSummary>> GetClientsAsync(string serverBaseUrl, string apiKey, string? mfaCode, CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync("/api/admin/clients", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<AdminClientSummary>>(JsonOptions, cancellationToken))
            ?? [];
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        int take,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync($"/api/admin/audit?take={Math.Max(1, take)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntryDto>>(JsonOptions, cancellationToken))
            ?? [];
    }

    public async Task<QueueFileTransferResponse> QueueUploadTransferAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        QueueFileUploadRequest request,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/file-transfers/upload",
            request,
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueFileTransferResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no upload transfer response.");
    }

    public async Task<QueueFileTransferResponse> QueueDownloadTransferAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        QueueFileDownloadRequest request,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/file-transfers/download",
            request,
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueFileTransferResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no download transfer response.");
    }

    public async Task<FileTransferDto> GetFileTransferAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync($"/api/admin/file-transfers/{transferId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<FileTransferDto>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no file transfer payload.");
    }

    public async Task<FileTransferContentResponse> GetFileTransferContentAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync($"/api/admin/file-transfers/{transferId}/content", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<FileTransferContentResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no file transfer content.");
    }

    public async Task<QueueAgentJobResponse> QueueProcessSnapshotJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/process-snapshot",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no agent job queue response.");
    }

    public async Task<QueueAgentJobResponse> QueueWindowsUpdateScanJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/windows-update-scan",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no windows update scan queue response.");
    }

    public async Task<QueueAgentJobResponse> QueueWindowsUpdateInstallJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/windows-update-install",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no windows update install queue response.");
    }

    public async Task<QueueAgentJobResponse> QueueRegistrySnapshotJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        string registryPath,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/registry-snapshot",
            new AgentRegistrySnapshotRequest(registryPath),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no registry snapshot queue response.");
    }

    public async Task<QueueAgentJobResponse> QueueServiceSnapshotJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/service-snapshot",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no service snapshot queue response.");
    }

    public async Task<QueueAgentJobResponse> QueueServiceControlJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        string serviceName,
        string action,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/service-control",
            new AgentServiceControlRequest(serviceName, action),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no service control queue response.");
    }

    public async Task<QueueAgentJobResponse> QueueScriptExecutionJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        string scriptContent,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/script-execution",
            new AgentScriptExecutionRequest(scriptContent, string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, []),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no script execution queue response.");
    }

    public async Task<QueueAgentJobResponse> QueuePowerPlanSnapshotJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/power-plan-snapshot",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no power plan snapshot queue response.");
    }

    public async Task<QueueAgentJobResponse> QueuePowerPlanActivateJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        string planGuid,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/agent-jobs/power-plan-activate",
            new AgentPowerPlanActivateRequest(planGuid),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<QueueAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no power plan activation queue response.");
    }

    public async Task<AgentJobDto> GetAgentJobAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync($"/api/admin/agent-jobs/{jobId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AgentJobDto>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no agent job payload.");
    }

    public async Task<CreateSupportRequestResponse> CreateSupportRequestAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CreateSupportRequestRequest request,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/support-requests",
            request,
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CreateSupportRequestResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no response body.");
    }

    public async Task<EndActiveSessionResponse> EndActiveSessionAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        var response = await client.PostAsync(
            $"/api/admin/clients/{clientId}/active-session/end",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<EndActiveSessionResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no response body.");
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetChatMessagesAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.GetAsync($"/api/admin/clients/{clientId}/chat-messages", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<ChatMessageDto>>(JsonOptions, cancellationToken))
            ?? [];
    }

    public async Task<ChatMessageDto> SendChatMessageAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        string message,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/chat-messages",
            new SendAdminChatMessageRequest(message),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ChatMessageDto>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no chat message payload.");
    }

    public async Task DeleteClientAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.DeleteAsync(
            $"/api/admin/clients/{clientId}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateClientMetadataAsync(
        string serverBaseUrl,
        string apiKey,
        string? mfaCode,
        Guid clientId,
        UpdateAdminClientMetadataRequest request,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl, apiKey, mfaCode);
        using var response = await client.PutAsJsonAsync(
            $"/api/admin/clients/{clientId}/metadata",
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private HttpClient GetOrCreateClient(string serverBaseUrl, string apiKey, string? mfaCode)
    {
        if (!Uri.TryCreate(serverBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Server URL is not a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Admin API key is required.");
        }

        if (_cachedClient is not null &&
            string.Equals(_cachedServerBaseUrl, serverBaseUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_cachedApiKey, apiKey, StringComparison.Ordinal) &&
            string.Equals(_cachedMfaCode, mfaCode, StringComparison.Ordinal))
        {
            return _cachedClient;
        }

        _cachedClient?.Dispose();

        var client = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.Add(ApiKeyHeaderName, apiKey.Trim());
        if (!string.IsNullOrWhiteSpace(mfaCode))
        {
            client.DefaultRequestHeaders.Add(MfaHeaderName, mfaCode.Trim());
        }
        _cachedClient = client;
        _cachedServerBaseUrl = serverBaseUrl;
        _cachedApiKey = apiKey;
        _cachedMfaCode = mfaCode;
        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ExtractMessage(responseBody);
        var prefix = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Unauthorized",
            System.Net.HttpStatusCode.Forbidden => "Forbidden",
            _ => $"Request failed ({(int)response.StatusCode})"
        };

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? prefix : $"{prefix}: {message}");
    }

    private static string ExtractMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("message", out var messageProperty))
                {
                    return messageProperty.GetString() ?? string.Empty;
                }

                if (document.RootElement.TryGetProperty("title", out var titleProperty))
                {
                    return titleProperty.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
        }

        return responseBody.Trim();
    }
}
