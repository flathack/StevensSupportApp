using System.Text.Json;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Shared.Contracts;
using StevensSupportHelper.Shared.Diagnostics;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class ClientUpdateCoordinator(UpdateManifestClient manifestClient, IOptions<ServiceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly UpdateManifestClient _manifestClient = manifestClient;
    private readonly ServiceOptions _options = options.Value;

    public async Task<PendingClientUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.UpdateManifestUrl))
        {
            return null;
        }

        Directory.CreateDirectory(_options.UpdatesRoot);

        var release = await _manifestClient.TryGetAvailableReleaseAsync(cancellationToken);
        if (release is null)
        {
            return null;
        }

        var stagedPackagePath = Path.Combine(_options.UpdatesRoot, $"StevensSupportHelper-client-{release.Version}.zip");
        if (!File.Exists(stagedPackagePath) ||
            !string.Equals(UpdateManifestEvaluator.ComputeSha256(stagedPackagePath), release.Bundle.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await _manifestClient.DownloadReleaseAsync(release, stagedPackagePath, cancellationToken);
        }

        var pending = new PendingClientUpdate(
            release.Version,
            release.Channel,
            release.Notes,
            stagedPackagePath,
            release.Bundle.Sha256,
            DateTimeOffset.UtcNow);
        await SavePendingUpdateAsync(pending, cancellationToken);
        AppDiagnostics.WriteEvent("ClientService", "UpdateStaged", $"Staged client update {release.Version} from channel {release.Channel}.");
        return pending;
    }

    public async Task<PendingClientUpdate?> LoadPendingUpdateAsync(CancellationToken cancellationToken)
    {
        var statusPath = GetPendingUpdateStatusPath();
        if (!File.Exists(statusPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(statusPath);
        return await JsonSerializer.DeserializeAsync<PendingClientUpdate>(stream, JsonOptions, cancellationToken);
    }

    private async Task SavePendingUpdateAsync(PendingClientUpdate pendingUpdate, CancellationToken cancellationToken)
    {
        var statusPath = GetPendingUpdateStatusPath();
        await using var stream = File.Create(statusPath);
        await JsonSerializer.SerializeAsync(stream, pendingUpdate, JsonOptions, cancellationToken);
    }

    public string GetPendingUpdateStatusPath()
    {
        return Path.Combine(_options.UpdatesRoot, "pending-update.json");
    }
}
