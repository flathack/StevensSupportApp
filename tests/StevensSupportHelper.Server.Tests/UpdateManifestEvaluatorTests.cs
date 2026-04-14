using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Tests;

public sealed class UpdateManifestEvaluatorTests
{
    [Fact]
    public void FindLatestApplicableRelease_ReturnsNewestMatchingChannelAboveCurrentVersion()
    {
        var manifest = new UpdateReleaseManifest(
            "StevensSupportHelper",
            DateTimeOffset.UtcNow.ToString("O"),
            [
                new UpdateReleaseInfo("stable", "1.0.0", DateTimeOffset.UtcNow.ToString("O"), "Old", new UpdateArtifact("https://example.invalid/1.zip", "ABC", 10)),
                new UpdateReleaseInfo("stable", "1.2.0", DateTimeOffset.UtcNow.ToString("O"), "New", new UpdateArtifact("https://example.invalid/2.zip", "DEF", 20)),
                new UpdateReleaseInfo("beta", "9.0.0", DateTimeOffset.UtcNow.ToString("O"), "Beta", new UpdateArtifact("https://example.invalid/3.zip", "XYZ", 30))
            ]);

        var release = UpdateManifestEvaluator.FindLatestApplicableRelease(manifest, "stable", "1.1.0");

        Assert.NotNull(release);
        Assert.Equal("1.2.0", release!.Version);
    }

    [Fact]
    public void FindLatestApplicableRelease_ReturnsNullWhenCurrentVersionIsNewest()
    {
        var manifest = new UpdateReleaseManifest(
            "StevensSupportHelper",
            DateTimeOffset.UtcNow.ToString("O"),
            [
                new UpdateReleaseInfo("stable", "1.0.0", DateTimeOffset.UtcNow.ToString("O"), "Only", new UpdateArtifact("https://example.invalid/1.zip", "ABC", 10))
            ]);

        var release = UpdateManifestEvaluator.FindLatestApplicableRelease(manifest, "stable", "1.0.0");

        Assert.Null(release);
    }
}
