namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteWingetPackageOption
{
    public string DisplayName { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public override string ToString() => $"{DisplayName} ({PackageId})";
}
