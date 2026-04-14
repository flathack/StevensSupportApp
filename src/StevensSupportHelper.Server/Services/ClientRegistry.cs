using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Services;

public sealed class ClientRegistry
{
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(45);
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, RegisteredClient> _clients;
    private readonly List<AuditEntryDto> _auditEntries;
    private readonly Dictionary<Guid, FileTransferDto> _fileTransfers;
    private readonly Dictionary<Guid, AgentJobDto> _agentJobs;
    private readonly List<ChatMessageDto> _chatMessages;
    private readonly ServerStateStore _stateStore;
    private readonly int _maxAuditEntries;
    private readonly int _maxTransferBytes;
    private readonly TimeSpan _sessionTimeout;
    private readonly TimeSpan _consentTimeout;
    private readonly ClientRegistrationVerifier _registrationVerifier;

    public ClientRegistry(ServerStateStore stateStore, IOptions<ServerStorageOptions> options, ClientRegistrationVerifier registrationVerifier)
    {
        _stateStore = stateStore;
        _maxAuditEntries = Math.Max(20, options.Value.MaxAuditEntries);
        _maxTransferBytes = Math.Max(1024, options.Value.MaxTransferBytes);
        _sessionTimeout = TimeSpan.FromMinutes(Math.Max(1, options.Value.SessionTimeoutMinutes));
        _consentTimeout = TimeSpan.FromMinutes(Math.Max(1, options.Value.ConsentTimeoutMinutes));
        _registrationVerifier = registrationVerifier;

        var persistedState = _stateStore.Load();
        _clients = persistedState.Clients
            .Select(RegisteredClient.FromPersisted)
            .ToDictionary(client => client.ClientId);
        _auditEntries = persistedState.AuditEntries
            .Select(entry => new AuditEntryDto(
                entry.AuditEntryId,
                entry.EventType,
                entry.ClientId,
                entry.DeviceName,
                entry.Actor,
                entry.Message,
                entry.CreatedAtUtc))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ToList();
        _fileTransfers = persistedState.FileTransfers
            .Select(FromPersistedFileTransfer)
            .ToDictionary(transfer => transfer.TransferId);
        _agentJobs = persistedState.AgentJobs
            .Select(FromPersistedAgentJob)
            .ToDictionary(job => job.JobId);
        _chatMessages = persistedState.ChatMessages
            .Select(record => new ChatMessageDto(
                record.MessageId,
                record.ClientId,
                record.SenderRole,
                record.SenderDisplayName,
                record.Message,
                record.CreatedAtUtc))
            .OrderBy(entry => entry.CreatedAtUtc)
            .ToList();
    }

    public RegisterClientResponse Register(RegisterClientRequest request)
    {
        if (!_registrationVerifier.Validate(request, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        lock (_syncRoot)
        {
            var existing = _clients.Values.FirstOrDefault(client =>
                string.Equals(client.MachineName, request.MachineName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(client.DeviceName, request.DeviceName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.DeviceName = request.DeviceName;
                existing.MachineName = request.MachineName;
                existing.CurrentUser = request.CurrentUser;
                existing.HasInteractiveUser = request.HasInteractiveUser;
                existing.IsAtLogonScreen = request.IsAtLogonScreen;
                existing.AgentVersion = request.AgentVersion;
                existing.BatteryPercentage = request.BatteryPercentage;
                existing.DiskUsages = NormalizeDiskUsages(request.DiskUsages);
                existing.TotalMemoryBytes = NormalizeNullableBytes(request.TotalMemoryBytes);
                existing.AvailableMemoryBytes = NormalizeAvailableBytes(request.TotalMemoryBytes, request.AvailableMemoryBytes);
                existing.OsDescription = NormalizeNullableString(request.OsDescription);
                existing.LastBootAtUtc = request.LastBootAtUtc;
                existing.ConsentRequired = request.ConsentRequired;
                existing.AutoApproveSupportRequests = request.AutoApproveSupportRequests;
                existing.TailscaleConnected = request.TailscaleConnected;
                existing.TailscaleIpAddresses = NormalizeTailscaleIpAddresses(request.TailscaleIpAddresses);
                existing.SupportedChannels = request.SupportedChannels.ToArray();
                existing.ReportedRustDeskId = request.RustDeskId?.Trim();
                existing.LastSeenAtUtc = DateTimeOffset.UtcNow;
                AppendAudit("ClientUpdated", existing.ClientId, existing.DeviceName, existing.CurrentUser, "Client registration was refreshed.");
                SaveState();
                return new RegisterClientResponse(existing.ClientId, existing.ClientSecret, 15);
            }

            var now = DateTimeOffset.UtcNow;
            var created = new RegisteredClient
            {
                ClientId = Guid.NewGuid(),
                ClientSecret = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                DeviceName = request.DeviceName,
                MachineName = request.MachineName,
                CurrentUser = request.CurrentUser,
                HasInteractiveUser = request.HasInteractiveUser,
                IsAtLogonScreen = request.IsAtLogonScreen,
                AgentVersion = request.AgentVersion,
                BatteryPercentage = request.BatteryPercentage,
                DiskUsages = NormalizeDiskUsages(request.DiskUsages),
                TotalMemoryBytes = NormalizeNullableBytes(request.TotalMemoryBytes),
                AvailableMemoryBytes = NormalizeAvailableBytes(request.TotalMemoryBytes, request.AvailableMemoryBytes),
                OsDescription = NormalizeNullableString(request.OsDescription),
                LastBootAtUtc = request.LastBootAtUtc,
                ConsentRequired = request.ConsentRequired,
                AutoApproveSupportRequests = request.AutoApproveSupportRequests,
                TailscaleConnected = request.TailscaleConnected,
                TailscaleIpAddresses = NormalizeTailscaleIpAddresses(request.TailscaleIpAddresses),
                ReportedRustDeskId = request.RustDeskId?.Trim(),
                RegisteredAtUtc = now,
                LastSeenAtUtc = now,
                SupportedChannels = request.SupportedChannels.ToArray()
            };

            _clients[created.ClientId] = created;
            AppendAudit("ClientRegistered", created.ClientId, created.DeviceName, created.CurrentUser, "Client was registered on the server.");
            SaveState();
            return new RegisterClientResponse(created.ClientId, created.ClientSecret, 15);
        }
    }

    public ClientHeartbeatResponse Heartbeat(ClientHeartbeatRequest request)
    {
        lock (_syncRoot)
        {
            RegisteredClient client = GetClient(request.ClientId, request.ClientSecret);

            client.CurrentUser = request.CurrentUser;
            client.HasInteractiveUser = request.HasInteractiveUser;
            client.IsAtLogonScreen = request.IsAtLogonScreen;
            client.AgentVersion = request.AgentVersion;
            client.BatteryPercentage = request.BatteryPercentage;
            client.ConsentRequired = request.ConsentRequired;
            client.DiskUsages = NormalizeDiskUsages(request.DiskUsages);
            client.TotalMemoryBytes = NormalizeNullableBytes(request.TotalMemoryBytes);
            client.AvailableMemoryBytes = NormalizeAvailableBytes(request.TotalMemoryBytes, request.AvailableMemoryBytes);
            client.OsDescription = NormalizeNullableString(request.OsDescription);
            client.LastBootAtUtc = request.LastBootAtUtc;
            client.AutoApproveSupportRequests = request.AutoApproveSupportRequests;
            client.TailscaleConnected = request.TailscaleConnected;
            client.TailscaleIpAddresses = NormalizeTailscaleIpAddresses(request.TailscaleIpAddresses);
            client.LastSeenAtUtc = DateTimeOffset.UtcNow;
            client.StartedAtUtc = request.StartedAtUtc;
            client.SupportedChannels = request.SupportedChannels.ToArray();
            client.ReportedRustDeskId = request.RustDeskId?.Trim();

            ExpireTimedOutState();
            SaveState();

            return new ClientHeartbeatResponse(
                DateTimeOffset.UtcNow,
                15,
                client.PendingSupportRequest,
                client.ActiveSession,
                GetPendingTransfersForClient(client.ClientId),
                GetPendingAgentJobsForClient(client.ClientId));
        }
    }

    public GetSupportStateResponse GetSupportState(GetSupportStateRequest request)
    {
        lock (_syncRoot)
        {
            RegisteredClient client = GetClient(request.ClientId, request.ClientSecret);
            ExpireTimedOutState();
            return new GetSupportStateResponse(client.PendingSupportRequest, client.ActiveSession, GetChatMessagesForClient(client.ClientId));
        }
    }

    public IReadOnlyList<ChatMessageDto> GetChatMessages(Guid clientId)
    {
        lock (_syncRoot)
        {
            if (!_clients.ContainsKey(clientId))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            return GetChatMessagesForClient(clientId);
        }
    }

    public IReadOnlyList<ChatMessageDto> GetChatMessagesForClientIdentity(Guid clientId, string clientSecret)
    {
        lock (_syncRoot)
        {
            _ = GetClient(clientId, clientSecret);
            return GetChatMessagesForClient(clientId);
        }
    }

    public ChatMessageDto AddAdminChatMessage(Guid clientId, string adminDisplayName, string message)
    {
        lock (_syncRoot)
        {
            if (!_clients.TryGetValue(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            var chatMessage = CreateChatMessage(clientId, "Admin", adminDisplayName, message);
            AppendAudit("ChatMessageSent", clientId, client.DeviceName, adminDisplayName, $"Chat message sent to {client.DeviceName}.");
            SaveState();
            return chatMessage;
        }
    }

    public ChatMessageDto AddClientChatMessage(SendClientChatMessageRequest request)
    {
        lock (_syncRoot)
        {
            var client = GetClient(request.ClientId, request.ClientSecret);
            var senderDisplayName = string.IsNullOrWhiteSpace(request.SenderDisplayName)
                ? client.CurrentUser
                : request.SenderDisplayName.Trim();
            var chatMessage = CreateChatMessage(client.ClientId, "Client", senderDisplayName, request.Message);
            AppendAudit("ChatMessageReply", client.ClientId, client.DeviceName, senderDisplayName, $"Chat reply received from {client.DeviceName}.");
            SaveState();
            return chatMessage;
        }
    }

    public SubmitSupportDecisionResponse SubmitSupportDecision(Guid requestId, SubmitSupportDecisionRequest request)
    {
        lock (_syncRoot)
        {
            RegisteredClient client = GetClient(request.ClientId, request.ClientSecret);
            ExpireTimedOutState();

            if (client.PendingSupportRequest is null || client.PendingSupportRequest.RequestId != requestId)
            {
                throw new KeyNotFoundException("Support request not found for client.");
            }

            var updatedRequest = client.PendingSupportRequest with
            {
                Status = request.Approved ? "Approved" : "Denied"
            };

            client.PendingSupportRequest = null;
            client.ActiveSession = request.Approved
                ? new SupportSessionDto(
                    Guid.NewGuid(),
                    updatedRequest.RequestId,
                    updatedRequest.AdminDisplayName,
                    updatedRequest.PreferredChannel,
                    DateTimeOffset.UtcNow,
                    "Active")
                : null;

            AppendAudit(
                request.Approved ? "SupportApproved" : "SupportDenied",
                client.ClientId,
                client.DeviceName,
                client.CurrentUser,
                request.Approved
                    ? $"Support request {updatedRequest.RequestId} was approved."
                    : $"Support request {updatedRequest.RequestId} was denied.");
            SaveState();

            return new SubmitSupportDecisionResponse(updatedRequest, client.ActiveSession);
        }
    }

    public IReadOnlyList<AdminClientSummary> GetClients()
    {
        lock (_syncRoot)
        {
            ExpireTimedOutState();
            return _clients.Values
                .OrderBy(client => client.DeviceName, StringComparer.OrdinalIgnoreCase)
                .Select(ToAdminClientSummary)
                .ToArray();
        }
    }

    public IReadOnlyList<AuditEntryDto> GetAuditEntries(int take)
    {
        lock (_syncRoot)
        {
            return _auditEntries
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .Take(Math.Max(1, take))
                .ToArray();
        }
    }

    public CreateSupportRequestResponse CreateSupportRequest(Guid clientId, CreateSupportRequestRequest request, string adminDisplayName)
    {
        lock (_syncRoot)
        {
            ExpireTimedOutState();

            if (!_clients.TryGetValue(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            if (client.PendingSupportRequest is not null)
            {
                throw new InvalidOperationException("Client already has a pending support request.");
            }

            if (client.ActiveSession is not null)
            {
                throw new InvalidOperationException("Client already has an active session.");
            }

            if (!client.SupportedChannels.Contains(request.PreferredChannel))
            {
                throw new InvalidOperationException("Requested channel is not supported by the client.");
            }

            var supportRequest = new SupportRequestDto(
                Guid.NewGuid(),
                adminDisplayName,
                request.PreferredChannel,
                request.Reason,
                DateTimeOffset.UtcNow,
                "PendingClientConsent");

            if (client.AutoApproveSupportRequests)
            {
                var approvedRequest = supportRequest with
                {
                    Status = "Approved"
                };
                var activeSession = new SupportSessionDto(
                    Guid.NewGuid(),
                    approvedRequest.RequestId,
                    adminDisplayName,
                    approvedRequest.PreferredChannel,
                    DateTimeOffset.UtcNow,
                    "Active");
                client.PendingSupportRequest = null;
                client.ActiveSession = activeSession;
                AppendAudit(
                    "SupportAutoApproved",
                    client.ClientId,
                    client.DeviceName,
                    adminDisplayName,
                    $"Support request {approvedRequest.RequestId} was auto-approved by client policy.");
                SaveState();

                return new CreateSupportRequestResponse(
                    approvedRequest.RequestId,
                    approvedRequest.Status,
                    $"Support request for {client.DeviceName} was auto-approved.");
            }

            client.PendingSupportRequest = supportRequest;
            client.ActiveSession = null;
            AppendAudit(
                "SupportRequested",
                client.ClientId,
                client.DeviceName,
                adminDisplayName,
                $"Support request {supportRequest.RequestId} queued via {request.PreferredChannel}.");
            SaveState();

            return new CreateSupportRequestResponse(
                supportRequest.RequestId,
                supportRequest.Status,
                $"Support request queued for {client.DeviceName}.");
        }
    }

    public QueueFileTransferResponse QueueUploadTransfer(Guid clientId, QueueFileUploadRequest request, string actorDisplayName)
    {
        lock (_syncRoot)
        {
            var client = GetClientByIdForActiveSession(clientId);
            var normalizedPath = NormalizeRelativePath(request.TargetRelativePath, fallbackFileName: request.FileName);
            ValidateBase64PayloadSize(request.ContentBase64);

            var transfer = new FileTransferDto(
                Guid.NewGuid(),
                client.ClientId,
                client.ActiveSession!.SessionId,
                FileTransferDirection.AdminToClient,
                normalizedPath,
                string.IsNullOrWhiteSpace(request.FileName) ? Path.GetFileName(normalizedPath) : request.FileName,
                "PendingClientProcessing",
                DateTimeOffset.UtcNow,
                null,
                request.ContentBase64,
                null);

            _fileTransfers[transfer.TransferId] = transfer;
            AppendAudit(
                "FileUploadQueued",
                client.ClientId,
                client.DeviceName,
                actorDisplayName,
                $"Upload transfer {transfer.TransferId} queued for {transfer.RelativePath}.");
            SaveState();

            return new QueueFileTransferResponse(transfer.TransferId, transfer.Status, $"Upload queued for {client.DeviceName}.");
        }
    }

    public QueueFileTransferResponse QueueDownloadTransfer(Guid clientId, QueueFileDownloadRequest request, string actorDisplayName)
    {
        lock (_syncRoot)
        {
            var client = GetClientByIdForActiveSession(clientId);
            var normalizedPath = NormalizeRelativePath(request.SourceRelativePath);

            var transfer = new FileTransferDto(
                Guid.NewGuid(),
                client.ClientId,
                client.ActiveSession!.SessionId,
                FileTransferDirection.ClientToAdmin,
                normalizedPath,
                Path.GetFileName(normalizedPath),
                "PendingClientProcessing",
                DateTimeOffset.UtcNow,
                null,
                null,
                null);

            _fileTransfers[transfer.TransferId] = transfer;
            AppendAudit(
                "FileDownloadQueued",
                client.ClientId,
                client.DeviceName,
                actorDisplayName,
                $"Download transfer {transfer.TransferId} queued for {transfer.RelativePath}.");
            SaveState();

            return new QueueFileTransferResponse(transfer.TransferId, transfer.Status, $"Download queued for {client.DeviceName}.");
        }
    }

    public FileTransferDto GetFileTransfer(Guid transferId)
    {
        lock (_syncRoot)
        {
            if (!_fileTransfers.TryGetValue(transferId, out var transfer))
            {
                throw new KeyNotFoundException("File transfer not found.");
            }

            return transfer;
        }
    }

    public FileTransferContentResponse GetFileTransferContent(Guid transferId)
    {
        lock (_syncRoot)
        {
            if (!_fileTransfers.TryGetValue(transferId, out var transfer))
            {
                throw new KeyNotFoundException("File transfer not found.");
            }

            if (transfer.Direction != FileTransferDirection.ClientToAdmin || transfer.Status != "Completed" || string.IsNullOrWhiteSpace(transfer.ContentBase64))
            {
                throw new InvalidOperationException("File transfer content is not available.");
            }

            return new FileTransferContentResponse(
                transfer.TransferId,
                transfer.FileName,
                transfer.Status,
                transfer.ContentBase64);
        }
    }

    public CompleteFileTransferResponse CompleteFileTransfer(Guid transferId, CompleteFileTransferRequest request)
    {
        lock (_syncRoot)
        {
            var client = GetClient(request.ClientId, request.ClientSecret);

            if (!_fileTransfers.TryGetValue(transferId, out var transfer) || transfer.ClientId != client.ClientId)
            {
                throw new KeyNotFoundException("File transfer not found for client.");
            }

            if (transfer.Status == "Completed")
            {
                return new CompleteFileTransferResponse(transfer.TransferId, transfer.Status, "File transfer was already completed.");
            }

            if (request.Success && transfer.Direction == FileTransferDirection.ClientToAdmin)
            {
                ValidateBase64PayloadSize(request.ContentBase64);
            }

            var updatedTransfer = transfer with
            {
                Status = request.Success ? "Completed" : "Failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ContentBase64 = transfer.Direction == FileTransferDirection.ClientToAdmin && request.Success
                    ? request.ContentBase64
                    : transfer.ContentBase64,
                ErrorMessage = request.Success ? null : request.ErrorMessage
            };

            _fileTransfers[transferId] = updatedTransfer;
            AppendAudit(
                request.Success ? "FileTransferCompleted" : "FileTransferFailed",
                client.ClientId,
                client.DeviceName,
                client.CurrentUser,
                request.Success
                    ? $"File transfer {transfer.TransferId} completed."
                    : $"File transfer {transfer.TransferId} failed: {request.ErrorMessage}");
            SaveState();

            return new CompleteFileTransferResponse(updatedTransfer.TransferId, updatedTransfer.Status, $"Transfer {updatedTransfer.TransferId} marked as {updatedTransfer.Status}.");
        }
    }

    public EndActiveSessionResponse EndActiveSession(Guid clientId, string actorDisplayName)
    {
        lock (_syncRoot)
        {
            if (!_clients.TryGetValue(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            if (client.ActiveSession is null)
            {
                throw new InvalidOperationException("Client has no active session.");
            }

            var endedSession = client.ActiveSession with
            {
                Status = "Ended"
            };

            client.ActiveSession = null;
            AppendAudit(
                "SessionEnded",
                client.ClientId,
                client.DeviceName,
                actorDisplayName,
                $"Session {endedSession.SessionId} was ended by {actorDisplayName}.");
            SaveState();

            return new EndActiveSessionResponse(
                endedSession.SessionId,
                endedSession.Status,
                $"Active session for {client.DeviceName} was ended.");
        }
    }

    public QueueAgentJobResponse QueueProcessSnapshotJob(Guid clientId, string actorDisplayName)
        => QueueAgentJob(clientId, AgentJobType.ProcessSnapshot, actorDisplayName, null);

    public QueueAgentJobResponse QueueWindowsUpdateScanJob(Guid clientId, string actorDisplayName)
        => QueueAgentJob(clientId, AgentJobType.WindowsUpdateScan, actorDisplayName, null);

    public QueueAgentJobResponse QueueWindowsUpdateInstallJob(Guid clientId, string actorDisplayName)
        => QueueAgentJob(clientId, AgentJobType.WindowsUpdateInstall, actorDisplayName, null);

    public QueueAgentJobResponse QueueRegistrySnapshotJob(Guid clientId, string registryPath, string actorDisplayName)
        => QueueAgentJob(
            clientId,
            AgentJobType.RegistrySnapshot,
            actorDisplayName,
            System.Text.Json.JsonSerializer.Serialize(new AgentRegistrySnapshotRequest(registryPath)));

    public QueueAgentJobResponse QueueServiceSnapshotJob(Guid clientId, string actorDisplayName)
        => QueueAgentJob(clientId, AgentJobType.ServiceSnapshot, actorDisplayName, null);

    public QueueAgentJobResponse QueueServiceControlJob(Guid clientId, string serviceName, string action, string actorDisplayName)
        => QueueAgentJob(
            clientId,
            AgentJobType.ServiceControl,
            actorDisplayName,
            System.Text.Json.JsonSerializer.Serialize(new AgentServiceControlRequest(serviceName, action)));

    public QueueAgentJobResponse QueueScriptExecutionJob(Guid clientId, string scriptContent, string actorDisplayName)
    {
        lock (_syncRoot)
        {
            if (!_clients.TryGetValue(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            var requestJson = System.Text.Json.JsonSerializer.Serialize(new AgentScriptExecutionRequest(
                scriptContent,
                client.DeviceName,
                client.MachineName,
                client.CurrentUser,
                client.AgentVersion,
                client.EffectiveRustDeskId,
                client.Notes,
                client.TailscaleIpAddresses));
            return QueueAgentJob(clientId, AgentJobType.ScriptExecution, actorDisplayName, requestJson);
        }
    }

    public QueueAgentJobResponse QueuePowerPlanSnapshotJob(Guid clientId, string actorDisplayName)
        => QueueAgentJob(clientId, AgentJobType.PowerPlanSnapshot, actorDisplayName, null);

    public QueueAgentJobResponse QueuePowerPlanActivateJob(Guid clientId, string planGuid, string actorDisplayName)
        => QueueAgentJob(
            clientId,
            AgentJobType.PowerPlanActivate,
            actorDisplayName,
            System.Text.Json.JsonSerializer.Serialize(new AgentPowerPlanActivateRequest(planGuid)));

    private QueueAgentJobResponse QueueAgentJob(Guid clientId, AgentJobType jobType, string actorDisplayName, string? requestJson)
    {
        lock (_syncRoot)
        {
            if (!_clients.TryGetValue(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            if (DateTimeOffset.UtcNow - client.LastSeenAtUtc > OnlineThreshold)
            {
                throw new InvalidOperationException("Client is currently offline.");
            }

            var activeExistingJob = _agentJobs.Values.FirstOrDefault(job =>
                job.ClientId == clientId &&
                job.JobType == jobType &&
                string.Equals(job.RequestJson, requestJson, StringComparison.Ordinal) &&
                job.Status == "PendingClientProcessing");
            if (activeExistingJob is not null)
            {
                return new QueueAgentJobResponse(activeExistingJob.JobId, activeExistingJob.Status, $"{jobType} job is already queued.");
            }

            var job = new AgentJobDto(
                Guid.NewGuid(),
                clientId,
                jobType,
                "PendingClientProcessing",
                DateTimeOffset.UtcNow,
                null,
                requestJson,
                null,
                null);

            _agentJobs[job.JobId] = job;
            AppendAudit(
                "AgentJobQueued",
                client.ClientId,
                client.DeviceName,
                actorDisplayName,
                $"Agent job {job.JobId} queued: {job.JobType}.");
            SaveState();

            return new QueueAgentJobResponse(job.JobId, job.Status, $"Agent job queued for {client.DeviceName}.");
        }
    }

    public AgentJobDto GetAgentJob(Guid jobId)
    {
        lock (_syncRoot)
        {
            if (!_agentJobs.TryGetValue(jobId, out var job))
            {
                throw new KeyNotFoundException("Agent job not found.");
            }

            return job;
        }
    }

    public CompleteAgentJobResponse CompleteAgentJob(Guid jobId, CompleteAgentJobRequest request)
    {
        lock (_syncRoot)
        {
            var client = GetClient(request.ClientId, request.ClientSecret);
            if (!_agentJobs.TryGetValue(jobId, out var job) || job.ClientId != client.ClientId)
            {
                throw new KeyNotFoundException("Agent job not found for client.");
            }

            if (job.Status == "Completed" || job.Status == "Failed")
            {
                return new CompleteAgentJobResponse(job.JobId, job.Status, "Agent job was already completed.");
            }

            var updatedJob = job with
            {
                Status = request.Success ? "Completed" : "Failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                RequestJson = job.RequestJson,
                ResultJson = request.Success ? request.ResultJson : null,
                ErrorMessage = request.Success ? null : request.ErrorMessage
            };

            _agentJobs[jobId] = updatedJob;
            AppendAudit(
                request.Success ? "AgentJobCompleted" : "AgentJobFailed",
                client.ClientId,
                client.DeviceName,
                client.CurrentUser,
                request.Success
                    ? $"Agent job {jobId} completed: {job.JobType}."
                    : $"Agent job {jobId} failed: {request.ErrorMessage}");
            SaveState();

            return new CompleteAgentJobResponse(updatedJob.JobId, updatedJob.Status, $"Agent job {updatedJob.JobId} marked as {updatedJob.Status}.");
        }
    }

    public void DeleteClient(Guid clientId, string actorDisplayName)
    {
        lock (_syncRoot)
        {
            if (!_clients.Remove(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            var transferIdsToRemove = _fileTransfers.Values
                .Where(transfer => transfer.ClientId == clientId)
                .Select(transfer => transfer.TransferId)
                .ToArray();
            foreach (var transferId in transferIdsToRemove)
            {
                _fileTransfers.Remove(transferId);
            }

            var agentJobIdsToRemove = _agentJobs.Values
                .Where(job => job.ClientId == clientId)
                .Select(job => job.JobId)
                .ToArray();
            foreach (var jobId in agentJobIdsToRemove)
            {
                _agentJobs.Remove(jobId);
            }

            AppendAudit(
                "ClientDeleted",
                clientId,
                client.DeviceName,
                actorDisplayName,
                $"Client {client.DeviceName} was deleted from the server registry.");
            SaveState();
        }
    }

    public void UpdateClientMetadata(Guid clientId, UpdateAdminClientMetadataRequest request, string actorDisplayName)
    {
        lock (_syncRoot)
        {
            if (!_clients.TryGetValue(clientId, out var client))
            {
                throw new KeyNotFoundException("Client not found.");
            }

            client.Notes = request.Notes?.Trim() ?? string.Empty;
            client.RustDeskIdOverride = string.IsNullOrWhiteSpace(request.RustDeskId) ? null : request.RustDeskId.Trim();
            client.RustDeskPassword = string.IsNullOrWhiteSpace(request.RustDeskPassword) ? null : request.RustDeskPassword.Trim();
            client.RemoteUserName = string.IsNullOrWhiteSpace(request.RemoteUserName) ? null : request.RemoteUserName.Trim();
            client.RemotePassword = string.IsNullOrWhiteSpace(request.RemotePassword) ? null : request.RemotePassword.Trim();

            AppendAudit(
                "ClientMetadataUpdated",
                clientId,
                client.DeviceName,
                actorDisplayName,
                $"Client metadata for {client.DeviceName} was updated.");
            SaveState();
        }
    }

    private AdminClientSummary ToAdminClientSummary(RegisteredClient client)
    {
        var clientMessages = _chatMessages
            .Where(message => message.ClientId == client.ClientId)
            .OrderBy(message => message.CreatedAtUtc)
            .ToArray();
        var lastAdminChatAtUtc = clientMessages
            .Where(message => string.Equals(message.SenderRole, "Admin", StringComparison.OrdinalIgnoreCase))
            .Select(message => (DateTimeOffset?)message.CreatedAtUtc)
            .LastOrDefault();
        var unreadClientChatCount = clientMessages.Count(message =>
            string.Equals(message.SenderRole, "Client", StringComparison.OrdinalIgnoreCase) &&
            (lastAdminChatAtUtc is null || message.CreatedAtUtc > lastAdminChatAtUtc.Value));
        var lastClientChatMessageAtUtc = clientMessages
            .Where(message => string.Equals(message.SenderRole, "Client", StringComparison.OrdinalIgnoreCase))
            .Select(message => (DateTimeOffset?)message.CreatedAtUtc)
            .LastOrDefault();

        return new AdminClientSummary(
            client.ClientId,
            client.DeviceName,
            client.MachineName,
            client.CurrentUser,
            client.HasInteractiveUser,
            client.IsAtLogonScreen,
            client.BatteryPercentage,
            client.DiskUsages,
            client.TotalMemoryBytes,
            client.AvailableMemoryBytes,
            client.OsDescription,
            client.LastBootAtUtc,
            DateTimeOffset.UtcNow - client.LastSeenAtUtc <= OnlineThreshold,
            client.ConsentRequired,
            client.AutoApproveSupportRequests,
            client.TailscaleConnected,
            client.TailscaleIpAddresses,
            client.AgentVersion,
            client.RegisteredAtUtc,
            client.LastSeenAtUtc,
            client.SupportedChannels,
            client.PendingSupportRequest,
            client.ActiveSession,
            unreadClientChatCount,
            lastClientChatMessageAtUtc,
            client.EffectiveRustDeskId,
            client.Notes,
            client.RustDeskPassword,
            client.RemoteUserName,
            client.RemotePassword);
    }

    private void ExpireTimedOutState()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        foreach (var client in _clients.Values)
        {
            if (client.PendingSupportRequest is not null && now - client.PendingSupportRequest.RequestedAtUtc > _consentTimeout)
            {
                AppendAudit(
                    "SupportRequestExpired",
                    client.ClientId,
                    client.DeviceName,
                    "System",
                    $"Support request {client.PendingSupportRequest.RequestId} expired after {_consentTimeout.TotalMinutes:0} minutes.");
                client.PendingSupportRequest = null;
                changed = true;
            }

            if (client.ActiveSession is not null && now - client.ActiveSession.ApprovedAtUtc > _sessionTimeout)
            {
                AppendAudit(
                    "SessionExpired",
                    client.ClientId,
                    client.DeviceName,
                    "System",
                    $"Session {client.ActiveSession.SessionId} expired after {_sessionTimeout.TotalMinutes:0} minutes.");
                client.ActiveSession = null;
                changed = true;
            }
        }

        if (changed)
        {
            SaveState();
        }
    }

    private RegisteredClient GetClient(Guid clientId, string clientSecret)
    {
        if (!_clients.TryGetValue(clientId, out var client) || !ConstantTimeEquals(client.ClientSecret, clientSecret))
        {
            throw new InvalidOperationException("Unknown client or invalid client secret.");
        }

        return client;
    }

    private void AppendAudit(string eventType, Guid? clientId, string deviceName, string actor, string message)
    {
        _auditEntries.Insert(0, new AuditEntryDto(
            Guid.NewGuid(),
            eventType,
            clientId,
            deviceName,
            actor,
            message,
            DateTimeOffset.UtcNow));

        if (_auditEntries.Count > _maxAuditEntries)
        {
            _auditEntries.RemoveRange(_maxAuditEntries, _auditEntries.Count - _maxAuditEntries);
        }
    }

    private void SaveState()
    {
        _stateStore.Save(new PersistedServerState
        {
            Clients = _clients.Values.Select(client => client.ToPersisted()).ToList(),
            AuditEntries = _auditEntries.Select(entry => new PersistedAuditEntry
            {
                AuditEntryId = entry.AuditEntryId,
                EventType = entry.EventType,
                ClientId = entry.ClientId,
                DeviceName = entry.DeviceName,
                Actor = entry.Actor,
                Message = entry.Message,
                CreatedAtUtc = entry.CreatedAtUtc
            }).ToList(),
            FileTransfers = _fileTransfers.Values.Select(transfer => new PersistedFileTransferRecord
            {
                TransferId = transfer.TransferId,
                ClientId = transfer.ClientId,
                SessionId = transfer.SessionId,
                Direction = transfer.Direction.ToString(),
                RelativePath = transfer.RelativePath,
                FileName = transfer.FileName,
                Status = transfer.Status,
                RequestedAtUtc = transfer.RequestedAtUtc,
                CompletedAtUtc = transfer.CompletedAtUtc,
                ContentBase64 = transfer.ContentBase64,
                ErrorMessage = transfer.ErrorMessage
            }).ToList(),
            AgentJobs = _agentJobs.Values.Select(job => new PersistedAgentJobRecord
            {
                JobId = job.JobId,
                ClientId = job.ClientId,
                JobType = job.JobType.ToString(),
                Status = job.Status,
                RequestedAtUtc = job.RequestedAtUtc,
                CompletedAtUtc = job.CompletedAtUtc,
                RequestJson = job.RequestJson,
                ResultJson = job.ResultJson,
                ErrorMessage = job.ErrorMessage
            }).ToList(),
            ChatMessages = _chatMessages.Select(message => new PersistedChatMessageRecord
            {
                MessageId = message.MessageId,
                ClientId = message.ClientId,
                SenderRole = message.SenderRole,
                SenderDisplayName = message.SenderDisplayName,
                Message = message.Message,
                CreatedAtUtc = message.CreatedAtUtc
            }).ToList()
        });
    }

    private RegisteredClient GetClientByIdForActiveSession(Guid clientId)
    {
        if (!_clients.TryGetValue(clientId, out var client))
        {
            throw new KeyNotFoundException("Client not found.");
        }

        if (client.ActiveSession is null)
        {
            throw new InvalidOperationException("Client has no active session.");
        }

        return client;
    }

    private IReadOnlyList<FileTransferDto> GetPendingTransfersForClient(Guid clientId)
    {
        return _fileTransfers.Values
            .Where(transfer => transfer.ClientId == clientId && transfer.Status == "PendingClientProcessing")
            .OrderBy(transfer => transfer.RequestedAtUtc)
            .ToArray();
    }

    private IReadOnlyList<AgentJobDto> GetPendingAgentJobsForClient(Guid clientId)
    {
        return _agentJobs.Values
            .Where(job => job.ClientId == clientId && job.Status == "PendingClientProcessing")
            .OrderBy(job => job.RequestedAtUtc)
            .ToArray();
    }

    private IReadOnlyList<ChatMessageDto> GetChatMessagesForClient(Guid clientId)
    {
        return _chatMessages
            .Where(message => message.ClientId == clientId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(100)
            .OrderBy(message => message.CreatedAtUtc)
            .ToArray();
    }

    private ChatMessageDto CreateChatMessage(Guid clientId, string senderRole, string senderDisplayName, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Chat message must not be empty.");
        }

        var chatMessage = new ChatMessageDto(
            Guid.NewGuid(),
            clientId,
            senderRole,
            string.IsNullOrWhiteSpace(senderDisplayName) ? senderRole : senderDisplayName.Trim(),
            message.Trim(),
            DateTimeOffset.UtcNow);
        _chatMessages.Add(chatMessage);

        if (_chatMessages.Count > 2000)
        {
            _chatMessages.RemoveRange(0, _chatMessages.Count - 2000);
        }

        return chatMessage;
    }

    private string NormalizeRelativePath(string? relativePath, string? fallbackFileName = null)
    {
        var candidate = string.IsNullOrWhiteSpace(relativePath) ? fallbackFileName : relativePath;
        var normalized = (candidate ?? string.Empty).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("A relative path is required for file transfer.");
        }

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("File transfer path must be relative.");
        }

        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part == "." || part == ".."))
        {
            throw new InvalidOperationException("File transfer path contains invalid traversal segments.");
        }

        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private void ValidateBase64PayloadSize(string? contentBase64)
    {
        if (string.IsNullOrWhiteSpace(contentBase64))
        {
            throw new InvalidOperationException("File transfer content is missing.");
        }

        var estimatedBytes = (contentBase64.Length * 3L) / 4L;
        if (estimatedBytes > _maxTransferBytes)
        {
            throw new InvalidOperationException($"File transfer exceeds the maximum size of {_maxTransferBytes} bytes.");
        }
    }

    private static IReadOnlyList<string> NormalizeTailscaleIpAddresses(IReadOnlyList<string>? addresses)
    {
        return (addresses ?? [])
            .Where(static address => !string.IsNullOrWhiteSpace(address))
            .Select(static address => address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DiskUsageDto> NormalizeDiskUsages(IReadOnlyList<DiskUsageDto>? diskUsages)
    {
        return (diskUsages ?? [])
            .Where(static disk => !string.IsNullOrWhiteSpace(disk.DriveName) && disk.TotalBytes > 0)
            .Select(static disk => new DiskUsageDto(
                disk.DriveName.Trim(),
                disk.TotalBytes,
                Math.Clamp(disk.FreeBytes, 0, disk.TotalBytes)))
            .Distinct()
            .OrderBy(static disk => disk.DriveName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static long? NormalizeNullableBytes(long? value) => value is > 0 ? value : null;

    private static long? NormalizeAvailableBytes(long? totalBytes, long? availableBytes)
    {
        if (totalBytes is not > 0 || availableBytes is null)
        {
            return null;
        }

        return Math.Clamp(availableBytes.Value, 0, totalBytes.Value);
    }

    private static string? NormalizeNullableString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static FileTransferDto FromPersistedFileTransfer(PersistedFileTransferRecord record)
    {
        return new FileTransferDto(
            record.TransferId,
            record.ClientId,
            record.SessionId,
            Enum.TryParse<FileTransferDirection>(record.Direction, true, out var direction) ? direction : FileTransferDirection.AdminToClient,
            record.RelativePath,
            record.FileName,
            record.Status,
            record.RequestedAtUtc,
            record.CompletedAtUtc,
            record.ContentBase64,
            record.ErrorMessage);
    }

    private static AgentJobDto FromPersistedAgentJob(PersistedAgentJobRecord record)
    {
        return new AgentJobDto(
            record.JobId,
            record.ClientId,
            Enum.TryParse<AgentJobType>(record.JobType, true, out var jobType) ? jobType : AgentJobType.ProcessSnapshot,
            record.Status,
            record.RequestedAtUtc,
            record.CompletedAtUtc,
            record.RequestJson,
            record.ResultJson,
            record.ErrorMessage);
    }

    private static bool ConstantTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var index = 0; index < left.Length; index++)
        {
            diff |= left[index] ^ right[index];
        }

        return diff == 0;
    }

    private sealed class RegisteredClient
    {
        public Guid ClientId { get; init; }
        public string ClientSecret { get; init; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string CurrentUser { get; set; } = string.Empty;
        public bool HasInteractiveUser { get; set; }
        public bool IsAtLogonScreen { get; set; }
        public string AgentVersion { get; set; } = "0.0.0.0";
        public int? BatteryPercentage { get; set; }
        public IReadOnlyList<DiskUsageDto> DiskUsages { get; set; } = [];
        public long? TotalMemoryBytes { get; set; }
        public long? AvailableMemoryBytes { get; set; }
        public string? OsDescription { get; set; }
        public DateTimeOffset? LastBootAtUtc { get; set; }
        public bool ConsentRequired { get; set; }
        public bool AutoApproveSupportRequests { get; set; }
        public bool TailscaleConnected { get; set; }
        public IReadOnlyList<string> TailscaleIpAddresses { get; set; } = [];
        public string? ReportedRustDeskId { get; set; }
        public string? RustDeskIdOverride { get; set; }
        public string? RustDeskPassword { get; set; }
        public string? RemoteUserName { get; set; }
        public string? RemotePassword { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTimeOffset RegisteredAtUtc { get; init; }
        public DateTimeOffset LastSeenAtUtc { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public IReadOnlyList<RemoteChannel> SupportedChannels { get; set; } = [];
        public SupportRequestDto? PendingSupportRequest { get; set; }
        public SupportSessionDto? ActiveSession { get; set; }

        public string? EffectiveRustDeskId => string.IsNullOrWhiteSpace(RustDeskIdOverride)
            ? ReportedRustDeskId
            : RustDeskIdOverride;

        public PersistedClientRecord ToPersisted()
        {
            return new PersistedClientRecord
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                DeviceName = DeviceName,
                MachineName = MachineName,
                CurrentUser = CurrentUser,
                HasInteractiveUser = HasInteractiveUser,
                IsAtLogonScreen = IsAtLogonScreen,
                AgentVersion = AgentVersion,
                BatteryPercentage = BatteryPercentage,
                DiskUsages = DiskUsages.ToList(),
                TotalMemoryBytes = TotalMemoryBytes,
                AvailableMemoryBytes = AvailableMemoryBytes,
                OsDescription = OsDescription,
                LastBootAtUtc = LastBootAtUtc,
                ConsentRequired = ConsentRequired,
                AutoApproveSupportRequests = AutoApproveSupportRequests,
                TailscaleConnected = TailscaleConnected,
                TailscaleIpAddresses = TailscaleIpAddresses.ToList(),
                RustDeskId = ReportedRustDeskId,
                RustDeskIdOverride = RustDeskIdOverride,
                Notes = Notes,
                RustDeskPassword = RustDeskPassword,
                RemoteUserName = RemoteUserName,
                RemotePassword = RemotePassword,
                RegisteredAtUtc = RegisteredAtUtc,
                LastSeenAtUtc = LastSeenAtUtc,
                StartedAtUtc = StartedAtUtc,
                SupportedChannels = SupportedChannels.Select(channel => channel.ToString()).ToList(),
                PendingSupportRequest = PendingSupportRequest is null ? null : new PersistedSupportRequest
                {
                    RequestId = PendingSupportRequest.RequestId,
                    AdminDisplayName = PendingSupportRequest.AdminDisplayName,
                    PreferredChannel = PendingSupportRequest.PreferredChannel.ToString(),
                    Reason = PendingSupportRequest.Reason,
                    RequestedAtUtc = PendingSupportRequest.RequestedAtUtc,
                    Status = PendingSupportRequest.Status
                },
                ActiveSession = ActiveSession is null ? null : new PersistedSupportSession
                {
                    SessionId = ActiveSession.SessionId,
                    RequestId = ActiveSession.RequestId,
                    AdminDisplayName = ActiveSession.AdminDisplayName,
                    Channel = ActiveSession.Channel.ToString(),
                    ApprovedAtUtc = ActiveSession.ApprovedAtUtc,
                    Status = ActiveSession.Status
                }
            };
        }

        public static RegisteredClient FromPersisted(PersistedClientRecord record)
        {
            return new RegisteredClient
            {
                ClientId = record.ClientId,
                ClientSecret = record.ClientSecret,
                DeviceName = record.DeviceName,
                MachineName = record.MachineName,
                CurrentUser = record.CurrentUser,
                HasInteractiveUser = record.HasInteractiveUser,
                IsAtLogonScreen = record.IsAtLogonScreen,
                AgentVersion = string.IsNullOrWhiteSpace(record.AgentVersion) ? "0.0.0.0" : record.AgentVersion,
                BatteryPercentage = record.BatteryPercentage,
                DiskUsages = NormalizeDiskUsages(record.DiskUsages),
                TotalMemoryBytes = NormalizeNullableBytes(record.TotalMemoryBytes),
                AvailableMemoryBytes = NormalizeAvailableBytes(record.TotalMemoryBytes, record.AvailableMemoryBytes),
                OsDescription = NormalizeNullableString(record.OsDescription),
                LastBootAtUtc = record.LastBootAtUtc,
                ConsentRequired = record.ConsentRequired,
                AutoApproveSupportRequests = record.AutoApproveSupportRequests,
                TailscaleConnected = record.TailscaleConnected,
                TailscaleIpAddresses = record.TailscaleIpAddresses
                    .Where(static address => !string.IsNullOrWhiteSpace(address))
                    .Select(static address => address.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ReportedRustDeskId = record.RustDeskId,
                RustDeskIdOverride = record.RustDeskIdOverride,
                Notes = record.Notes ?? string.Empty,
                RustDeskPassword = record.RustDeskPassword,
                RemoteUserName = record.RemoteUserName,
                RemotePassword = record.RemotePassword,
                RegisteredAtUtc = record.RegisteredAtUtc,
                LastSeenAtUtc = record.LastSeenAtUtc,
                StartedAtUtc = record.StartedAtUtc,
                SupportedChannels = record.SupportedChannels.Select(ParseRemoteChannel).Distinct().ToArray(),
                PendingSupportRequest = record.PendingSupportRequest is null ? null : new SupportRequestDto(
                    record.PendingSupportRequest.RequestId,
                    record.PendingSupportRequest.AdminDisplayName,
                    ParseRemoteChannel(record.PendingSupportRequest.PreferredChannel),
                    record.PendingSupportRequest.Reason,
                    record.PendingSupportRequest.RequestedAtUtc,
                    record.PendingSupportRequest.Status),
                ActiveSession = record.ActiveSession is null ? null : new SupportSessionDto(
                    record.ActiveSession.SessionId,
                    record.ActiveSession.RequestId,
                    record.ActiveSession.AdminDisplayName,
                    ParseRemoteChannel(record.ActiveSession.Channel),
                    record.ActiveSession.ApprovedAtUtc,
                    record.ActiveSession.Status)
            };
        }

        private static RemoteChannel ParseRemoteChannel(string value)
        {
            return Enum.TryParse<RemoteChannel>(value, ignoreCase: true, out var channel)
                ? channel
                : RemoteChannel.WinRm;
        }
    }
}
