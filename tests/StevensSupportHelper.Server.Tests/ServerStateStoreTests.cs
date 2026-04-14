using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;

namespace StevensSupportHelper.Server.Tests;

public sealed class ServerStateStoreTests
{
    [Fact]
    public void SaveAndLoad_WithSqliteProvider_RoundTripsState()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var store = new ServerStateStore(Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            Provider = "Sqlite",
            StateFilePath = Path.Combine(dir, "state.json"),
            DatabasePath = Path.Combine(dir, "state.db")
        }));

        var expected = new PersistedServerState
        {
            Clients =
            [
                new PersistedClientRecord
                {
                    ClientId = Guid.NewGuid(),
                    ClientSecret = "secret",
                    DeviceName = "DeviceA",
                    MachineName = "PC01",
                    CurrentUser = "User1",
                    ConsentRequired = true,
                    TailscaleConnected = true,
                    RegisteredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    LastSeenAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
                    SupportedChannels = ["WinRm", "Rdp"]
                }
            ],
            AuditEntries =
            [
                new PersistedAuditEntry
                {
                    AuditEntryId = Guid.NewGuid(),
                    EventType = "ClientRegistered",
                    ClientId = null,
                    DeviceName = "DeviceA",
                    Actor = "System",
                    Message = "Registered",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                }
            ],
            FileTransfers =
            [
                new PersistedFileTransferRecord
                {
                    TransferId = Guid.NewGuid(),
                    ClientId = Guid.NewGuid(),
                    SessionId = Guid.NewGuid(),
                    Direction = "AdminToClient",
                    RelativePath = "folder\\file.txt",
                    FileName = "file.txt",
                    Status = "PendingClientProcessing",
                    RequestedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        store.Save(expected);
        var loaded = store.Load();

        Assert.Single(loaded.Clients);
        Assert.Equal(expected.Clients[0].DeviceName, loaded.Clients[0].DeviceName);
        Assert.Equal(expected.Clients[0].SupportedChannels, loaded.Clients[0].SupportedChannels);
        Assert.Single(loaded.AuditEntries);
        Assert.Equal(expected.AuditEntries[0].EventType, loaded.AuditEntries[0].EventType);
        Assert.Single(loaded.FileTransfers);
        Assert.Equal(expected.FileTransfers[0].RelativePath, loaded.FileTransfers[0].RelativePath);
    }

    [Fact]
    public void Load_WithSqliteProvider_MigratesExistingJsonState()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "state.json");
        var dbPath = Path.Combine(dir, "state.db");

        var jsonStore = new ServerStateStore(Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            Provider = "Json",
            StateFilePath = jsonPath,
            DatabasePath = dbPath
        }));
        jsonStore.Save(new PersistedServerState
        {
            Clients =
            [
                new PersistedClientRecord
                {
                    ClientId = Guid.NewGuid(),
                    ClientSecret = "secret",
                    DeviceName = "MigratedDevice",
                    MachineName = "PC02",
                    CurrentUser = "User2",
                    RegisteredAtUtc = DateTimeOffset.UtcNow,
                    LastSeenAtUtc = DateTimeOffset.UtcNow,
                    StartedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        });

        var sqliteStore = new ServerStateStore(Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            Provider = "Sqlite",
            StateFilePath = jsonPath,
            DatabasePath = dbPath
        }));

        var migrated = sqliteStore.Load();
        var reloaded = sqliteStore.Load();

        Assert.Single(migrated.Clients);
        Assert.Equal("MigratedDevice", migrated.Clients[0].DeviceName);
        Assert.Single(reloaded.Clients);
        Assert.False(File.Exists(jsonPath));
        Assert.True(File.Exists(jsonPath + ".migrated"));
        Assert.True(File.Exists(dbPath));
    }
}
