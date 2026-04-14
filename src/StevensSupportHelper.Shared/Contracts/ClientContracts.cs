namespace StevensSupportHelper.Shared.Contracts;

public sealed record DiskUsageDto(
    string DriveName,
    long TotalBytes,
    long FreeBytes);

public sealed record RegisterClientRequest(
    string DeviceName,
    string MachineName,
    string CurrentUser,
    bool HasInteractiveUser,
    bool IsAtLogonScreen,
    string AgentVersion,
    int? BatteryPercentage,
    bool ConsentRequired,
    bool AutoApproveSupportRequests,
    bool TailscaleConnected,
    IReadOnlyList<string>? TailscaleIpAddresses,
    IReadOnlyList<RemoteChannel> SupportedChannels,
    DateTimeOffset RequestedAtUtc,
    string RegistrationNonce,
    string RegistrationSignature,
    string? RustDeskId = null,
    IReadOnlyList<DiskUsageDto>? DiskUsages = null,
    long? TotalMemoryBytes = null,
    long? AvailableMemoryBytes = null,
    string? OsDescription = null,
    DateTimeOffset? LastBootAtUtc = null);

public sealed record RegisterClientResponse(
    Guid ClientId,
    string ClientSecret,
    int HeartbeatIntervalSeconds);

public sealed record ClientHeartbeatRequest(
    Guid ClientId,
    string ClientSecret,
    string CurrentUser,
    bool HasInteractiveUser,
    bool IsAtLogonScreen,
    string AgentVersion,
    int? BatteryPercentage,
    bool ConsentRequired,
    bool AutoApproveSupportRequests,
    bool TailscaleConnected,
    IReadOnlyList<string>? TailscaleIpAddresses,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<RemoteChannel> SupportedChannels,
    string? RustDeskId = null,
    IReadOnlyList<DiskUsageDto>? DiskUsages = null,
    long? TotalMemoryBytes = null,
    long? AvailableMemoryBytes = null,
    string? OsDescription = null,
    DateTimeOffset? LastBootAtUtc = null);

public sealed record ClientHeartbeatResponse(
    DateTimeOffset ServerTimeUtc,
    int NextHeartbeatIntervalSeconds,
    SupportRequestDto? PendingSupportRequest,
    SupportSessionDto? ActiveSession,
    IReadOnlyList<FileTransferDto> PendingFileTransfers,
    IReadOnlyList<AgentJobDto> PendingAgentJobs);

public sealed record GetSupportStateRequest(
    Guid ClientId,
    string ClientSecret);

public sealed record GetSupportStateResponse(
    SupportRequestDto? PendingSupportRequest,
    SupportSessionDto? ActiveSession,
    IReadOnlyList<ChatMessageDto> ChatMessages);

public sealed record ChatMessageDto(
    Guid MessageId,
    Guid ClientId,
    string SenderRole,
    string SenderDisplayName,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record SendClientChatMessageRequest(
    Guid ClientId,
    string ClientSecret,
    string Message,
    string SenderDisplayName);

public sealed record SubmitSupportDecisionRequest(
    Guid ClientId,
    string ClientSecret,
    bool Approved);

public sealed record SubmitSupportDecisionResponse(
    SupportRequestDto SupportRequest,
    SupportSessionDto? ActiveSession);

public enum FileTransferDirection
{
    AdminToClient,
    ClientToAdmin
}

public sealed record FileTransferDto(
    Guid TransferId,
    Guid ClientId,
    Guid SessionId,
    FileTransferDirection Direction,
    string RelativePath,
    string FileName,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ContentBase64,
    string? ErrorMessage);

public sealed record CompleteFileTransferRequest(
    Guid ClientId,
    string ClientSecret,
    bool Success,
    string? ErrorMessage,
    string? ContentBase64);

public sealed record CompleteFileTransferResponse(
    Guid TransferId,
    string Status,
    string Message);

public enum AgentJobType
{
    ProcessSnapshot,
    WindowsUpdateScan,
    WindowsUpdateInstall,
    RegistrySnapshot,
    ServiceSnapshot,
    ServiceControl,
    ScriptExecution,
    PowerPlanSnapshot,
    PowerPlanActivate
}

public sealed record AgentJobDto(
    Guid JobId,
    Guid ClientId,
    AgentJobType JobType,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? RequestJson,
    string? ResultJson,
    string? ErrorMessage);

public sealed record CompleteAgentJobRequest(
    Guid ClientId,
    string ClientSecret,
    bool Success,
    string? ResultJson,
    string? ErrorMessage);

public sealed record CompleteAgentJobResponse(
    Guid JobId,
    string Status,
    string Message);

public sealed record AgentProcessInfoDto(
    int Id,
    string ProcessName,
    string MainWindowTitle,
    double? CpuSeconds,
    double WorkingSetMb,
    DateTimeOffset? StartTimeUtc);

public sealed record AgentSystemSummaryDto(
    int ProcessCount,
    double CpuPercent,
    double UsedMemoryGb,
    double TotalMemoryGb,
    double MemoryPercent);

public sealed record AgentProcessSnapshotResult(
    IReadOnlyList<AgentProcessInfoDto> Processes,
    AgentSystemSummaryDto Summary);

public sealed record AgentWindowsUpdateItemDto(
    string Title,
    string KbArticleIds,
    string Categories,
    bool IsDownloaded,
    long MaxDownloadSizeBytes);

public sealed record AgentWindowsUpdateScanResult(
    IReadOnlyList<AgentWindowsUpdateItemDto> Updates);

public sealed record AgentWindowsUpdateInstallResult(
    string Message);

public sealed record AgentRegistrySnapshotRequest(
    string RegistryPath);

public sealed record AgentRegistryEntryDto(
    string Name,
    string Kind,
    string Value);

public sealed record AgentRegistrySnapshotResult(
    IReadOnlyList<string> SubKeys,
    IReadOnlyList<AgentRegistryEntryDto> Values);

public sealed record AgentServiceInfoDto(
    string Name,
    string DisplayName,
    string Status,
    string StartType,
    bool CanStop);

public sealed record AgentServiceSnapshotResult(
    IReadOnlyList<AgentServiceInfoDto> Services);

public sealed record AgentServiceControlRequest(
    string ServiceName,
    string Action);

public sealed record AgentServiceControlResult(
    string Message);

public sealed record AgentScriptExecutionRequest(
    string ScriptContent,
    string DeviceName,
    string MachineName,
    string CurrentUser,
    string AgentVersion,
    string? RustDeskId,
    string Notes,
    IReadOnlyList<string> TailscaleIpAddresses);

public sealed record AgentScriptExecutionResult(
    string Output,
    string ErrorOutput,
    int ExitCode,
    string HostApplication);

public sealed record AgentPowerPlanDto(
    string Guid,
    string Name,
    bool IsActive);

public sealed record AgentPowerPlanSnapshotResult(
    IReadOnlyList<AgentPowerPlanDto> Plans);

public sealed record AgentPowerPlanActivateRequest(
    string Guid);

public sealed record AgentPowerPlanActivateResult(
    string Message);

public sealed record SupportRequestDto(
    Guid RequestId,
    string AdminDisplayName,
    RemoteChannel PreferredChannel,
    string Reason,
    DateTimeOffset RequestedAtUtc,
    string Status);

public sealed record SupportSessionDto(
    Guid SessionId,
    Guid RequestId,
    string AdminDisplayName,
    RemoteChannel Channel,
    DateTimeOffset ApprovedAtUtc,
    string Status);
