using System.Net.Http.Json;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class ServerApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly ServiceOptions _options;
    private readonly ClientEnvironmentDiscoveryService _environmentDiscoveryService;

    public ServerApiClient(HttpClient httpClient, IOptions<ServiceOptions> options, ClientEnvironmentDiscoveryService environmentDiscoveryService)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _environmentDiscoveryService = environmentDiscoveryService;
        _httpClient.BaseAddress = new Uri(_options.ServerBaseUrl, UriKind.Absolute);
    }

    public async Task<RegisterClientResponse> RegisterAsync(CancellationToken cancellationToken)
    {
        var request = BuildRegistrationRequest(_environmentDiscoveryService.GetSnapshot());
        var response = await _httpClient.PostAsJsonAsync(
            "/api/clients/register",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterClientResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no registration payload.");
    }

    public async Task<ClientHeartbeatResponse> SendHeartbeatAsync(ClientIdentity identity, CancellationToken cancellationToken)
    {
        var snapshot = _environmentDiscoveryService.GetSnapshot();
        var sessionState = DetectSessionState();
        var effectiveConsentRequired = snapshot.ConsentRequired && (sessionState.HasInteractiveUser || !_options.AllowSupportAtLogonScreen);
        var effectiveAutoApprove = snapshot.AutoApproveSupportRequests || (!sessionState.HasInteractiveUser && _options.AllowSupportAtLogonScreen);
        var response = await _httpClient.PostAsJsonAsync(
            "/api/clients/heartbeat",
            new ClientHeartbeatRequest(
                identity.ClientId,
                identity.ClientSecret,
                sessionState.DisplayUserName,
                sessionState.HasInteractiveUser,
                sessionState.IsAtLogonScreen,
                GetAgentVersion(),
                snapshot.BatteryPercentage,
                effectiveConsentRequired,
                effectiveAutoApprove,
                snapshot.TailscaleConnected,
                snapshot.TailscaleIpAddresses,
                DateTimeOffset.UtcNow,
                BuildChannels(),
                snapshot.RustDeskId,
                snapshot.DiskUsages,
                snapshot.TotalMemoryBytes,
                snapshot.AvailableMemoryBytes,
                snapshot.OsDescription,
                snapshot.LastBootAtUtc),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientHeartbeatResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no heartbeat payload.");
    }

    public async Task<CompleteFileTransferResponse> CompleteFileTransferAsync(
        Guid transferId,
        CompleteFileTransferRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/clients/file-transfers/{transferId}/complete",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompleteFileTransferResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no file transfer completion payload.");
    }

    public async Task<CompleteAgentJobResponse> CompleteAgentJobAsync(
        Guid jobId,
        CompleteAgentJobRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/clients/agent-jobs/{jobId}/complete",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompleteAgentJobResponse>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Server returned no agent job completion payload.");
    }

    private IReadOnlyList<RemoteChannel> BuildChannels()
    {
        var channels = new List<RemoteChannel> { RemoteChannel.WinRm };
        var snapshot = _environmentDiscoveryService.GetSnapshot();
        if (snapshot.TailscaleConnected)
        {
            channels.Insert(0, RemoteChannel.Rdp);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RustDeskId))
        {
            channels.Add(RemoteChannel.RustDesk);
        }

        return channels;
    }

    private RegisterClientRequest BuildRegistrationRequest(ClientRuntimeSnapshot snapshot)
    {
        var requestedAtUtc = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var sessionState = DetectSessionState();
        var effectiveConsentRequired = snapshot.ConsentRequired && (sessionState.HasInteractiveUser || !_options.AllowSupportAtLogonScreen);
        var effectiveAutoApprove = snapshot.AutoApproveSupportRequests || (!sessionState.HasInteractiveUser && _options.AllowSupportAtLogonScreen);
        var unsignedRequest = new RegisterClientRequest(
            _options.DeviceName,
            Environment.MachineName,
            sessionState.DisplayUserName,
            sessionState.HasInteractiveUser,
            sessionState.IsAtLogonScreen,
            GetAgentVersion(),
            snapshot.BatteryPercentage,
            effectiveConsentRequired,
            effectiveAutoApprove,
            snapshot.TailscaleConnected,
            snapshot.TailscaleIpAddresses,
            BuildChannels(),
            requestedAtUtc,
            nonce,
            string.Empty,
            snapshot.RustDeskId,
            snapshot.DiskUsages,
            snapshot.TotalMemoryBytes,
            snapshot.AvailableMemoryBytes,
            snapshot.OsDescription,
            snapshot.LastBootAtUtc);

        var signature = RegistrationSignatureHelper.ComputeSignature(unsignedRequest, _options.RegistrationSharedKey);
        return unsignedRequest with
        {
            RegistrationSignature = signature
        };
    }

    private static ClientSessionState DetectSessionState()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "query.exe",
                Arguments = "user",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
                var disconnectedUser = string.Empty;
                foreach (var line in lines.Skip(1))
                {
                    var parsedSession = ParseQueryUserLine(line);
                    if (parsedSession is null || string.IsNullOrWhiteSpace(parsedSession.UserName))
                    {
                        continue;
                    }

                    if (parsedSession.IsActive)
                    {
                        return new ClientSessionState(parsedSession.UserName, true, false);
                    }

                    if (parsedSession.IsDisconnected &&
                        string.IsNullOrWhiteSpace(disconnectedUser))
                    {
                        disconnectedUser = parsedSession.UserName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(disconnectedUser))
                {
                    return new ClientSessionState(disconnectedUser, true, false);
                }
            }
        }
        catch
        {
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is not null)
            {
                var userName = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);
                if (!string.IsNullOrWhiteSpace(userName))
                {
                    return new ClientSessionState(userName, true, false);
                }
            }
        }
        catch
        {
        }

        var fallbackUser = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(fallbackUser) &&
            !string.Equals(fallbackUser, "SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            return new ClientSessionState(fallbackUser, true, false);
        }

        return new ClientSessionState("Login Screen", false, true);
    }

    private static ParsedSessionState? ParseQueryUserLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var normalized = Regex.Replace(line.Trim(), @"\s{2,}", "|");
        var parts = normalized.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var userName = parts[0].TrimStart('>');
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var loweredLine = line.ToLowerInvariant();
        var isActive = line.Contains('>')
            || loweredLine.Contains(" active ")
            || loweredLine.Contains(" aktiv ")
            || loweredLine.Contains(" actif ")
            || loweredLine.Contains(" activo ");
        var isDisconnected = loweredLine.Contains(" disc ")
            || loweredLine.Contains(" getrennt ")
            || loweredLine.Contains(" discon ")
            || loweredLine.Contains(" déconnect");

        return new ParsedSessionState(userName, isActive, isDisconnected);
    }

    private static string GetAgentVersion()
    {
        return UpdateManifestEvaluator.NormalizeVersion(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");
    }

    private sealed record ClientSessionState(
        string DisplayUserName,
        bool HasInteractiveUser,
        bool IsAtLogonScreen);

    private sealed record ParsedSessionState(
        string UserName,
        bool IsActive,
        bool IsDisconnected);
}
