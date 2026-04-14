using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class HeartbeatTests
{
    [Fact]
    public void Heartbeat_RegisteredClient_Succeeds()
    {
        var registry = TestHelpers.CreateRegistry();
        var reg = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));

        var response = registry.Heartbeat(new ClientHeartbeatRequest(
            reg.ClientId, reg.ClientSecret, "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm]));

        Assert.True(response.NextHeartbeatIntervalSeconds > 0);
        Assert.NotEqual(default, response.ServerTimeUtc);
    }

    [Fact]
    public void Heartbeat_WrongSecret_Throws()
    {
        var registry = TestHelpers.CreateRegistry();
        var reg = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Heartbeat(new ClientHeartbeatRequest(
                reg.ClientId, "wrong-secret", "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm])));
    }

    [Fact]
    public void Heartbeat_UnknownClient_Throws()
    {
        var registry = TestHelpers.CreateRegistry();

        Assert.Throws<InvalidOperationException>(() =>
            registry.Heartbeat(new ClientHeartbeatRequest(
                Guid.NewGuid(), "any-secret", "User1", true, false, "1.0.0", null, true, false, false, [], DateTimeOffset.UtcNow, [RemoteChannel.WinRm])));
    }

    [Fact]
    public void Heartbeat_UpdatesClientDetails()
    {
        var registry = TestHelpers.CreateRegistry();
        var reg = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Dev", machineName: "PC01"));

        registry.Heartbeat(new ClientHeartbeatRequest(
            reg.ClientId, reg.ClientSecret, "User2", true, false, "1.1.0", 82, true, false, true, ["100.64.0.12"], DateTimeOffset.UtcNow, [RemoteChannel.WinRm, RemoteChannel.Rdp]));

        var clients = registry.GetClients();
        Assert.Equal("User2", clients[0].CurrentUser);
        Assert.Equal("1.1.0", clients[0].AgentVersion);
        Assert.Equal(82, clients[0].BatteryPercentage);
        Assert.True(clients[0].TailscaleConnected);
        Assert.Contains(RemoteChannel.Rdp, clients[0].SupportedChannels);
    }
}
