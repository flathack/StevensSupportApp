namespace StevensSupportHelper.Admin.Models;

public sealed class RemoteSystemSummary
{
    public int ProcessCount { get; init; }
    public double CpuPercent { get; init; }
    public double UsedMemoryGb { get; init; }
    public double TotalMemoryGb { get; init; }
    public double MemoryPercent { get; init; }
}
