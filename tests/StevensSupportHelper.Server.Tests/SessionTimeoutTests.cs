using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class SessionTimeoutTests
{
    private static ClientRegistry CreateRegistryWithTimeout(int timeoutMinutes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var storageOptions = Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = Path.Combine(dir, "state.json"),
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024,
            SessionTimeoutMinutes = timeoutMinutes
        });
        var registrationOptions = Microsoft.Extensions.Options.Options.Create(new ClientRegistrationOptions
        {
            RequireSignedRegistration = true,
            SharedKey = "test-registration-key",
            AllowedClockSkewMinutes = 5
        });

        var store = new ServerStateStore(storageOptions);
        return new ClientRegistry(store, storageOptions, new ClientRegistrationVerifier(registrationOptions));
    }

    [Fact]
    public void GetClients_SessionWithinTimeout_SessionRemains()
    {
        // Use 60-minute timeout - session just created should not expire
        var registry = CreateRegistryWithTimeout(60);
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");
        registry.SubmitSupportDecision(request.RequestId, new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));

        var clients = registry.GetClients();

        Assert.NotNull(clients[0].ActiveSession);
        Assert.Equal("Active", clients[0].ActiveSession!.Status);
    }

    [Fact]
    public void GetClients_SessionExceedsTimeout_SessionExpired()
    {
        // Use 1-minute timeout configured, but we can't easily time-travel.
        // Instead, verify that the timeout config value is respected by checking
        // the audit trail after calling GetClients with a very short timeout
        // and a session created "in the past" by persisting and reloading.
        var dir = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stateFile = Path.Combine(dir, "state.json");

        // Step 1: Create a registry and set up an active session
        var storageOpts1 = Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = stateFile,
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024,
            SessionTimeoutMinutes = 60
        });

        var store1 = new ServerStateStore(storageOpts1);
        var registrationOptions1 = Microsoft.Extensions.Options.Options.Create(new ClientRegistrationOptions
        {
            RequireSignedRegistration = true,
            SharedKey = "test-registration-key",
            AllowedClockSkewMinutes = 5
        });
        var registry1 = new ClientRegistry(store1, storageOpts1, new ClientRegistrationVerifier(registrationOptions1));

        var client = registry1.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry1.Heartbeat(new ClientHeartbeatRequest(client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));
        var request = registry1.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");
        registry1.SubmitSupportDecision(request.RequestId, new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));

        // Verify session is active
        var clientsBefore = registry1.GetClients();
        Assert.NotNull(clientsBefore[0].ActiveSession);

        // Step 2: Manually manipulate the persisted state to set ApprovedAtUtc to 2 hours ago
        var state = store1.Load();
        var persistedClient = state.Clients[0];
        Assert.NotNull(persistedClient.ActiveSession);
        persistedClient.ActiveSession = new PersistedSupportSession
        {
            SessionId = persistedClient.ActiveSession.SessionId,
            RequestId = persistedClient.ActiveSession.RequestId,
            AdminDisplayName = persistedClient.ActiveSession.AdminDisplayName,
            Channel = persistedClient.ActiveSession.Channel,
            ApprovedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            Status = persistedClient.ActiveSession.Status
        };
        store1.Save(state);

        // Step 3: Create a new registry with 1-minute timeout, loading the manipulated state
        var storageOpts2 = Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = stateFile,
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024,
            SessionTimeoutMinutes = 1
        });

        var store2 = new ServerStateStore(storageOpts2);
        var registrationOptions2 = Microsoft.Extensions.Options.Options.Create(new ClientRegistrationOptions
        {
            RequireSignedRegistration = true,
            SharedKey = "test-registration-key",
            AllowedClockSkewMinutes = 5
        });
        var registry2 = new ClientRegistry(store2, storageOpts2, new ClientRegistrationVerifier(registrationOptions2));

        // Step 4: GetClients should trigger expiry
        var clientsAfter = registry2.GetClients();

        Assert.Null(clientsAfter[0].ActiveSession);

        // Verify the audit trail contains the expiry event
        var audit = registry2.GetAuditEntries(50);
        Assert.Contains(audit, e => e.EventType == "SessionExpired");
    }
}
