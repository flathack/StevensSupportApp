namespace StevensSupportHelper.Admin.Models;

public sealed class AdminLogEntry
{
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Level { get; init; } = "Info";
    public string Message { get; init; } = string.Empty;
}
