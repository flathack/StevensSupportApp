using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class ConsentTimeoutTests
{
    private static ClientRegistry CreateRegistryWithConsentTimeout(string stateFilePath, int consentTimeoutMinutes)
    {
        var storageOptions = Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = stateFilePath,
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024,
            SessionTimeoutMinutes = 60,
            ConsentTimeoutMinutes = consentTimeoutMinutes
        });
        var registrationOptions = Microsoft.Extensions.Options.Options.Create(new ClientRegistrationOptions
        {
            RequireSignedRegistration = true,
            SharedKey = "test-registration-key",
            AllowedClockSkewMinutes = 5
        });

        return new ClientRegistry(
            new ServerStateStore(storageOptions),
            storageOptions,
            new ClientRegistrationVerifier(registrationOptions));
    }

    [Fact]
    public void GetSupportState_RequestWithinTimeout_RemainsPending()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var registry = CreateRegistryWithConsentTimeout(Path.Combine(dir, "state.json"), 5);
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");

        var supportState = registry.GetSupportState(new GetSupportStateRequest(client.ClientId, client.ClientSecret));

        Assert.NotNull(supportState.PendingSupportRequest);
        Assert.Equal(request.RequestId, supportState.PendingSupportRequest!.RequestId);
    }

    [Fact]
    public void GetClients_ExpiredRequest_IsClearedAndAudited()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stateFile = Path.Combine(dir, "state.json");

        var registry = CreateRegistryWithConsentTimeout(stateFile, 5);
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));
        registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");

        var store = new ServerStateStore(Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = stateFile,
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024,
            SessionTimeoutMinutes = 60,
            ConsentTimeoutMinutes = 1
        }));

        var state = store.Load();
        state.Clients[0].PendingSupportRequest = new PersistedSupportRequest
        {
            RequestId = state.Clients[0].PendingSupportRequest!.RequestId,
            AdminDisplayName = state.Clients[0].PendingSupportRequest!.AdminDisplayName,
            PreferredChannel = state.Clients[0].PendingSupportRequest!.PreferredChannel,
            Reason = state.Clients[0].PendingSupportRequest!.Reason,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = state.Clients[0].PendingSupportRequest!.Status
        };
        store.Save(state);

        var reloadedRegistry = CreateRegistryWithConsentTimeout(stateFile, 1);
        var clients = reloadedRegistry.GetClients();
        var audit = reloadedRegistry.GetAuditEntries(20);

        Assert.Null(clients[0].PendingSupportRequest);
        Assert.Contains(audit, entry => entry.EventType == "SupportRequestExpired");
    }

    [Fact]
    public void CreateSupportRequest_AfterPreviousRequestExpired_AllowsNewRequest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stateFile = Path.Combine(dir, "state.json");

        var registry = CreateRegistryWithConsentTimeout(stateFile, 5);
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));
        registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Old request"), "Admin");

        var store = new ServerStateStore(Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = stateFile,
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024,
            SessionTimeoutMinutes = 60,
            ConsentTimeoutMinutes = 1
        }));

        var state = store.Load();
        state.Clients[0].PendingSupportRequest = new PersistedSupportRequest
        {
            RequestId = state.Clients[0].PendingSupportRequest!.RequestId,
            AdminDisplayName = state.Clients[0].PendingSupportRequest!.AdminDisplayName,
            PreferredChannel = state.Clients[0].PendingSupportRequest!.PreferredChannel,
            Reason = state.Clients[0].PendingSupportRequest!.Reason,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = state.Clients[0].PendingSupportRequest!.Status
        };
        store.Save(state);

        var reloadedRegistry = CreateRegistryWithConsentTimeout(stateFile, 1);
        var replacement = reloadedRegistry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "New request"), "Admin");

        Assert.Equal("PendingClientConsent", replacement.Status);
    }
}
