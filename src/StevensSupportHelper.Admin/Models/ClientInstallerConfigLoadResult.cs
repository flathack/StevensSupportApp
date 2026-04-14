namespace StevensSupportHelper.Admin.Models;

public sealed record ClientInstallerConfigLoadResult(
    string ConfigText,
    bool IsSynthesized,
    string Message);
