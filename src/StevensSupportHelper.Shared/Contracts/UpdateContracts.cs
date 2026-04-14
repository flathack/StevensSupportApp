using System.Security.Cryptography;
using System.Text.Json;

namespace StevensSupportHelper.Shared.Contracts;

public sealed record UpdateReleaseManifest(
    string Product,
    string GeneratedAtUtc,
    IReadOnlyList<UpdateReleaseInfo> Releases);

public sealed record UpdateReleaseInfo(
    string Channel,
    string Version,
    string PublishedAtUtc,
    string Notes,
    UpdateArtifact Bundle);

public sealed record UpdateArtifact(
    string Url,
    string Sha256,
    long SizeBytes);

public sealed record PendingClientUpdate(
    string Version,
    string Channel,
    string Notes,
    string PackagePath,
    string Sha256,
    DateTimeOffset DetectedAtUtc);

public static class UpdateManifestEvaluator
{
    public static UpdateReleaseInfo? FindLatestApplicableRelease(
        UpdateReleaseManifest manifest,
        string channel,
        string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!Version.TryParse(NormalizeVersion(currentVersion), out var current))
        {
            current = new Version(0, 0, 0, 0);
        }

        return manifest.Releases
            .Where(release => string.Equals(release.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .Select(release => new
            {
                Release = release,
                ParsedVersion = Version.TryParse(NormalizeVersion(release.Version), out var parsed)
                    ? parsed
                    : new Version(0, 0, 0, 0)
            })
            .Where(candidate => candidate.ParsedVersion > current)
            .OrderByDescending(candidate => candidate.ParsedVersion)
            .Select(candidate => candidate.Release)
            .FirstOrDefault();
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    public static string NormalizeVersion(string version)
    {
        return string.IsNullOrWhiteSpace(version) ? "0.0.0.0" : version.Trim();
    }

    public static UpdateReleaseManifest? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<UpdateReleaseManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
