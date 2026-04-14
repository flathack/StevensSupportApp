namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteProcessInfo
{
    public int Id { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string MainWindowTitle { get; init; } = string.Empty;
    public double? CpuSeconds { get; init; }
    public double WorkingSetMb { get; init; }
    public DateTimeOffset? StartTimeUtc { get; init; }
}
