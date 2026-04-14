using System.IO;
using System.Text;
using StevensSupportHelper.Admin.Models;

namespace StevensSupportHelper.Admin.Services;

public sealed class RemoteActionScriptService
{
    public string EnsureScriptDirectory(string? configuredPath)
    {
        var scriptDirectory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "RemoteActions")
            : configuredPath.Trim();
        Directory.CreateDirectory(scriptDirectory);
        EnsureDefaultScripts(scriptDirectory);
        return scriptDirectory;
    }

    public IReadOnlyList<RemoteActionScript> LoadScripts(string? configuredPath)
    {
        var scriptDirectory = EnsureScriptDirectory(configuredPath);
        return Directory.GetFiles(scriptDirectory, "*.ps1", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new RemoteActionScript(
                Path.GetFileNameWithoutExtension(path),
                path,
                File.ReadAllText(path)))
            .ToArray();
    }

    public void SaveScript(RemoteActionScript script)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(script.Path)!);
        File.WriteAllText(script.Path, script.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void EnsureDefaultScripts(string scriptDirectory)
    {
        foreach (var script in GetDefaultScripts())
        {
            var path = Path.Combine(scriptDirectory, script.FileName);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, script.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }

    private static IReadOnlyList<(string FileName, string Content)> GetDefaultScripts()
    {
        const string header = """
# Available variables:
# $ClientId
# $DeviceName
# $MachineName
# $CurrentUser
# $AgentVersion
# $RustDeskId
# $Notes
# $TailscaleIpAddresses
""";

        return
        [
            ("restart_computer.ps1", header + """

Restart-Computer -Force
"""),
            ("shutdown_computer.ps1", header + """

Stop-Computer -Force
"""),
            ("winget_update_all.ps1", header + """

winget update --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-String -Width 4096
"""),
            ("restart_client_service.ps1", header + """

$serviceName = 'StevensSupportHelperClientService'
Restart-Service -Name $serviceName -Force
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            ("remote_update_client.ps1", header + """

$installerPath = 'C:\ProgramData\StevensSupportHelper\AdminUpdates\StevensSupportHelper.Installer.exe'
$configPath = 'C:\ProgramData\StevensSupportHelper\AdminUpdates\client.installer.config'
$serviceName = 'StevensSupportHelperClientService'
$trayProcessName = 'StevensSupportHelper.Client.Tray'

Write-Host "Remote update started for $DeviceName on $env:COMPUTERNAME"
Write-Host "Installer path: $installerPath"
Write-Host "Config path: $configPath"

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer not found at $installerPath"
}

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "client.installer.config not found at $configPath"
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Stopping service $serviceName"
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }
}
else {
    Write-Host "Service $serviceName does not exist yet."
}

$trayProcesses = @(Get-Process -Name $trayProcessName -ErrorAction SilentlyContinue)
if ($trayProcesses.Count -gt 0) {
    Write-Host "Stopping tray process $trayProcessName"
    $trayProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
}
else {
    Write-Host "Tray process $trayProcessName is not running."
}

Write-Host "Running installer in silent mode"
$process = Start-Process -FilePath $installerPath -WorkingDirectory (Split-Path -Path $installerPath -Parent) -ArgumentList '--silent --update-only' -PassThru -Wait
Write-Host "Installer exit code: $($process.ExitCode)"

if ($process.ExitCode -ne 0) {
    throw "Installer failed with exit code $($process.ExitCode)"
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Starting service $serviceName"
    Start-Service -Name $serviceName -ErrorAction Stop
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
}

$trayExecutable = 'C:\Program Files\StevensSupportHelper\client-tray\StevensSupportHelper.Client.Tray.exe'
if (Test-Path -LiteralPath $trayExecutable) {
    Write-Host "Starting tray executable"
    Start-Process -FilePath $trayExecutable -WorkingDirectory (Split-Path -Path $trayExecutable -Parent) | Out-Null
}
else {
    Write-Host "Tray executable not found at $trayExecutable"
}

Write-Host "Remote update finished successfully."
Get-Service -Name $serviceName -ErrorAction SilentlyContinue | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            ("start_client_service.ps1", header + """

$serviceName = 'StevensSupportHelperClientService'
Start-Service -Name $serviceName
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            ("stop_client_service.ps1", header + """

$serviceName = 'StevensSupportHelperClientService'
Stop-Service -Name $serviceName -Force
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            ("show_client_runtime.ps1", header + """

[pscustomobject]@{
    ComputerName = $env:COMPUTERNAME
    CurrentUser = (whoami)
    ClientId = $ClientId
    DeviceName = $DeviceName
    MachineName = $MachineName
    AgentVersion = $AgentVersion
    TailscaleIpAddresses = ($TailscaleIpAddresses -join ', ')
} | Format-List | Out-String -Width 4096
""")
        ];
    }
}
