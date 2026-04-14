using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class ClientEnvironmentDiscoveryService(IOptionsMonitor<ServiceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IOptionsMonitor<ServiceOptions> _options = options;
    private readonly string _dynamicSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "dynamic-client-settings.json");

    private ClientRuntimeSnapshot? _lastWrittenSnapshot;

    public ClientRuntimeSnapshot GetSnapshot()
    {
        var configuredOptions = _options.CurrentValue;
        var detectedTailscaleIps = configuredOptions.AutoDetectTailscaleIps
            ? DetectTailscaleIpAddresses()
            : [];
        var configuredTailscaleIps = configuredOptions.TailscaleIpAddresses
            .Where(static address => !string.IsNullOrWhiteSpace(address))
            .Select(static address => address.Trim())
            .ToArray();
        var tailscaleIps = detectedTailscaleIps
            .Concat(configuredTailscaleIps)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rustDeskId = configuredOptions.AutoDetectRustDeskId
            ? DetectRustDeskId(configuredOptions.RustDeskId)
            : NormalizeNullable(configuredOptions.RustDeskId);

        var snapshot = new ClientRuntimeSnapshot(
            configuredOptions.AutoApproveSupportRequests,
            configuredOptions.ConsentRequired,
            configuredOptions.TailscaleConnected || tailscaleIps.Length > 0,
            tailscaleIps,
            rustDeskId,
            DetectBatteryPercentage(),
            DetectDiskUsages(),
            DetectMemorySnapshot(),
            RuntimeInformation.OSDescription.Trim(),
            DetectLastBootAtUtc());

        PersistSnapshot(snapshot);
        return snapshot;
    }

    private void PersistSnapshot(ClientRuntimeSnapshot snapshot)
    {
        if (_lastWrittenSnapshot is not null &&
            _lastWrittenSnapshot.AutoApproveSupportRequests == snapshot.AutoApproveSupportRequests &&
            _lastWrittenSnapshot.ConsentRequired == snapshot.ConsentRequired &&
            _lastWrittenSnapshot.TailscaleConnected == snapshot.TailscaleConnected &&
            string.Equals(_lastWrittenSnapshot.RustDeskId, snapshot.RustDeskId, StringComparison.Ordinal) &&
            _lastWrittenSnapshot.BatteryPercentage == snapshot.BatteryPercentage &&
            _lastWrittenSnapshot.DiskUsages.SequenceEqual(snapshot.DiskUsages) &&
            _lastWrittenSnapshot.TotalMemoryBytes == snapshot.TotalMemoryBytes &&
            _lastWrittenSnapshot.AvailableMemoryBytes == snapshot.AvailableMemoryBytes &&
            string.Equals(_lastWrittenSnapshot.OsDescription, snapshot.OsDescription, StringComparison.Ordinal) &&
            _lastWrittenSnapshot.LastBootAtUtc == snapshot.LastBootAtUtc &&
            _lastWrittenSnapshot.TailscaleIpAddresses.SequenceEqual(snapshot.TailscaleIpAddresses, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_dynamicSettingsPath)!);
        var payload = new
        {
            StevensSupportHelper = new
            {
                snapshot.AutoApproveSupportRequests,
                snapshot.ConsentRequired,
                snapshot.TailscaleConnected,
                TailscaleIpAddresses = snapshot.TailscaleIpAddresses,
                RustDeskId = snapshot.RustDeskId ?? string.Empty,
                BatteryPercentage = snapshot.BatteryPercentage,
                DiskUsages = snapshot.DiskUsages,
                TotalMemoryBytes = snapshot.TotalMemoryBytes,
                AvailableMemoryBytes = snapshot.AvailableMemoryBytes,
                OsDescription = snapshot.OsDescription,
                LastBootAtUtc = snapshot.LastBootAtUtc
            }
        };
        File.WriteAllText(_dynamicSettingsPath, JsonSerializer.Serialize(payload, JsonOptions));
        _lastWrittenSnapshot = snapshot;
    }

    private static string[] DetectTailscaleIpAddresses()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static nic =>
                nic.OperationalStatus == OperationalStatus.Up &&
                (nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
                 || nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
            .Select(static address => address.Address)
            .Where(static address =>
                !IPAddress.IsLoopback(address) &&
                !(address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal))
            .Select(static address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (addresses.Length > 0)
        {
            return addresses;
        }

        var tailscaleCli = ResolveTailscaleExecutable();
        if (tailscaleCli is null)
        {
            return [];
        }

        var cliAddresses = new List<string>();
        cliAddresses.AddRange(ReadCliAddresses(tailscaleCli, "ip -4"));
        cliAddresses.AddRange(ReadCliAddresses(tailscaleCli, "ip -6"));
        return cliAddresses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadCliAddresses(string executablePath, string arguments)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);
            if (process.ExitCode != 0)
            {
                return [];
            }

            return output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string? DetectRustDeskId(string configuredRustDeskId)
    {
        if (!string.IsNullOrWhiteSpace(configuredRustDeskId))
        {
            return configuredRustDeskId.Trim();
        }

        string[] candidatePaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RustDesk", "config", "RustDesk2.toml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustDesk", "config", "RustDesk2.toml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustDesk", "config", "RustDesk2.toml")
        ];

        foreach (var candidatePath in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            string content = File.ReadAllText(candidatePath);
            var match = Regex.Match(content, @"(?m)^\s*id\s*=\s*['""]?(?<id>[0-9\-]+)['""]?\s*$");
            if (match.Success)
            {
                return match.Groups["id"].Value.Trim();
            }
        }

        return null;
    }

    private static string? ResolveTailscaleExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(programFiles, "Tailscale", "tailscale.exe"),
            Path.Combine(localAppData, "Tailscale", "tailscale.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? DetectBatteryPercentage()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty EstimatedChargeRemaining)\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(4000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            return int.TryParse(output, out var percentage) && percentage is >= 0 and <= 100
                ? percentage
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<DiskUsageDto> DetectDiskUsages()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(static drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network)
                .OrderBy(static drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static drive => new DiskUsageDto(
                    drive.Name.TrimEnd(Path.DirectorySeparatorChar),
                    drive.TotalSize,
                    drive.AvailableFreeSpace))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static (long? TotalMemoryBytes, long? AvailableMemoryBytes) DetectMemorySnapshot()
    {
        try
        {
            var status = new MemoryStatusEx();
            status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
            return GlobalMemoryStatusEx(ref status)
                ? ((long)status.ullTotalPhys, (long)status.ullAvailPhys)
                : (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    private static DateTimeOffset? DetectLastBootAtUtc()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return DateTimeOffset.UtcNow - uptime;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}

public sealed record ClientRuntimeSnapshot(
    bool AutoApproveSupportRequests,
    bool ConsentRequired,
    bool TailscaleConnected,
    IReadOnlyList<string> TailscaleIpAddresses,
    string? RustDeskId,
    int? BatteryPercentage,
    IReadOnlyList<DiskUsageDto> DiskUsages,
    (long? TotalMemoryBytes, long? AvailableMemoryBytes) MemorySnapshot,
    string? OsDescription,
    DateTimeOffset? LastBootAtUtc)
{
    public long? TotalMemoryBytes => MemorySnapshot.TotalMemoryBytes;
    public long? AvailableMemoryBytes => MemorySnapshot.AvailableMemoryBytes;
}
