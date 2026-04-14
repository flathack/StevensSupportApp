using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Tray.Services;

internal sealed class TrayApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private HttpClient? _cachedClient;
    private string? _cachedServerBaseUrl;

    public async Task<GetSupportStateResponse> GetSupportStateAsync(string serverBaseUrl, ClientIdentity identity, CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl);
        var response = await client.PostAsJsonAsync(
            "/api/clients/support-state",
            new GetSupportStateRequest(identity.ClientId, identity.ClientSecret),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GetSupportStateResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no support-state payload.");
    }

    public async Task<SubmitSupportDecisionResponse> SubmitDecisionAsync(
        string serverBaseUrl,
        Guid requestId,
        SubmitSupportDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl);
        var response = await client.PostAsJsonAsync(
            $"/api/clients/support-requests/{requestId}/decision",
            request,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitSupportDecisionResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no decision payload.");
    }

    public async Task<ChatMessageDto> SendChatMessageAsync(
        string serverBaseUrl,
        SendClientChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient(serverBaseUrl);
        var response = await client.PostAsJsonAsync(
            "/api/clients/chat-messages",
            request,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatMessageDto>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no chat message payload.");
    }

    private HttpClient GetOrCreateClient(string serverBaseUrl)
    {
        if (!Uri.TryCreate(serverBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Server URL is not a valid absolute URI.");
        }

        if (_cachedClient is not null && string.Equals(_cachedServerBaseUrl, serverBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedClient;
        }

        _cachedClient?.Dispose();
        _cachedClient = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(10)
        };
        _cachedServerBaseUrl = serverBaseUrl;
        return _cachedClient;
    }
}
