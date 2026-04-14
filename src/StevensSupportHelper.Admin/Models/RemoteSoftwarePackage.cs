namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteSoftwarePackage
{
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string QuietUninstallCommand { get; init; } = string.Empty;
    public string UninstallCommand { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public bool WindowsInstaller { get; init; }
    public string Source { get; init; } = string.Empty;
}
