namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteRegistryEntry
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string FullName => string.IsNullOrWhiteSpace(Name) ? "(Default)" : Name;
}
