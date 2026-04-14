using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

internal static class TestHelpers
{
    public static ClientRegistry CreateRegistry(string? stateDir = null)
    {
        var dir = stateDir ?? Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var storageOptions = Microsoft.Extensions.Options.Options.Create(new ServerStorageOptions
        {
            StateFilePath = Path.Combine(dir, "state.json"),
            MaxAuditEntries = 50,
            MaxTransferBytes = 5 * 1024 * 1024
        });
        var registrationOptions = Microsoft.Extensions.Options.Options.Create(new ClientRegistrationOptions
        {
            RequireSignedRegistration = true,
            SharedKey = "test-registration-key",
            AllowedClockSkewMinutes = 5
        });

        var store = new ServerStateStore(storageOptions);
        var verifier = new ClientRegistrationVerifier(registrationOptions);
        return new ClientRegistry(store, storageOptions, verifier);
    }

    public static RegisterClientRequest CreateSignedRegistrationRequest(
        string deviceName = "TestDevice",
        string machineName = "MACHINE01",
        string currentUser = "User1",
        string agentVersion = "1.0.0",
        bool consentRequired = true,
        bool autoApproveSupportRequests = false,
        bool tailscaleConnected = false,
        IReadOnlyList<RemoteChannel>? supportedChannels = null,
        string? rustDeskId = null,
        DateTimeOffset? requestedAtUtc = null,
        string? registrationNonce = null)
    {
        var request = new RegisterClientRequest(
            deviceName,
            machineName,
            currentUser,
            true,
            false,
            agentVersion,
            null,
            consentRequired,
            autoApproveSupportRequests,
            tailscaleConnected,
            [],
            supportedChannels ?? [RemoteChannel.WinRm],
            requestedAtUtc ?? DateTimeOffset.UtcNow,
            registrationNonce ?? Guid.NewGuid().ToString("N"),
            string.Empty,
            rustDeskId);

        return request with
        {
            RegistrationSignature = RegistrationSignatureHelper.ComputeSignature(request, "test-registration-key")
        };
    }
}
