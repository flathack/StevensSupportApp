using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class SupportSessionTests
{
    private static (Services.ClientRegistry Registry, RegisterClientResponse Client) SetupWithClient()
    {
        var registry = TestHelpers.CreateRegistry();
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01", supportedChannels: [RemoteChannel.WinRm, RemoteChannel.Rdp]));
        // Send heartbeat so client is online
        registry.Heartbeat(new ClientHeartbeatRequest(
            client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm, RemoteChannel.Rdp]));
        return (registry, client);
    }

    [Fact]
    public void CreateSupportRequest_ValidClient_Succeeds()
    {
        var (registry, client) = SetupWithClient();

        var response = registry.CreateSupportRequest(
            client.ClientId,
            new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test support"),
            "Admin");

        Assert.Equal("PendingClientConsent", response.Status);
        Assert.NotEqual(Guid.Empty, response.RequestId);
    }

    [Fact]
    public void CreateSupportRequest_UnknownClient_Throws()
    {
        var (registry, _) = SetupWithClient();

        Assert.Throws<KeyNotFoundException>(() =>
            registry.CreateSupportRequest(
                Guid.NewGuid(),
                new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"),
                "Admin"));
    }

    [Fact]
    public void CreateSupportRequest_DuplicateRequest_Throws()
    {
        var (registry, client) = SetupWithClient();
        registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");

        Assert.Throws<InvalidOperationException>(() =>
            registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Again"), "Admin"));
    }

    [Fact]
    public void CreateSupportRequest_UnsupportedChannel_Throws()
    {
        var registry = TestHelpers.CreateRegistry();
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));
        registry.Heartbeat(new ClientHeartbeatRequest(client.ClientId, client.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));

        Assert.Throws<InvalidOperationException>(() =>
            registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.Rdp, "Test"), "Admin"));
    }

    [Fact]
    public void CreateSupportRequest_RustDeskChannelWhenAdvertised_Succeeds()
    {
        var registry = TestHelpers.CreateRegistry();
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(
            deviceName: "Dev",
            machineName: "PC01",
            supportedChannels: [RemoteChannel.WinRm, RemoteChannel.RustDesk],
            rustDeskId: "123-456-789"));
        registry.Heartbeat(new ClientHeartbeatRequest(
            client.ClientId,
            client.ClientSecret,
            "User1",
            true,
            false,
            "1.0.0",
            null,
            true,
            false,
            false,
            [],
            DateTimeOffset.UtcNow,
            [RemoteChannel.WinRm, RemoteChannel.RustDesk],
            "123-456-789"));

        var response = registry.CreateSupportRequest(
            client.ClientId,
            new CreateSupportRequestRequest("Admin", RemoteChannel.RustDesk, "RustDesk support"),
            "Admin");

        Assert.Equal("PendingClientConsent", response.Status);
    }

    [Fact]
    public void CreateSupportRequest_AutoApproveEnabled_StartsSessionImmediately()
    {
        var registry = TestHelpers.CreateRegistry();
        var client = registry.Register(TestHelpers.CreateSignedRegistrationRequest(
            deviceName: "Server01",
            machineName: "SERVER01",
            autoApproveSupportRequests: true,
            supportedChannels: [RemoteChannel.WinRm]));
        registry.Heartbeat(new ClientHeartbeatRequest(
            client.ClientId,
            client.ClientSecret,
            "System",
            false,
            true,
            "1.0.0",
            null,
            false,
            true,
            false,
            [],
            DateTimeOffset.UtcNow,
            [RemoteChannel.WinRm]));

        var response = registry.CreateSupportRequest(
            client.ClientId,
            new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Unattended support"),
            "Admin");

        var summary = Assert.Single(registry.GetClients());
        Assert.Equal("Approved", response.Status);
        Assert.Null(summary.PendingSupportRequest);
        Assert.NotNull(summary.ActiveSession);
        Assert.Equal(RemoteChannel.WinRm, summary.ActiveSession!.Channel);
    }

    [Fact]
    public void SubmitDecision_Approve_CreatesSession()
    {
        var (registry, client) = SetupWithClient();
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");

        var decision = registry.SubmitSupportDecision(
            request.RequestId,
            new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));

        Assert.Equal("Approved", decision.SupportRequest.Status);
        Assert.NotNull(decision.ActiveSession);
        Assert.Equal("Active", decision.ActiveSession!.Status);
    }

    [Fact]
    public void SubmitDecision_Deny_NoSession()
    {
        var (registry, client) = SetupWithClient();
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");

        var decision = registry.SubmitSupportDecision(
            request.RequestId,
            new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, false));

        Assert.Equal("Denied", decision.SupportRequest.Status);
        Assert.Null(decision.ActiveSession);
    }

    [Fact]
    public void CreateSupportRequest_WhileSessionActive_Throws()
    {
        var (registry, client) = SetupWithClient();
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");
        registry.SubmitSupportDecision(request.RequestId, new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));

        Assert.Throws<InvalidOperationException>(() =>
            registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Second"), "Admin"));
    }

    [Fact]
    public void EndActiveSession_Succeeds()
    {
        var (registry, client) = SetupWithClient();
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Test"), "Admin");
        registry.SubmitSupportDecision(request.RequestId, new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));

        var ended = registry.EndActiveSession(client.ClientId, "Admin");

        Assert.Equal("Ended", ended.Status);

        // Client should have no active session now
        var clients = registry.GetClients();
        Assert.Null(clients[0].ActiveSession);
    }

    [Fact]
    public void EndActiveSession_NoSession_Throws()
    {
        var (registry, client) = SetupWithClient();

        Assert.Throws<InvalidOperationException>(() =>
            registry.EndActiveSession(client.ClientId, "Admin"));
    }

    [Fact]
    public void FullSessionLifecycle_AuditTrail()
    {
        var (registry, client) = SetupWithClient();
        var request = registry.CreateSupportRequest(client.ClientId, new CreateSupportRequestRequest("Admin", RemoteChannel.WinRm, "Lifecycle test"), "Admin");
        registry.SubmitSupportDecision(request.RequestId, new SubmitSupportDecisionRequest(client.ClientId, client.ClientSecret, true));
        registry.EndActiveSession(client.ClientId, "Admin");

        var audit = registry.GetAuditEntries(50);
        var eventTypes = audit.Select(e => e.EventType).ToList();

        Assert.Contains("ClientRegistered", eventTypes);
        Assert.Contains("SupportRequested", eventTypes);
        Assert.Contains("SupportApproved", eventTypes);
        Assert.Contains("SessionEnded", eventTypes);
    }
}
