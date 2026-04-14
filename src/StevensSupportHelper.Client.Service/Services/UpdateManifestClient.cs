using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class UpdateManifestClient(HttpClient httpClient, IOptions<ServiceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly ServiceOptions _options = options.Value;

    public string GetCurrentVersion()
    {
        return UpdateManifestEvaluator.NormalizeVersion(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");
    }

    public async Task<UpdateReleaseInfo?> TryGetAvailableReleaseAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.UpdateManifestUrl))
        {
            return null;
        }

        var manifest = await DownloadManifestAsync(cancellationToken);
        return manifest is null
            ? null
            : UpdateManifestEvaluator.FindLatestApplicableRelease(
                manifest,
                _options.UpdateChannel,
                GetCurrentVersion());
    }

    public async Task<string> DownloadReleaseAsync(UpdateReleaseInfo release, string targetFilePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);

        using var response = await _httpClient.GetAsync(release.Bundle.Url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var output = File.Create(targetFilePath))
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        var downloadedHash = UpdateManifestEvaluator.ComputeSha256(targetFilePath);
        if (!string.Equals(downloadedHash, release.Bundle.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(targetFilePath);
            throw new InvalidOperationException("Downloaded update bundle failed SHA256 validation.");
        }

        return targetFilePath;
    }

    private async Task<UpdateReleaseManifest?> DownloadManifestAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_options.UpdateManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<UpdateReleaseManifest>(json, JsonOptions);
    }
}
