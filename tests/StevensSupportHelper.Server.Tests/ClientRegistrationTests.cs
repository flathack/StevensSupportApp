using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class ClientRegistrationTests
{
    [Fact]
    public void Register_NewClient_ReturnsIdAndSecret()
    {
        var registry = TestHelpers.CreateRegistry();
        var request = TestHelpers.CreateSignedRegistrationRequest();

        var response = registry.Register(request);

        Assert.NotEqual(Guid.Empty, response.ClientId);
        Assert.False(string.IsNullOrWhiteSpace(response.ClientSecret));
        Assert.True(response.HeartbeatIntervalSeconds > 0);
    }

    [Fact]
    public void Register_SameDeviceAndMachine_ReturnsExistingClient()
    {
        var registry = TestHelpers.CreateRegistry();
        var request = TestHelpers.CreateSignedRegistrationRequest();

        var first = registry.Register(request);
        var second = registry.Register(request);

        Assert.Equal(first.ClientId, second.ClientId);
        Assert.Equal(first.ClientSecret, second.ClientSecret);
    }

    [Fact]
    public void Register_DifferentDevices_ReturnsDifferentIds()
    {
        var registry = TestHelpers.CreateRegistry();

        var first = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Device-A", machineName: "MACHINE-A"));
        var second = registry.Register(TestHelpers.CreateSignedRegistrationRequest(deviceName: "Device-B", machineName: "MACHINE-B", currentUser: "User2", supportedChannels: [RemoteChannel.Rdp]));

        Assert.NotEqual(first.ClientId, second.ClientId);
    }

    [Fact]
    public void GetClients_AfterRegistration_ContainsClient()
    {
        var registry = TestHelpers.CreateRegistry();
        registry.Register(TestHelpers.CreateSignedRegistrationRequest());

        var clients = registry.GetClients();

        Assert.Single(clients);
        Assert.Equal("TestDevice", clients[0].DeviceName);
        Assert.Equal("MACHINE01", clients[0].MachineName);
    }

    [Fact]
    public void Register_CreatesAuditEntry()
    {
        var registry = TestHelpers.CreateRegistry();
        registry.Register(TestHelpers.CreateSignedRegistrationRequest());

        var audit = registry.GetAuditEntries(10);

        Assert.NotEmpty(audit);
        Assert.Equal("ClientRegistered", audit[0].EventType);
    }

    [Fact]
    public void Register_MissingSignature_Throws()
    {
        var registry = TestHelpers.CreateRegistry();
        var request = TestHelpers.CreateSignedRegistrationRequest() with
        {
            RegistrationSignature = string.Empty
        };

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(request));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ExpiredTimestamp_Throws()
    {
        var registry = TestHelpers.CreateRegistry();
        var request = TestHelpers.CreateSignedRegistrationRequest(requestedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(request));

        Assert.Contains("timestamp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
