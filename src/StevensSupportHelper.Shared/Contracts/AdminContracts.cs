namespace StevensSupportHelper.Shared.Contracts;

public sealed record AdminClientSummary(
    Guid ClientId,
    string DeviceName,
    string MachineName,
    string CurrentUser,
    bool HasInteractiveUser,
    bool IsAtLogonScreen,
    int? BatteryPercentage,
    IReadOnlyList<DiskUsageDto> DiskUsages,
    long? TotalMemoryBytes,
    long? AvailableMemoryBytes,
    string? OsDescription,
    DateTimeOffset? LastBootAtUtc,
    bool IsOnline,
    bool ConsentRequired,
    bool AutoApproveSupportRequests,
    bool TailscaleConnected,
    IReadOnlyList<string> TailscaleIpAddresses,
    string AgentVersion,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastSeenAtUtc,
    IReadOnlyList<RemoteChannel> SupportedChannels,
    SupportRequestDto? PendingSupportRequest,
    SupportSessionDto? ActiveSession,
    int UnreadClientChatCount,
    DateTimeOffset? LastClientChatMessageAtUtc,
    string? RustDeskId = null,
    string Notes = "",
    string? RustDeskPassword = null,
    string? RemoteUserName = null,
    string? RemotePassword = null);

public sealed record AdminSessionInfoResponse(
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool RequiresMfa);

public sealed record CreateSupportRequestRequest(
    string AdminDisplayName,
    RemoteChannel PreferredChannel,
    string Reason);

public sealed record UpdateAdminClientMetadataRequest(
    string? Notes,
    string? RustDeskId,
    string? RustDeskPassword,
    string? RemoteUserName,
    string? RemotePassword);

public sealed record CreateSupportRequestResponse(
    Guid RequestId,
    string Status,
    string Message);

public sealed record EndActiveSessionResponse(
    Guid SessionId,
    string Status,
    string Message);

public sealed record AuditEntryDto(
    Guid AuditEntryId,
    string EventType,
    Guid? ClientId,
    string DeviceName,
    string Actor,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record QueueFileUploadRequest(
    string FileName,
    string TargetRelativePath,
    string ContentBase64);

public sealed record QueueFileDownloadRequest(
    string SourceRelativePath);

public sealed record QueueFileTransferResponse(
    Guid TransferId,
    string Status,
    string Message);

public sealed record FileTransferContentResponse(
    Guid TransferId,
    string FileName,
    string Status,
    string ContentBase64);

public sealed record QueueAgentJobResponse(
    Guid JobId,
    string Status,
    string Message);

public sealed record SendAdminChatMessageRequest(
    string Message);
