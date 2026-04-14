namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteServiceInfo
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string StartType { get; init; } = string.Empty;
    public bool CanStop { get; init; }
}
