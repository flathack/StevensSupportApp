using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.ServiceProcess;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class AgentJobProcessor(ClientEnvironmentDiscoveryService environmentDiscoveryService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ClientEnvironmentDiscoveryService _environmentDiscoveryService = environmentDiscoveryService;

    public Task<string> ProcessAsync(AgentJobDto job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return job.JobType switch
        {
            AgentJobType.ProcessSnapshot => Task.FromResult(BuildProcessSnapshotJson()),
            AgentJobType.WindowsUpdateScan => Task.FromResult(BuildWindowsUpdateScanJsonSafe()),
            AgentJobType.WindowsUpdateInstall => Task.FromResult(InstallWindowsUpdatesJsonSafe()),
            AgentJobType.RegistrySnapshot => Task.FromResult(BuildRegistrySnapshotJsonSafe(job)),
            AgentJobType.ServiceSnapshot => Task.FromResult(BuildServiceSnapshotJsonSafe()),
            AgentJobType.ServiceControl => Task.FromResult(ExecuteServiceControlJsonSafe(job)),
            AgentJobType.ScriptExecution => ExecuteScriptJsonSafeAsync(job, cancellationToken),
            AgentJobType.PowerPlanSnapshot => Task.FromResult(BuildPowerPlanSnapshotJsonSafe()),
            AgentJobType.PowerPlanActivate => Task.FromResult(ActivatePowerPlanJsonSafe(job)),
            _ => throw new InvalidOperationException($"Unsupported agent job type: {job.JobType}")
        };
    }

    private string BuildProcessSnapshotJson()
    {
        var processes = Process.GetProcesses()
            .Select(CreateProcessInfo)
            .OrderByDescending(static process => process.WorkingSetMb)
            .ThenBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var snapshot = _environmentDiscoveryService.GetSnapshot();
        var totalMemoryBytes = snapshot.TotalMemoryBytes ?? 0;
        var availableMemoryBytes = snapshot.AvailableMemoryBytes ?? 0;
        var usedMemoryBytes = Math.Max(0, totalMemoryBytes - availableMemoryBytes);
        var totalMemoryGb = totalMemoryBytes / 1024d / 1024d / 1024d;
        var usedMemoryGb = usedMemoryBytes / 1024d / 1024d / 1024d;
        var memoryPercent = totalMemoryBytes <= 0 ? 0 : usedMemoryBytes * 100d / totalMemoryBytes;

        var result = new AgentProcessSnapshotResult(
            processes,
            new AgentSystemSummaryDto(
                processes.Length,
                0,
                usedMemoryGb,
                totalMemoryGb,
                memoryPercent));

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private string BuildWindowsUpdateScanJsonSafe()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Update agent jobs are only supported on Windows.");
        }

        return BuildWindowsUpdateScanJson();
    }

    [SupportedOSPlatform("windows")]
    private string BuildWindowsUpdateScanJson()
    {
        var updateSession = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)
            ?? throw new InvalidOperationException("Microsoft Update Session COM object is unavailable.");
        dynamic session = updateSession;
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Software'");

        var items = new List<AgentWindowsUpdateItemDto>();
        for (var index = 0; index < (int)searchResult.Updates.Count; index++)
        {
            dynamic update = searchResult.Updates.Item(index);
            string kbIds = string.Join(", ", ((object[])update.KBArticleIDs).Select(static item => item?.ToString()).Where(static item => !string.IsNullOrWhiteSpace(item)));
            var categories = new List<string>();
            for (var categoryIndex = 0; categoryIndex < (int)update.Categories.Count; categoryIndex++)
            {
                categories.Add(update.Categories.Item(categoryIndex).Name.ToString());
            }

            items.Add(new AgentWindowsUpdateItemDto(
                update.Title.ToString(),
                kbIds,
                string.Join(", ", categories),
                (bool)update.IsDownloaded,
                (long)update.MaxDownloadSize));
        }

        return JsonSerializer.Serialize(new AgentWindowsUpdateScanResult(items), JsonOptions);
    }

    private string InstallWindowsUpdatesJsonSafe()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Update agent jobs are only supported on Windows.");
        }

        return InstallWindowsUpdatesJson();
    }

    private string BuildRegistrySnapshotJsonSafe(AgentJobDto job)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Registry agent jobs are only supported on Windows.");
        }

        return BuildRegistrySnapshotJson(job);
    }

    [SupportedOSPlatform("windows")]
    private string BuildRegistrySnapshotJson(AgentJobDto job)
    {
        var request = JsonSerializer.Deserialize<AgentRegistrySnapshotRequest>(job.RequestJson ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Registry snapshot job is missing request payload.");
        var (hive, subKeyPath) = ParseRegistryPath(request.RegistryPath);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key = string.IsNullOrWhiteSpace(subKeyPath) ? baseKey : baseKey.OpenSubKey(subKeyPath, writable: false);
        if (key is null)
        {
            throw new InvalidOperationException("Registry path not found.");
        }

        var subKeys = key.GetSubKeyNames()
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var values = key.GetValueNames()
            .Select(name =>
            {
                var valueName = name ?? string.Empty;
                object? value = key.GetValue(valueName);
                var kind = key.GetValueKind(valueName).ToString();
                var normalizedValue = value switch
                {
                    null => string.Empty,
                    string[] array => string.Join(", ", array),
                    byte[] bytes => BitConverter.ToString(bytes),
                    _ => value.ToString() ?? string.Empty
                };

                return new AgentRegistryEntryDto(valueName, kind, normalizedValue);
            })
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(new AgentRegistrySnapshotResult(subKeys, values), JsonOptions);
    }

    private string BuildServiceSnapshotJsonSafe()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Service agent jobs are only supported on Windows.");
        }

        return BuildServiceSnapshotJson();
    }

    private string ExecuteServiceControlJsonSafe(AgentJobDto job)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Service agent jobs are only supported on Windows.");
        }

        return ExecuteServiceControlJson(job);
    }

    private Task<string> ExecuteScriptJsonSafeAsync(AgentJobDto job, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Script execution agent jobs are only supported on Windows.");
        }

        return ExecuteScriptJsonAsync(job, cancellationToken);
    }

    private string BuildPowerPlanSnapshotJsonSafe()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Power plan agent jobs are only supported on Windows.");
        }

        return BuildPowerPlanSnapshotJson();
    }

    private string ActivatePowerPlanJsonSafe(AgentJobDto job)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Power plan agent jobs are only supported on Windows.");
        }

        return ActivatePowerPlanJson(job);
    }

    [SupportedOSPlatform("windows")]
    private string BuildServiceSnapshotJson()
    {
        var services = ServiceController.GetServices()
            .Select(CreateServiceInfo)
            .OrderBy(static service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static service => service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(new AgentServiceSnapshotResult(services), JsonOptions);
    }

    [SupportedOSPlatform("windows")]
    private string ExecuteServiceControlJson(AgentJobDto job)
    {
        var request = JsonSerializer.Deserialize<AgentServiceControlRequest>(job.RequestJson ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Service control job is missing request payload.");
        if (string.IsNullOrWhiteSpace(request.ServiceName))
        {
            throw new InvalidOperationException("Service name is required.");
        }

        var normalizedAction = request.Action?.Trim().ToLowerInvariant();
        using var service = new ServiceController(request.ServiceName.Trim());
        _ = service.Status;

        string message = normalizedAction switch
        {
            "start" => StartService(service),
            "stop" => StopService(service),
            "restart" => RestartService(service),
            _ => throw new InvalidOperationException($"Unsupported service action: {request.Action}")
        };

        return JsonSerializer.Serialize(new AgentServiceControlResult(message), JsonOptions);
    }

    [SupportedOSPlatform("windows")]
    private async Task<string> ExecuteScriptJsonAsync(AgentJobDto job, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<AgentScriptExecutionRequest>(job.RequestJson ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Script execution job is missing request payload.");
        if (string.IsNullOrWhiteSpace(request.ScriptContent))
        {
            throw new InvalidOperationException("Script content is required.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "StevensSupportHelper", "AgentScripts", job.JobId.ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = Path.Combine(tempDirectory, "remote-action.ps1");

        try
        {
            await File.WriteAllTextAsync(scriptPath, BuildScriptWrapper(request, job.ClientId), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

            var hostApplication = ResolvePowerShellHostPath();
            var startInfo = new ProcessStartInfo
            {
                FileName = hostApplication,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                WorkingDirectory = tempDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the PowerShell host process.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var errorOutput = await errorTask;

            var result = new AgentScriptExecutionResult(
                output.Trim(),
                errorOutput.Trim(),
                process.ExitCode,
                Path.GetFileName(hostApplication));
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private string BuildPowerPlanSnapshotJson()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = "/list",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start powercfg.");
        var output = process.StandardOutput.ReadToEnd();
        var errorOutput = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorOutput) ? "powercfg /list failed." : errorOutput.Trim());
        }

        var plans = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParsePowerPlan)
            .Where(static plan => plan is not null)
            .Select(static plan => plan!)
            .ToArray();

        return JsonSerializer.Serialize(new AgentPowerPlanSnapshotResult(plans), JsonOptions);
    }

    [SupportedOSPlatform("windows")]
    private string ActivatePowerPlanJson(AgentJobDto job)
    {
        var request = JsonSerializer.Deserialize<AgentPowerPlanActivateRequest>(job.RequestJson ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Power plan activation job is missing request payload.");
        if (string.IsNullOrWhiteSpace(request.Guid))
        {
            throw new InvalidOperationException("Power plan GUID is required.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = $"/setactive {request.Guid.Trim()}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start powercfg.");
        var errorOutput = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorOutput) ? "powercfg /setactive failed." : errorOutput.Trim());
        }

        return JsonSerializer.Serialize(new AgentPowerPlanActivateResult($"Power plan {request.Guid.Trim()} activated successfully."), JsonOptions);
    }

    [SupportedOSPlatform("windows")]
    private string InstallWindowsUpdatesJson()
    {
        var updateSession = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)
            ?? throw new InvalidOperationException("Microsoft Update Session COM object is unavailable.");
        dynamic session = updateSession;
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Software'");
        if ((int)searchResult.Updates.Count == 0)
        {
            return JsonSerializer.Serialize(new AgentWindowsUpdateInstallResult("No Windows updates are pending."), JsonOptions);
        }

        dynamic updatesToInstall = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)
            ?? throw new InvalidOperationException("Microsoft Update Collection COM object is unavailable.");
        for (var index = 0; index < (int)searchResult.Updates.Count; index++)
        {
            updatesToInstall.Add(searchResult.Updates.Item(index));
        }

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = updatesToInstall;
        downloader.Download();

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = updatesToInstall;
        dynamic result = installer.Install();

        var message = $"Windows Update install result: {result.ResultCode} | Reboot required: {result.RebootRequired} | Updates processed: {(int)updatesToInstall.Count}";
        return JsonSerializer.Serialize(new AgentWindowsUpdateInstallResult(message), JsonOptions);
    }

    [SupportedOSPlatform("windows")]
    private static (RegistryHive Hive, string SubKeyPath) ParseRegistryPath(string registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
        {
            throw new InvalidOperationException("Registry path is required.");
        }

        var normalized = registryPath.Trim();
        if (normalized.StartsWith("Registry::", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Registry::".Length..];
        }

        normalized = normalized.Replace('/', '\\');
        var separatorIndex = normalized.IndexOf('\\');
        var hiveName = separatorIndex >= 0 ? normalized[..separatorIndex] : normalized;
        var subKeyPath = separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : string.Empty;

        return hiveName.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => (RegistryHive.LocalMachine, subKeyPath),
            "HKEY_CURRENT_USER" or "HKCU" => (RegistryHive.CurrentUser, subKeyPath),
            "HKEY_CLASSES_ROOT" or "HKCR" => (RegistryHive.ClassesRoot, subKeyPath),
            "HKEY_USERS" or "HKU" => (RegistryHive.Users, subKeyPath),
            "HKEY_CURRENT_CONFIG" or "HKCC" => (RegistryHive.CurrentConfig, subKeyPath),
            _ => throw new InvalidOperationException($"Unsupported registry hive: {hiveName}")
        };
    }

    private static AgentProcessInfoDto CreateProcessInfo(Process process)
    {
        try
        {
            return new AgentProcessInfoDto(
                process.Id,
                process.ProcessName,
                SafeRead(() => process.MainWindowTitle) ?? string.Empty,
                SafeRead<double?>(() => process.TotalProcessorTime.TotalSeconds),
                SafeRead<double?>(() => process.WorkingSet64 / 1024d / 1024d) ?? 0d,
                SafeRead<DateTimeOffset?>(() => new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero)));
        }
        finally
        {
            process.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static AgentServiceInfoDto CreateServiceInfo(ServiceController service)
    {
        try
        {
            return new AgentServiceInfoDto(
                service.ServiceName,
                service.DisplayName,
                service.Status.ToString(),
                SafeRead(() => service.StartType.ToString()) ?? "Unknown",
                SafeRead<bool?>(() => service.CanStop) ?? false);
        }
        finally
        {
            service.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static string StartService(ServiceController service)
    {
        if (service.Status != ServiceControllerStatus.Running)
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }

        service.Refresh();
        return $"Service {service.ServiceName} is {service.Status}.";
    }

    [SupportedOSPlatform("windows")]
    private static string StopService(ServiceController service)
    {
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            if (!service.CanStop)
            {
                throw new InvalidOperationException($"Service {service.ServiceName} cannot be stopped.");
            }

            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }

        service.Refresh();
        return $"Service {service.ServiceName} is {service.Status}.";
    }

    [SupportedOSPlatform("windows")]
    private static string RestartService(ServiceController service)
    {
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            if (!service.CanStop)
            {
                throw new InvalidOperationException($"Service {service.ServiceName} cannot be restarted because it cannot be stopped.");
            }

            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }

        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        service.Refresh();
        return $"Service {service.ServiceName} restarted successfully and is {service.Status}.";
    }

    private static T? SafeRead<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return default;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string BuildScriptWrapper(AgentScriptExecutionRequest request, Guid clientId)
    {
        static string EscapeSingleQuoted(string? value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine($"$ClientId = '{clientId:D}'");
        builder.AppendLine($"$DeviceName = '{EscapeSingleQuoted(request.DeviceName)}'");
        builder.AppendLine($"$MachineName = '{EscapeSingleQuoted(request.MachineName)}'");
        builder.AppendLine($"$CurrentUser = '{EscapeSingleQuoted(request.CurrentUser)}'");
        builder.AppendLine($"$AgentVersion = '{EscapeSingleQuoted(request.AgentVersion)}'");
        builder.AppendLine($"$RustDeskId = '{EscapeSingleQuoted(request.RustDeskId)}'");
        builder.AppendLine($"$Notes = '{EscapeSingleQuoted(request.Notes)}'");
        builder.AppendLine("$TailscaleIpAddresses = @(");
        foreach (var ipAddress in request.TailscaleIpAddresses)
        {
            builder.AppendLine($"    '{EscapeSingleQuoted(ipAddress)}'");
        }
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine(request.ScriptContent);
        return builder.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static string ResolvePowerShellHostPath()
    {
        var systemPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(systemPowerShell))
        {
            return systemPowerShell;
        }

        const string fallback = "powershell.exe";
        return fallback;
    }

    private static AgentPowerPlanDto? ParsePowerPlan(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"Power Scheme GUID:\s+([A-Fa-f0-9\-]{36})\s+\((.+?)\)(\s+\*)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        return new AgentPowerPlanDto(
            match.Groups[1].Value,
            match.Groups[2].Value,
            match.Groups[3].Success);
    }
}
