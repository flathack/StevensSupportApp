namespace StevensSupportHelper.Admin.Models;

public sealed class ClientDiskUsageRow
{
    private const double MaxBarWidth = 164;

    public string DriveName { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)UsedBytes / TotalBytes * 100d, 0d, 100d);

    public double UsedBarWidth => MaxBarWidth * UsedPercentage / 100d;

    public string UsageSummary => $"{DriveName}  {UsedPercentage:0}% belegt";

    public string CapacitySummary => $"{FormatBytes(UsedBytes)} / {FormatBytes(TotalBytes)}";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
