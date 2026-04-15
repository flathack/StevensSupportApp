using System.Text;

namespace StevensSupportHelper.Server.Services;

public sealed class RemoteActionCatalogService
{
    private readonly ServerStateStore _stateStore;

    public RemoteActionCatalogService(ServerStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public IReadOnlyList<RemoteActionCatalogEntry> GetActions()
    {
        var scriptDirectory = EnsureScriptDirectory();
        var defaultMetadata = GetDefaultScriptMetadata()
            .ToDictionary(static entry => entry.FileName, StringComparer.OrdinalIgnoreCase);

        return Directory.GetFiles(scriptDirectory, "*.ps1", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var scriptContent = File.ReadAllText(path);
                if (defaultMetadata.TryGetValue(fileName, out var metadata))
                {
                    return new RemoteActionCatalogEntry(
                        fileName,
                        metadata.Description,
                        metadata.RequiresElevation,
                        scriptContent);
                }

                return new RemoteActionCatalogEntry(
                    fileName,
                    "Benutzerdefiniertes Remote-Skript.",
                    false,
                    scriptContent);
            })
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RemoteActionCatalogEntry GetAction(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("Bitte gib einen Skriptnamen an.", nameof(actionName));
        }

        var normalizedFileName = NormalizeFileName(actionName);
        var action = GetActions().FirstOrDefault(entry => string.Equals(entry.Name, normalizedFileName, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            throw new FileNotFoundException($"Das Remote-Skript '{normalizedFileName}' wurde nicht gefunden.", normalizedFileName);
        }

        return action;
    }

    private string EnsureScriptDirectory()
    {
        var state = _stateStore.Load();
        var configuredPath = state.DeploymentSettings.RemoteActionsPath?.Trim();
        var scriptDirectory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_stateStore.StorageRootPath, "RemoteActions")
            : configuredPath;

        Directory.CreateDirectory(scriptDirectory);
        EnsureDefaultScripts(scriptDirectory);
        return scriptDirectory;
    }

    private static string NormalizeFileName(string actionName)
    {
        var trimmed = actionName.Trim();
        var fileName = Path.GetFileName(trimmed);
        return fileName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".ps1";
    }

    private static void EnsureDefaultScripts(string scriptDirectory)
    {
        foreach (var script in GetDefaultScriptMetadata())
        {
            var path = Path.Combine(scriptDirectory, script.FileName);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, script.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }

    private static IReadOnlyList<RemoteActionScriptMetadata> GetDefaultScriptMetadata()
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
            new RemoteActionScriptMetadata(
                "collect_support_snapshot.ps1",
                "Sammelt Diagnosedaten und eine Systemübersicht.",
                false,
                header + """

$computerInfo = Get-ComputerInfo | Select-Object CsName, WindowsVersion, OsArchitecture, OsBuildNumber
$network = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '169.254*' } |
    Select-Object InterfaceAlias, IPAddress
$services = Get-Service | Sort-Object Status, DisplayName | Select-Object -First 25 Name, DisplayName, Status

"=== Computer ==="
$computerInfo | Format-List | Out-String -Width 4096
"=== Netzwerk ==="
$network | Format-Table -AutoSize | Out-String -Width 4096
"=== Dienste (Top 25) ==="
$services | Format-Table -AutoSize | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "restart_spooler.ps1",
                "Startet den Windows-Druckspooler neu.",
                true,
                header + """

$serviceName = 'Spooler'
Restart-Service -Name $serviceName -Force
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "restart_computer.ps1",
                "Startet den Computer neu.",
                true,
                header + """

Restart-Computer -Force
"""),
            new RemoteActionScriptMetadata(
                "shutdown_computer.ps1",
                "Fährt den Computer herunter.",
                true,
                header + """

Stop-Computer -Force
"""),
            new RemoteActionScriptMetadata(
                "winget_update_all.ps1",
                "Plant Softwareupdates per winget ein.",
                true,
                header + """

winget update --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "restart_client_service.ps1",
                "Startet den StevensSupportHelper-Clientdienst neu.",
                true,
                header + """

$serviceName = 'StevensSupportHelperClientService'
Restart-Service -Name $serviceName -Force
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "remote_update_client.ps1",
                "Führt ein vorbereitetes Remote-Update des Clients aus.",
                true,
                header + """

$installerPath = 'C:\ProgramData\StevensSupportHelper\AdminUpdates\StevensSupportHelper.Installer.exe'
$configPath = 'C:\ProgramData\StevensSupportHelper\AdminUpdates\client.installer.config'
$serviceName = 'StevensSupportHelperClientService'
$trayProcessName = 'StevensSupportHelper.Client.Tray'

Write-Host "Remote update started for $DeviceName on $env:COMPUTERNAME"

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer not found at $installerPath"
}

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "client.installer.config not found at $configPath"
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}

$trayProcesses = @(Get-Process -Name $trayProcessName -ErrorAction SilentlyContinue)
if ($trayProcesses.Count -gt 0) {
    $trayProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
}

$process = Start-Process -FilePath $installerPath -WorkingDirectory (Split-Path -Path $installerPath -Parent) -ArgumentList '--silent --update-only' -PassThru -Wait
if ($process.ExitCode -ne 0) {
    throw "Installer failed with exit code $($process.ExitCode)"
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Start-Service -Name $serviceName -ErrorAction Stop
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
}

Write-Host "Remote update finished successfully."
Get-Service -Name $serviceName -ErrorAction SilentlyContinue | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "start_client_service.ps1",
                "Startet den StevensSupportHelper-Clientdienst.",
                true,
                header + """

$serviceName = 'StevensSupportHelperClientService'
Start-Service -Name $serviceName
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "stop_client_service.ps1",
                "Stoppt den StevensSupportHelper-Clientdienst.",
                true,
                header + """

$serviceName = 'StevensSupportHelperClientService'
Stop-Service -Name $serviceName -Force
Get-Service -Name $serviceName | Select-Object Name, Status | Format-Table -AutoSize | Out-String -Width 4096
"""),
            new RemoteActionScriptMetadata(
                "show_client_runtime.ps1",
                "Zeigt Laufzeit- und Verbindungsinformationen des Clients an.",
                false,
                header + """

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

public sealed record RemoteActionCatalogEntry(
    string Name,
    string Description,
    bool RequiresElevation,
    string ScriptContent);

internal sealed record RemoteActionScriptMetadata(
    string FileName,
    string Description,
    bool RequiresElevation,
    string Content);
