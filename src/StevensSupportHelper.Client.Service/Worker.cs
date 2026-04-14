using System.Net;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Client.Service.Services;
using StevensSupportHelper.Shared.Contracts;
using StevensSupportHelper.Shared.Diagnostics;

namespace StevensSupportHelper.Client.Service;

public sealed class Worker(
    ILogger<Worker> logger,
    ClientIdentityStore identityStore,
    ServerApiClient serverApiClient,
    ManagedFileTransferService managedFileTransferService,
    AgentJobProcessor agentJobProcessor,
    ClientUpdateCoordinator updateCoordinator,
    IOptions<ServiceOptions> options) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly ClientIdentityStore _identityStore = identityStore;
    private readonly ServerApiClient _serverApiClient = serverApiClient;
    private readonly ManagedFileTransferService _managedFileTransferService = managedFileTransferService;
    private readonly AgentJobProcessor _agentJobProcessor = agentJobProcessor;
    private readonly ClientUpdateCoordinator _updateCoordinator = updateCoordinator;
    private readonly ServiceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ClientIdentity? identity = null;
        Guid? lastSeenRequestId = null;
        DateTimeOffset nextUpdateCheckAtUtc = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                identity ??= await EnsureIdentityWithRetryAsync(stoppingToken);
                ClientHeartbeatResponse heartbeat = await _serverApiClient.SendHeartbeatAsync(identity, stoppingToken);
                if (heartbeat.PendingSupportRequest is { } request && request.RequestId != lastSeenRequestId)
                {
                    lastSeenRequestId = request.RequestId;
                    _logger.LogInformation(
                        "Support request {RequestId} queued by {Admin} via {Channel}. Reason: {Reason}",
                        request.RequestId,
                        request.AdminDisplayName,
                        request.PreferredChannel,
                        request.Reason);
                }

                foreach (var transfer in heartbeat.PendingFileTransfers)
                {
                    _logger.LogInformation(
                        "Processing transfer {TransferId} ({Direction}) for {RelativePath}",
                        transfer.TransferId,
                        transfer.Direction,
                        transfer.RelativePath);
                    await _managedFileTransferService.ProcessAsync(identity, transfer, _serverApiClient, stoppingToken);
                }

                foreach (var job in heartbeat.PendingAgentJobs)
                {
                    _logger.LogInformation(
                        "Processing agent job {JobId} ({JobType})",
                        job.JobId,
                        job.JobType);
                    await ProcessAgentJobAsync(identity, job, stoppingToken);
                }

                if (DateTimeOffset.UtcNow >= nextUpdateCheckAtUtc)
                {
                    try
                    {
                        var pendingUpdate = await _updateCoordinator.CheckForUpdateAsync(stoppingToken);
                        if (pendingUpdate is not null)
                        {
                            _logger.LogInformation(
                                "Staged client update {Version} at {PackagePath}",
                                pendingUpdate.Version,
                                pendingUpdate.PackagePath);
                        }
                    }
                    catch (Exception updateException)
                    {
                        _logger.LogWarning(updateException, "Update check failed.");
                    }

                    nextUpdateCheckAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(15, _options.UpdateCheckIntervalMinutes));
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, heartbeat.NextHeartbeatIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    _logger.LogInformation("Client identity rejected by server. Re-registering.");
                    AppDiagnostics.WriteEvent("ClientService", "IdentityRejected", "Client identity was rejected by the server. Deleting local identity and re-registering.", exception);
                    await _identityStore.DeleteAsync(stoppingToken);
                    identity = null;
                    continue;
                }

                _logger.LogWarning(exception, "Heartbeat failed. Retrying in {DelaySeconds} seconds.", _options.HeartbeatIntervalSeconds);
                AppDiagnostics.WriteEvent(
                    "ClientService",
                    "HeartbeatFailed",
                    $"Heartbeat or registration failed. Retrying in {_options.HeartbeatIntervalSeconds} seconds.",
                    exception);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.HeartbeatIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task<ClientIdentity> EnsureIdentityWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                return await EnsureIdentityAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Client registration is currently unavailable. Retrying in {DelaySeconds} seconds.",
                    _options.HeartbeatIntervalSeconds);
                AppDiagnostics.WriteEvent(
                    "ClientService",
                    "RegistrationRetry",
                    $"Client registration is currently unavailable. Retrying in {_options.HeartbeatIntervalSeconds} seconds.",
                    exception);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.HeartbeatIntervalSeconds)), cancellationToken);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private async Task<ClientIdentity> EnsureIdentityAsync(CancellationToken cancellationToken)
    {
        var existing = await _identityStore.LoadAsync(cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Using existing client identity {ClientId}", existing.ClientId);
            AppDiagnostics.WriteEvent("ClientService", "IdentityLoaded", $"Using existing client identity {existing.ClientId}.");
            return existing;
        }

        RegisterClientResponse registration = await _serverApiClient.RegisterAsync(cancellationToken);
        var created = new ClientIdentity(registration.ClientId, registration.ClientSecret);
        await _identityStore.SaveAsync(created, cancellationToken);
        _logger.LogInformation("Registered new client identity {ClientId}", created.ClientId);
        AppDiagnostics.WriteEvent("ClientService", "Registered", $"Registered new client identity {created.ClientId}.");
        return created;
    }

    private async Task ProcessAgentJobAsync(ClientIdentity identity, AgentJobDto job, CancellationToken cancellationToken)
    {
        try
        {
            var resultJson = await _agentJobProcessor.ProcessAsync(job, cancellationToken);
            await _serverApiClient.CompleteAgentJobAsync(
                job.JobId,
                new CompleteAgentJobRequest(identity.ClientId, identity.ClientSecret, true, resultJson, null),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _serverApiClient.CompleteAgentJobAsync(
                job.JobId,
                new CompleteAgentJobRequest(identity.ClientId, identity.ClientSecret, false, null, exception.Message),
                cancellationToken);
        }
    }
}
