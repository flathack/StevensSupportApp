using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Services;

public sealed class ServerStateStore
{
    private const string JsonProvider = "Json";
    private const string SqliteProvider = "Sqlite";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _provider;
    private readonly string _stateFilePath;
    private readonly string _databasePath;
    public string StorageRootPath { get; }

    public ServerStateStore(IOptions<ServerStorageOptions> options)
    {
        var configuredOptions = options.Value;
        _provider = string.IsNullOrWhiteSpace(configuredOptions.Provider)
            ? JsonProvider
            : configuredOptions.Provider.Trim();
        _stateFilePath = Environment.ExpandEnvironmentVariables(configuredOptions.StateFilePath);
        _databasePath = Environment.ExpandEnvironmentVariables(configuredOptions.DatabasePath);
        StorageRootPath = ResolveStorageRoot(_databasePath, _stateFilePath);

        EnsureParentDirectory(_stateFilePath);
        EnsureParentDirectory(_databasePath);
        Directory.CreateDirectory(StorageRootPath);
    }

    public PersistedServerState Load()
    {
        if (string.Equals(_provider, SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            return LoadFromSqlite();
        }

        return LoadFromJson();
    }

    public void Save(PersistedServerState state)
    {
        if (string.Equals(_provider, SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            SaveToSqlite(state);
            return;
        }

        SaveToJson(state);
    }

    private PersistedServerState LoadFromJson()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new PersistedServerState();
        }

        using var stream = File.OpenRead(_stateFilePath);
        return JsonSerializer.Deserialize<PersistedServerState>(stream, JsonOptions) ?? new PersistedServerState();
    }

    private void SaveToJson(PersistedServerState state)
    {
        var tempFilePath = _stateFilePath + ".tmp";
        using (var stream = File.Create(tempFilePath))
        {
            JsonSerializer.Serialize(stream, state, JsonOptions);
        }

        File.Move(tempFilePath, _stateFilePath, overwrite: true);
    }

    private PersistedServerState LoadFromSqlite()
    {
        using var connection = CreateConnection();
        EnsureSqliteSchema(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state_json FROM server_state WHERE id = 1;";
        var payload = command.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            return JsonSerializer.Deserialize<PersistedServerState>(payload, JsonOptions) ?? new PersistedServerState();
        }

        var migratedState = TryMigrateJsonState();
        if (migratedState is not null)
        {
            SaveToSqlite(connection, migratedState);
            return migratedState;
        }

        return new PersistedServerState();
    }

    private void SaveToSqlite(PersistedServerState state)
    {
        using var connection = CreateConnection();
        EnsureSqliteSchema(connection);
        SaveToSqlite(connection, state);
    }

    private void SaveToSqlite(SqliteConnection connection, PersistedServerState state)
    {
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO server_state (id, state_json, updated_at_utc)
            VALUES (1, $stateJson, $updatedAtUtc)
            ON CONFLICT(id) DO UPDATE SET
                state_json = excluded.state_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$stateJson", JsonSerializer.Serialize(state, JsonOptions));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    private PersistedServerState? TryMigrateJsonState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        var migrated = LoadFromJson();
        var backupPath = _stateFilePath + ".migrated";
        File.Move(_stateFilePath, backupPath, overwrite: true);
        return migrated;
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static void EnsureSqliteSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS server_state (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                state_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string ResolveStorageRoot(string databasePath, string stateFilePath)
    {
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            return databaseDirectory;
        }

        var stateDirectory = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrWhiteSpace(stateDirectory))
        {
            return stateDirectory;
        }

        return AppContext.BaseDirectory;
    }
}

public sealed class PersistedServerState
{
    public List<PersistedClientRecord> Clients { get; init; } = [];
    public List<PersistedAuditEntry> AuditEntries { get; init; } = [];
    public List<PersistedFileTransferRecord> FileTransfers { get; init; } = [];
    public List<PersistedAgentJobRecord> AgentJobs { get; init; } = [];
    public List<PersistedChatMessageRecord> ChatMessages { get; init; } = [];
    public List<PersistedUserRecord> Users { get; set; } = [];
    public bool HardcodedSuperAdminEnabled { get; set; } = true;
    public PersistedDeploymentSettings DeploymentSettings { get; set; } = new();
    public List<PersistedDeploymentAsset> DeploymentAssets { get; set; } = [];
    public List<PersistedDeploymentProfile> DeploymentProfiles { get; set; } = [];
}

public sealed class PersistedDeploymentSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = string.Empty;
    public string ServerProjectPath { get; set; } = string.Empty;
    public string RustDeskPath { get; set; } = string.Empty;
    public string RustDeskPassword { get; set; } = string.Empty;
    public string ClientInstallerPath { get; set; } = string.Empty;
    public string RemoteActionsPath { get; set; } = string.Empty;
    public string PackageGeneratorPath { get; set; } = string.Empty;
    public string RemoteUserName { get; set; } = string.Empty;
    public string RemotePassword { get; set; } = string.Empty;
    public string PreferredChannel { get; set; } = "Rdp";
    public string Reason { get; set; } = "Remote support requested.";
    public string DefaultRegistrationSharedKey { get; set; } = string.Empty;
    public string DefaultInstallRoot { get; set; } = @"C:\Program Files\StevensSupportHelper";
    public string DefaultServiceName { get; set; } = "StevensSupportHelperClientService";
}

public sealed class PersistedDeploymentAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PersistedDeploymentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerName { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public string RegistrationSharedKey { get; set; } = string.Empty;
    public string InstallRoot { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public bool InstallRustDesk { get; set; } = true;
    public bool InstallTailscale { get; set; } = true;
    public string TailscaleAuthKey { get; set; } = string.Empty;
    public bool EnableAutoApprove { get; set; } = true;
    public bool EnableRdp { get; set; } = true;
    public bool CreateServiceUser { get; set; }
    public bool ServiceUserIsAdministrator { get; set; } = true;
    public string ServiceUserName { get; set; } = string.Empty;
    public string ServiceUserPassword { get; set; } = string.Empty;
    public string RustDeskId { get; set; } = string.Empty;
    public string RustDeskPassword { get; set; } = string.Empty;
    public List<string> TailscaleIpAddresses { get; set; } = [];
    public bool Silent { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PersistedUserRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public string? TotpSecret { get; set; }
    public bool IsMfaEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PersistedClientRecord
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
    public List<DiskUsageDto> DiskUsages { get; set; } = [];
    public long? TotalMemoryBytes { get; set; }
    public long? AvailableMemoryBytes { get; set; }
    public string? OsDescription { get; set; }
    public DateTimeOffset? LastBootAtUtc { get; set; }
    public bool ConsentRequired { get; set; }
    public bool AutoApproveSupportRequests { get; set; }
    public bool TailscaleConnected { get; set; }
    public List<string> TailscaleIpAddresses { get; set; } = [];
    public string? RustDeskId { get; set; }
    public string? RustDeskIdOverride { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? RustDeskPassword { get; set; }
    public string? RemoteUserName { get; set; }
    public string? RemotePassword { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public List<string> SupportedChannels { get; set; } = [];
    public PersistedSupportRequest? PendingSupportRequest { get; set; }
    public PersistedSupportSession? ActiveSession { get; set; }
}

public sealed class PersistedSupportRequest
{
    public Guid RequestId { get; init; }
    public string AdminDisplayName { get; init; } = string.Empty;
    public string PreferredChannel { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class PersistedSupportSession
{
    public Guid SessionId { get; init; }
    public Guid RequestId { get; init; }
    public string AdminDisplayName { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class PersistedAuditEntry
{
    public Guid AuditEntryId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public Guid? ClientId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class PersistedFileTransferRecord
{
    public Guid TransferId { get; init; }
    public Guid ClientId { get; init; }
    public Guid SessionId { get; init; }
    public string Direction { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ContentBase64 { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class PersistedAgentJobRecord
{
    public Guid JobId { get; init; }
    public Guid ClientId { get; init; }
    public string JobType { get; init; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? RequestJson { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class PersistedChatMessageRecord
{
    public Guid MessageId { get; init; }
    public Guid ClientId { get; init; }
    public string SenderRole { get; init; } = string.Empty;
    public string SenderDisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}
