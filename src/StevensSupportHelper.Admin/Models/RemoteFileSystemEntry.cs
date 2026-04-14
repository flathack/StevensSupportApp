namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteFileSystemEntry
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string EntryType { get; init; } = string.Empty;
    public long? Length { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public bool IsDirectory => string.Equals(EntryType, "Directory", StringComparison.OrdinalIgnoreCase)
        || string.Equals(EntryType, "Drive", StringComparison.OrdinalIgnoreCase);
    public string SizeText => IsDirectory || Length is null ? string.Empty : $"{Length:N0} B";
    public string EntryIcon => EntryType switch
    {
        "Drive" => "🖴",
        "Directory" => "📁",
        _ => "📄"
    };
}
