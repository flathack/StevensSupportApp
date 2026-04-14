using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin.Services;

public sealed class PowerShellRemoteAdminService
{
    private const int WinRmSslPort = 5986;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string _defaultRemoteUserName = string.Empty;
    private string _defaultRemotePassword = string.Empty;

    public void UpdateDefaultCredentials(string remoteUserName, string remotePassword)
    {
        _defaultRemoteUserName = remoteUserName?.Trim() ?? string.Empty;
        _defaultRemotePassword = remotePassword ?? string.Empty;
    }

    public async Task<IReadOnlyList<RemoteFileSystemEntry>> ListDirectoryAsync(ClientRow client, string? path, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var escapedPath = EscapeLiteral(path ?? string.Empty);
        var body = $@"
$targetPath = '{escapedPath}'
if ([string]::IsNullOrWhiteSpace($targetPath)) {{
    $items = Invoke-Command -Session $session -ScriptBlock {{
        Get-PSDrive -PSProvider FileSystem |
            Sort-Object Name |
            ForEach-Object {{
                [pscustomobject]@{{
                    Name = $_.Name
                    FullPath = $_.Root
                    EntryType = 'Drive'
                    Length = $null
                    LastWriteTimeUtc = $null
                }}
            }}
    }}
}}
else {{
    $items = Invoke-Command -Session $session -ArgumentList $targetPath -ScriptBlock {{
        param($remotePath)
        Get-ChildItem -LiteralPath $remotePath -Force |
            Sort-Object @{{ Expression = {{ -not $_.PSIsContainer }} }}, Name |
            ForEach-Object {{
                [pscustomobject]@{{
                    Name = $_.Name
                    FullPath = $_.FullName
                    EntryType = if ($_.PSIsContainer) {{ 'Directory' }} else {{ 'File' }}
                    Length = if ($_.PSIsContainer) {{ $null }} else {{ [long]$_.Length }}
                    LastWriteTimeUtc = if ($_.LastWriteTimeUtc) {{ $_.LastWriteTimeUtc.ToString('O') }} else {{ $null }}
                }}
            }}
    }}
}}

$items | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeMany<RemoteFileSystemEntry>(output);
    }

    public async Task CreateDirectoryAsync(ClientRow client, string targetPath, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(targetPath)}' -ScriptBlock {{
    param($remotePath)
    New-Item -ItemType Directory -Path $remotePath -Force | Out-Null
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task DeletePathAsync(ClientRow client, string targetPath, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(targetPath)}' -ScriptBlock {{
    param($remotePath)
    Remove-Item -LiteralPath $remotePath -Recurse -Force
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task UploadFileAsync(ClientRow client, string localPath, string remotePath, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException("The selected local file does not exist.", localPath);
        }

        var escapedLocalPath = EscapeLiteral(localPath);
        var escapedRemotePath = EscapeLiteral(remotePath);
        var body = $@"
$remotePath = '{escapedRemotePath}'
$remoteDirectory = Split-Path -Path $remotePath -Parent
if (-not [string]::IsNullOrWhiteSpace($remoteDirectory)) {{
    Invoke-Command -Session $session -ArgumentList $remoteDirectory -ScriptBlock {{
        param($path)
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }}
}}

Copy-Item -LiteralPath '{escapedLocalPath}' -Destination $remotePath -ToSession $session -Force";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task DownloadFileAsync(ClientRow client, string remotePath, string localPath, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var localDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(localDirectory))
        {
            Directory.CreateDirectory(localDirectory);
        }

        var body = $@"Copy-Item -LiteralPath '{EscapeLiteral(remotePath)}' -Destination '{EscapeLiteral(localPath)}' -FromSession $session -Force";
        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteProcessInfo>> ListProcessesAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    Get-Process |
        Sort-Object ProcessName |
        ForEach-Object {
            [pscustomobject]@{
                Id = $_.Id
                ProcessName = $_.ProcessName
                MainWindowTitle = $_.MainWindowTitle
                CpuSeconds = if ($_.CPU -ne $null) { [math]::Round($_.CPU, 1) } else { $null }
                WorkingSetMb = [math]::Round($_.WorkingSet64 / 1MB, 1)
                StartTimeUtc = try { $_.StartTime.ToUniversalTime().ToString('O') } catch { $null }
            }
        }
} | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeMany<RemoteProcessInfo>(output);
    }

    public async Task<RemoteSystemSummary> GetSystemSummaryAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    $os = Get-CimInstance Win32_OperatingSystem
    $processors = @(Get-CimInstance Win32_Processor)
    $usedMemoryBytes = ([double]$os.TotalVisibleMemorySize - [double]$os.FreePhysicalMemory) * 1KB
    $totalMemoryBytes = [double]$os.TotalVisibleMemorySize * 1KB
    $cpuPercent = if ($processors.Count -gt 0) {
        [math]::Round((($processors | Measure-Object -Property LoadPercentage -Average).Average), 1)
    }
    else {
        0
    }

    [pscustomobject]@{
        ProcessCount = @(Get-Process).Count
        CpuPercent = $cpuPercent
        UsedMemoryGb = [math]::Round($usedMemoryBytes / 1GB, 2)
        TotalMemoryGb = [math]::Round($totalMemoryBytes / 1GB, 2)
        MemoryPercent = if ($totalMemoryBytes -gt 0) { [math]::Round(($usedMemoryBytes / $totalMemoryBytes) * 100, 1) } else { 0 }
    }
} | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeSingle<RemoteSystemSummary>(output);
    }

    public async Task<RemoteProcessInfo> StartProcessAsync(ClientRow client, string filePath, string? arguments, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(filePath)}', '{EscapeLiteral(arguments ?? string.Empty)}' -ScriptBlock {{
    param($remoteFilePath, $remoteArguments)
    $process = if ([string]::IsNullOrWhiteSpace($remoteArguments)) {{
        Start-Process -FilePath $remoteFilePath -PassThru
    }}
    else {{
        Start-Process -FilePath $remoteFilePath -ArgumentList $remoteArguments -PassThru
    }}

    [pscustomobject]@{{
        Id = $process.Id
        ProcessName = $process.ProcessName
        MainWindowTitle = $process.MainWindowTitle
        CpuSeconds = $null
        WorkingSetMb = 0
        StartTimeUtc = (Get-Date).ToUniversalTime().ToString('O')
    }}
}} | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeSingle<RemoteProcessInfo>(output);
    }

    public async Task KillProcessAsync(ClientRow client, int processId, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList {processId} -ScriptBlock {{
    param($targetProcessId)
    Stop-Process -Id $targetProcessId -Force
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<string> ExecuteCommandAsync(ClientRow client, string commandText, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ScriptBlock {{
    {commandText}
}} | Out-String -Width 4096";

        return await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteRegistryEntry>> ListRegistryValuesAsync(ClientRow client, string registryPath, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var (hive, subKeyPath) = ParseRegistryPath(registryPath);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(hive.ToString())}', '{EscapeLiteral(subKeyPath)}' -ScriptBlock {{
    param($remoteHive, $remoteSubKeyPath)
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::$remoteHive, [Microsoft.Win32.RegistryView]::Default)
    $key = if ([string]::IsNullOrWhiteSpace($remoteSubKeyPath)) {{ $baseKey }} else {{ $baseKey.OpenSubKey($remoteSubKeyPath, $false) }}
    if ($null -eq $key) {{
        throw 'Registry path not found.'
    }}
    $items = @()
    try {{
        $propertyNames = $key.GetValueNames()
        foreach ($propertyName in $propertyNames) {{
            $kind = $key.GetValueKind($propertyName).ToString()
            $value = $key.GetValue($propertyName)
            $items += [pscustomobject]@{{
                Name = [string]$propertyName
                Kind = [string]$kind
                Value = if ($value -is [array]) {{ ($value -join ', ') }} else {{ [string]$value }}
            }}
        }}
    }}
    finally {{
        if ($key -ne $baseKey) {{ $key.Close() }}
        $baseKey.Close()
    }}
    @($items)
}} | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeMany<RemoteRegistryEntry>(output);
    }

    public async Task<IReadOnlyList<string>> ListRegistrySubKeysAsync(ClientRow client, string registryPath, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var (hive, subKeyPath) = ParseRegistryPath(registryPath);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(hive.ToString())}', '{EscapeLiteral(subKeyPath)}' -ScriptBlock {{
    param($remoteHive, $remoteSubKeyPath)
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::$remoteHive, [Microsoft.Win32.RegistryView]::Default)
    $key = if ([string]::IsNullOrWhiteSpace($remoteSubKeyPath)) {{ $baseKey }} else {{ $baseKey.OpenSubKey($remoteSubKeyPath, $false) }}
    if ($null -eq $key) {{
        throw 'Registry path not found.'
    }}
    try {{
        @($key.GetSubKeyNames() | Sort-Object)
    }}
    finally {{
        if ($key -ne $baseKey) {{ $key.Close() }}
        $baseKey.Close()
    }}
}} | ConvertTo-Json -Depth 3 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeRegistrySubKeys(output);
    }

    public async Task SetRegistryStringValueAsync(ClientRow client, string registryPath, string name, string value, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var (hive, subKeyPath) = ParseRegistryPath(registryPath);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(hive.ToString())}', '{EscapeLiteral(subKeyPath)}', '{EscapeLiteral(name)}', '{EscapeLiteral(value)}' -ScriptBlock {{
    param($remoteHive, $remoteSubKeyPath, $entryName, $entryValue)
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::$remoteHive, [Microsoft.Win32.RegistryView]::Default)
    try {{
        $key = $baseKey.CreateSubKey($remoteSubKeyPath)
        if ($null -eq $key) {{
            throw 'Registry path could not be created.'
        }}
        try {{
            $key.SetValue($entryName, $entryValue, [Microsoft.Win32.RegistryValueKind]::String)
        }}
        finally {{
            $key.Close()
        }}
    }}
    finally {{
        $baseKey.Close()
    }}
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task DeleteRegistryValueAsync(ClientRow client, string registryPath, string name, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var (hive, subKeyPath) = ParseRegistryPath(registryPath);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(hive.ToString())}', '{EscapeLiteral(subKeyPath)}', '{EscapeLiteral(name)}' -ScriptBlock {{
    param($remoteHive, $remoteSubKeyPath, $entryName)
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::$remoteHive, [Microsoft.Win32.RegistryView]::Default)
    $key = if ([string]::IsNullOrWhiteSpace($remoteSubKeyPath)) {{ $baseKey }} else {{ $baseKey.OpenSubKey($remoteSubKeyPath, $true) }}
    if ($null -eq $key) {{
        throw 'Registry path not found.'
    }}
    try {{
        $key.DeleteValue($entryName, $false)
    }}
    finally {{
        if ($key -ne $baseKey) {{ $key.Close() }}
        $baseKey.Close()
    }}
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteSoftwarePackage>> ListInstalledSoftwareAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    $paths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    Get-ItemProperty -Path $paths -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.DisplayName) } |
        Sort-Object DisplayName |
        ForEach-Object {
            [pscustomobject]@{
                DisplayName = $_.DisplayName
                Publisher = $_.Publisher
                Version = $_.DisplayVersion
                QuietUninstallCommand = $_.QuietUninstallString
                UninstallCommand = $_.UninstallString
                ProductCode = if ($_.PSChildName -match '^\{[0-9A-Fa-f\-]+\}$') { $_.PSChildName } else { $null }
                WindowsInstaller = [bool]$_.WindowsInstaller
                Source = 'Registry'
            }
        }
} | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeMany<RemoteSoftwarePackage>(output);
    }

    public async Task InstallWingetPackageAsync(ClientRow client, string packageId, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(packageId)}' -ScriptBlock {{
    param($remotePackageId)
    winget install --id $remotePackageId -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --source winget
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<string> RunWingetUpdateAllAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    winget update --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-String -Width 4096
}";

        return await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<byte[]> CaptureScreenshotAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        if (!client.HasInteractiveUser)
        {
            throw new InvalidOperationException("A screenshot is only available while a user session is actively signed in.");
        }

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    $captureRoot = Join-Path $env:ProgramData 'StevensSupportHelper\ScreenshotCapture'
    $captureId = [guid]::NewGuid().ToString('N')
    $scriptPath = Join-Path $captureRoot ($captureId + '.ps1')
    $imagePath = Join-Path $captureRoot ($captureId + '.png')
    $taskName = 'StevensSupportHelper-Screenshot-' + $captureId

    try {
        New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null

        $activeUser = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName
        if ([string]::IsNullOrWhiteSpace($activeUser)) {
            throw 'No interactive user session is available for screenshot capture.'
        }

        $captureScript = @'
param(
    [string]$OutputPath
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($bounds.Width -le 0 -or $bounds.Height -le 0) {
    throw 'The active desktop has no visible screen bounds.'
}

$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
'@

        Set-Content -LiteralPath $scriptPath -Value $captureScript -Encoding UTF8

        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -File ""{0}"" -OutputPath ""{1}""' -f $scriptPath, $imagePath)
        $principal = New-ScheduledTaskPrincipal -UserId $activeUser -LogonType Interactive -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -StartWhenAvailable
        $task = New-ScheduledTask -Action $action -Principal $principal -Settings $settings

        try {
            Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
            Start-ScheduledTask -TaskName $taskName

            $deadline = (Get-Date).AddSeconds(20)
            do {
                Start-Sleep -Milliseconds 300
            } while ((-not (Test-Path -LiteralPath $imagePath)) -and (Get-Date) -lt $deadline)

            if (-not (Test-Path -LiteralPath $imagePath)) {
                throw 'Timed out waiting for the interactive screenshot task to produce an image.'
            }

            [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($imagePath))
        }
        finally {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        }
    }
    finally {
        Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $imagePath -Force -ErrorAction SilentlyContinue
    }
}";

        var output = (await ExecuteSessionScriptAsync(client, body, cancellationToken)).Trim();
        return Convert.FromBase64String(output);
    }

    public async Task<IReadOnlyList<RemotePowerPlan>> ListPowerPlansAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    $activeOutput = powercfg /getactivescheme
    $activeMatch = [regex]::Match(($activeOutput -join ' '), '([A-Fa-f0-9\-]{36})')
    $activeGuid = if ($activeMatch.Success) { $activeMatch.Groups[1].Value.ToLowerInvariant() } else { '' }
    powercfg /list | ForEach-Object {
        $line = $_.Trim()
        if ($line -match 'Power Scheme GUID:\s+([A-Fa-f0-9\-]{36})\s+\((.+?)\)(\s+\*)?') {
            [pscustomobject]@{
                Guid = $matches[1]
                Name = $matches[2]
                IsActive = ($matches[1].ToLowerInvariant() -eq $activeGuid)
            }
        }
    }
} | ConvertTo-Json -Depth 4 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeMany<RemotePowerPlan>(output);
    }

    public async Task SetActivePowerPlanAsync(ClientRow client, string planGuid, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(planGuid)}' -ScriptBlock {{
    param($remotePlanGuid)
    powercfg /setactive $remotePlanGuid | Out-Null
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteWindowsUpdateItem>> ListAvailableWindowsUpdatesAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    $session = New-Object -ComObject Microsoft.Update.Session
    $searcher = $session.CreateUpdateSearcher()
    $searchResult = $searcher.Search('IsInstalled=0 and Type=''Software''')
    $items = @()
    foreach ($update in $searchResult.Updates) {
        $kbIds = @($update.KBArticleIDs) -join ', '
        $categories = @($update.Categories | ForEach-Object { $_.Name }) -join ', '
        $items += [pscustomobject]@{
            Title = $update.Title
            KbArticleIds = $kbIds
            Categories = $categories
            IsDownloaded = [bool]$update.IsDownloaded
            MaxDownloadSizeBytes = [long]$update.MaxDownloadSize
        }
    }
    @($items)
} | ConvertTo-Json -Depth 5 -Compress";

        var output = await ExecuteSessionScriptAsync(client, body, cancellationToken);
        return DeserializeMany<RemoteWindowsUpdateItem>(output);
    }

    public async Task<string> InstallAvailableWindowsUpdatesAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);

        var body =
@"Invoke-Command -Session $session -ScriptBlock {
    $session = New-Object -ComObject Microsoft.Update.Session
    $searcher = $session.CreateUpdateSearcher()
    $searchResult = $searcher.Search('IsInstalled=0 and Type=''Software''')
    if ($searchResult.Updates.Count -eq 0) {
        return 'No Windows updates are pending.'
    }

    $updatesToInstall = New-Object -ComObject Microsoft.Update.UpdateColl
    foreach ($update in $searchResult.Updates) {
        [void]$updatesToInstall.Add($update)
    }

    $downloader = $session.CreateUpdateDownloader()
    $downloader.Updates = $updatesToInstall
    [void]$downloader.Download()

    $installer = $session.CreateUpdateInstaller()
    $installer.Updates = $updatesToInstall
    $result = $installer.Install()

    'Windows Update install result: ' + $result.ResultCode + ' | Reboot required: ' + $result.RebootRequired + ' | Updates processed: ' + $updatesToInstall.Count
}";

        return await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task SendUserMessageAsync(ClientRow client, string message, CancellationToken cancellationToken)
    {
        EnsureMaintenanceClient(client);

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(message)}' -ScriptBlock {{
    param($remoteMessage)
    msg.exe * /TIME:60 $remoteMessage | Out-Null
}}";

        await ExecuteDirectSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task UninstallSoftwareAsync(ClientRow client, RemoteSoftwarePackage package, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var uninstallCommand = !string.IsNullOrWhiteSpace(package.QuietUninstallCommand)
            ? package.QuietUninstallCommand
            : package.UninstallCommand;

        if (string.IsNullOrWhiteSpace(uninstallCommand) && package.WindowsInstaller && !string.IsNullOrWhiteSpace(package.ProductCode))
        {
            uninstallCommand = $"msiexec.exe /x {package.ProductCode}";
        }

        if (string.IsNullOrWhiteSpace(uninstallCommand))
        {
            uninstallCommand = $"winget uninstall --name \"{package.DisplayName.Replace("\"", "\\\"", StringComparison.Ordinal)}\" --exact --silent --accept-source-agreements --disable-interactivity";
        }

        var command = EnsureSilentUninstallCommand(uninstallCommand);
        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(command)}' -ScriptBlock {{
    param($remoteCommand)
    cmd.exe /c $remoteCommand
}}";

        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task RestartComputerAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var body = @"Invoke-Command -Session $session -ScriptBlock { Restart-Computer -Force }";
        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task ShutdownComputerAsync(ClientRow client, CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        var body = @"Invoke-Command -Session $session -ScriptBlock { Stop-Computer -Force }";
        await ExecuteSessionScriptAsync(client, body, cancellationToken);
    }

    public async Task<RemoteInstallerLaunchResult> StageAndRunClientInstallerAsync(
        ClientRow client,
        string installerPath,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        EnsureWinRmClient(client);
        return await StageAndRunInstallerCoreAsync(
            client,
            installerPath,
            progress,
            cancellationToken,
            useDirectMaintenanceSession: false);
    }

    public async Task<RemoteInstallerLaunchResult> RepairClientAsync(
        ClientRow client,
        string installerPath,
        string installerConfigText,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        EnsureMaintenanceClient(client);
        return await StageAndRunInstallerWithConfigCoreAsync(
            client,
            installerPath,
            installerConfigText,
            progress,
            cancellationToken,
            useDirectMaintenanceSession: true,
            installerArguments: "--silent");
    }

    public async Task<ClientInstallerConfigLoadResult> LoadClientInstallerConfigAsync(
        ClientRow client,
        CancellationToken cancellationToken)
    {
        EnsureMaintenanceClient(client);

        var body = @"
Invoke-Command -Session $session -ScriptBlock {
    $programDataRoot = Join-Path $env:ProgramData 'StevensSupportHelper'
    $savedConfigPath = Join-Path $programDataRoot 'client.installer.config'
    $installerStatePath = Join-Path $programDataRoot 'installer-state.json'
    $dynamicSettingsPath = Join-Path $programDataRoot 'dynamic-client-settings.json'
    $defaultInstallRoot = 'C:\Program Files\StevensSupportHelper'
    $defaultServiceName = 'StevensSupportHelperClientService'
    $serviceSettingsPath = Join-Path $defaultInstallRoot 'client-service\appsettings.json'

    function Read-JsonFile {
        param([string]$Path)
        if (-not (Test-Path -LiteralPath $Path)) {
            return $null
        }

        try {
            return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        }
        catch {
            return $null
        }
    }

    function Coalesce-String {
        param([Parameter(ValueFromRemainingArguments = $true)] [object[]]$Values)
        foreach ($value in $Values) {
            if ($null -ne $value) {
                $text = [string]$value
                if (-not [string]::IsNullOrWhiteSpace($text)) {
                    return $text.Trim()
                }
            }
        }

        return ''
    }

    function Coalesce-Bool {
        param([Parameter(ValueFromRemainingArguments = $true)] [object[]]$Values)
        foreach ($value in $Values) {
            if ($null -ne $value) {
                return [bool]$value
            }
        }

        return $false
    }

    function Coalesce-Array {
        param([Parameter(ValueFromRemainingArguments = $true)] [object[]]$Values)
        foreach ($value in $Values) {
            if ($null -eq $value) {
                continue
            }

            $results = @($value | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object { ([string]$_).Trim() })
            if ($results.Count -gt 0) {
                return @($results | Select-Object -Unique)
            }
        }

        return @()
    }

    if (Test-Path -LiteralPath $savedConfigPath) {
        $configText = Get-Content -LiteralPath $savedConfigPath -Raw
        [pscustomobject]@{
            ConfigTextBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($configText))
            IsSynthesized = $false
            Message = ""Loaded saved client.installer.config from $savedConfigPath.""
        }
    }
    else {
        $serviceSettings = Read-JsonFile -Path $serviceSettingsPath
        $dynamicSettings = Read-JsonFile -Path $dynamicSettingsPath
        $installerState = Read-JsonFile -Path $installerStatePath
        $serviceOptions = if ($serviceSettings) { $serviceSettings.StevensSupportHelper } else { $null }
        $dynamicOptions = if ($dynamicSettings) { $dynamicSettings.StevensSupportHelper } else { $null }

        $configObject = [ordered]@{
            serverUrl = Coalesce-String $serviceOptions.ServerBaseUrl $installerState.ServerUrl
            installRoot = $defaultInstallRoot
            serviceName = $defaultServiceName
            deviceName = Coalesce-String $serviceOptions.DeviceName $installerState.DeviceName $env:COMPUTERNAME
            registrationSharedKey = Coalesce-String $serviceOptions.RegistrationSharedKey $installerState.RegistrationSharedKey
            installRustDesk = $false
            rustDeskInstallerFileName = ''
            installTailscale = $false
            tailscaleInstallerFileName = ''
            tailscaleAuthKey = ''
            enableAutoApprove = Coalesce-Bool $dynamicOptions.AutoApproveSupportRequests $serviceOptions.AutoApproveSupportRequests $installerState.EnableAutoApprove
            enableRdp = Coalesce-Bool $installerState.EnableRdp
            createServiceUser = Coalesce-Bool $installerState.CreateServiceUser
            serviceUserIsAdministrator = Coalesce-Bool $installerState.ServiceUserIsAdministrator
            serviceUserName = Coalesce-String $installerState.ServiceUserName
            serviceUserPassword = ''
            rustDeskId = Coalesce-String $dynamicOptions.RustDeskId $serviceOptions.RustDeskId $installerState.RustDeskId
            rustDeskPassword = ''
            tailscaleIpAddresses = Coalesce-Array $dynamicOptions.TailscaleIpAddresses $serviceOptions.TailscaleIpAddresses $installerState.TailscaleIpAddresses
            silent = $true
        }

        $configText = $configObject | ConvertTo-Json -Depth 10
        [pscustomobject]@{
            ConfigTextBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($configText))
            IsSynthesized = $true
            Message = 'Saved client.installer.config not found on the client. Generated an editable config from the installed client state.'
        }
    }
} | ConvertTo-Json -Depth 6 -Compress";

        var output = await ExecuteDirectSessionScriptAsync(client, body, cancellationToken);
        var payload = DeserializeSingle<RemoteInstallerConfigPayload>(output);
        var configText = Encoding.UTF8.GetString(Convert.FromBase64String(payload.ConfigTextBase64));
        return new ClientInstallerConfigLoadResult(configText, payload.IsSynthesized, payload.Message);
    }

    public async Task<RemoteInstallerLaunchResult> UpdateClientWithConfigAsync(
        ClientRow client,
        string installerPath,
        string installerConfigText,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        EnsureMaintenanceClient(client);
        return await StageAndRunInstallerWithConfigCoreAsync(
            client,
            installerPath,
            installerConfigText,
            progress,
            cancellationToken,
            useDirectMaintenanceSession: true,
            installerArguments: "--silent --update-only");
    }

    public async Task<RepairPrecheckResult> CheckRepairReadinessAsync(ClientRow client, CancellationToken cancellationToken)
    {
        var targetHost = ResolveRemoteTarget(client);
        if (string.IsNullOrWhiteSpace(targetHost))
        {
            return new RepairPrecheckResult(
                string.Empty,
                string.Empty,
                false,
                false,
                "No WinRM target is available. Add a Tailscale IP or machine name.");
        }

        var effectiveUserName = !string.IsNullOrWhiteSpace(client.RemoteUserName)
            ? client.RemoteUserName.Trim()
            : _defaultRemoteUserName;
        var hasCredentials = !string.IsNullOrWhiteSpace(effectiveUserName) &&
            !string.IsNullOrWhiteSpace(!string.IsNullOrWhiteSpace(client.RemotePassword) ? client.RemotePassword : _defaultRemotePassword);
        if (!hasCredentials)
        {
            return new RepairPrecheckResult(
                targetHost,
                effectiveUserName,
                false,
                false,
                "Remote credentials are missing. Configure username and password in client metadata or admin settings.");
        }

        try
        {
            var output = await ExecuteDirectSessionScriptAsync(
                client,
                @"Invoke-Command -Session $session -ScriptBlock { 'WinRM OK' }",
                cancellationToken);
            return new RepairPrecheckResult(
                targetHost,
                effectiveUserName,
                true,
                output.Contains("WinRM OK", StringComparison.OrdinalIgnoreCase),
                "WinRM session was established successfully.");
        }
        catch (Exception exception)
        {
            return new RepairPrecheckResult(
                targetHost,
                effectiveUserName,
                true,
                false,
                $"WinRM precheck failed: {exception.Message}");
        }
    }

    public async Task<string> ExecuteRemoteActionScriptAsync(ClientRow client, string scriptContent, CancellationToken cancellationToken)
    {
        EnsureMaintenanceClient(client);

        var body = $@"
$clientId = '{EscapeLiteral(client.ClientId.ToString())}'
$deviceName = '{EscapeLiteral(client.DeviceName)}'
$machineName = '{EscapeLiteral(client.MachineName)}'
$currentUser = '{EscapeLiteral(client.CurrentUser)}'
$agentVersion = '{EscapeLiteral(client.AgentVersion)}'
$rustDeskId = '{EscapeLiteral(client.RustDeskId)}'
$notes = '{EscapeLiteral(client.Notes)}'
$tailscaleIpAddresses = @({string.Join(", ", client.TailscaleIpAddresses.Select(address => $"'{EscapeLiteral(address)}'"))})

Invoke-Command -Session $session -ArgumentList $clientId, $deviceName, $machineName, $currentUser, $agentVersion, $rustDeskId, $notes, $tailscaleIpAddresses -ScriptBlock {{
    param($clientId, $deviceName, $machineName, $currentUser, $agentVersion, $rustDeskId, $notes, $tailscaleIpAddresses)
    {scriptContent}
}} | Out-String -Width 4096";

        return await ExecuteDirectSessionScriptAsync(client, body, cancellationToken);
    }

    private async Task<RemoteInstallerLaunchResult> StageAndRunInstallerCoreAsync(
        ClientRow client,
        string installerPath,
        Action<string>? progress,
        CancellationToken cancellationToken,
        bool useDirectMaintenanceSession)
    {
        progress?.Invoke($"Validating installer on admin machine: {installerPath}");

        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("The selected installer does not exist.", installerPath);
        }

        var remoteUpdateRoot = $@"C:\ProgramData\StevensSupportHelper\AdminUpdates\{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        progress?.Invoke($"Creating remote update folder: {remoteUpdateRoot}");
        if (useDirectMaintenanceSession)
        {
            await CreateDirectoryDirectAsync(client, remoteUpdateRoot, cancellationToken);
        }
        else
        {
            await CreateDirectoryAsync(client, remoteUpdateRoot, cancellationToken);
        }

        var remoteInstallerPath = Path.Combine(remoteUpdateRoot, Path.GetFileName(installerPath));
        var localFileInfo = new FileInfo(installerPath);
        progress?.Invoke($"Upload update to client: {localFileInfo.Name} ({localFileInfo.Length / 1024d / 1024d:0.0} MB)");
        if (useDirectMaintenanceSession)
        {
            await UploadFileDirectAsync(client, installerPath, remoteInstallerPath, cancellationToken);
        }
        else
        {
            await UploadFileAsync(client, installerPath, remoteInstallerPath, cancellationToken);
        }
        progress?.Invoke($"Upload completed: {remoteInstallerPath}");

        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(remoteInstallerPath)}' -ScriptBlock {{
    param($remoteInstallerPath)
    $workingDirectory = Split-Path -Path $remoteInstallerPath -Parent
    if (-not (Test-Path -LiteralPath $remoteInstallerPath)) {{
        throw 'Uploaded installer could not be found on the client.'
    }}

    $stdoutPath = Join-Path $workingDirectory 'installer-stdout.log'
    $stderrPath = Join-Path $workingDirectory 'installer-stderr.log'
    $process = Start-Process -FilePath $remoteInstallerPath -WorkingDirectory $workingDirectory -ArgumentList '--silent --update-only' -PassThru -Wait -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    [pscustomobject]@{{
        RemoteUpdateRoot = $workingDirectory
        RemoteInstallerPath = $remoteInstallerPath
        ProcessId = $process.Id
        StartedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        ExitCode = $process.ExitCode
        StdOutPath = $stdoutPath
        StdErrPath = $stderrPath
        StandardOutput = if (Test-Path -LiteralPath $stdoutPath) {{ Get-Content -LiteralPath $stdoutPath -Raw }} else {{ '' }}
        StandardError = if (Test-Path -LiteralPath $stderrPath) {{ Get-Content -LiteralPath $stderrPath -Raw }} else {{ '' }}
    }}
}} | ConvertTo-Json -Depth 4 -Compress";

        progress?.Invoke("Starting remote installer process and waiting for completion...");
        var output = useDirectMaintenanceSession
            ? await ExecuteDirectSessionScriptAsync(client, body, cancellationToken)
            : await ExecuteSessionScriptAsync(client, body, cancellationToken);
        var result = DeserializeSingle<RemoteInstallerLaunchResult>(output);
        progress?.Invoke($"Remote installer finished with exit code {result.ExitCode} (PID {result.ProcessId}).");
        return result;
    }

    private async Task<RemoteInstallerLaunchResult> StageAndRunInstallerWithConfigCoreAsync(
        ClientRow client,
        string installerPath,
        string installerConfigText,
        Action<string>? progress,
        CancellationToken cancellationToken,
        bool useDirectMaintenanceSession,
        string installerArguments)
    {
        progress?.Invoke($"Validating installer on admin machine: {installerPath}");

        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("The selected installer does not exist.", installerPath);
        }

        var remoteUpdateRoot = $@"C:\ProgramData\StevensSupportHelper\AdminUpdates\{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        progress?.Invoke($"Creating remote repair folder: {remoteUpdateRoot}");
        if (useDirectMaintenanceSession)
        {
            await CreateDirectoryDirectAsync(client, remoteUpdateRoot, cancellationToken);
        }
        else
        {
            await CreateDirectoryAsync(client, remoteUpdateRoot, cancellationToken);
        }

        var remoteInstallerPath = Path.Combine(remoteUpdateRoot, Path.GetFileName(installerPath));
        var localFileInfo = new FileInfo(installerPath);
        progress?.Invoke($"Upload repair installer to client: {localFileInfo.Name} ({localFileInfo.Length / 1024d / 1024d:0.0} MB)");
        if (useDirectMaintenanceSession)
        {
            await UploadFileDirectAsync(client, installerPath, remoteInstallerPath, cancellationToken);
        }
        else
        {
            await UploadFileAsync(client, installerPath, remoteInstallerPath, cancellationToken);
        }

        var tempConfigPath = Path.Combine(Path.GetTempPath(), $"shh-installer-config-{client.ClientId:N}.json");
        try
        {
            await File.WriteAllTextAsync(tempConfigPath, installerConfigText, Encoding.UTF8, cancellationToken);
            var remoteConfigPath = Path.Combine(remoteUpdateRoot, "client.installer.config");
            progress?.Invoke("Uploading client.installer.config beside installer...");
            if (useDirectMaintenanceSession)
            {
                await UploadFileDirectAsync(client, tempConfigPath, remoteConfigPath, cancellationToken);
            }
            else
            {
                await UploadFileAsync(client, tempConfigPath, remoteConfigPath, cancellationToken);
            }

            var remotePersistentConfigPath = @"C:\ProgramData\StevensSupportHelper\client.installer.config";
            progress?.Invoke("Saving current client.installer.config on the client...");
            if (useDirectMaintenanceSession)
            {
                await UploadFileDirectAsync(client, tempConfigPath, remotePersistentConfigPath, cancellationToken);
            }
            else
            {
                await UploadFileAsync(client, tempConfigPath, remotePersistentConfigPath, cancellationToken);
            }

            var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(remoteInstallerPath)}', '{EscapeLiteral(installerArguments)}' -ScriptBlock {{
    param($remoteInstallerPath, $remoteInstallerArguments)
    $workingDirectory = Split-Path -Path $remoteInstallerPath -Parent
    $configPath = Join-Path $workingDirectory 'client.installer.config'
    if (-not (Test-Path -LiteralPath $remoteInstallerPath)) {{
        throw 'Uploaded installer could not be found on the client.'
    }}
    if (-not (Test-Path -LiteralPath $configPath)) {{
        throw 'client.installer.config was not uploaded to the client.'
    }}

    $stdoutPath = Join-Path $workingDirectory 'installer-stdout.log'
    $stderrPath = Join-Path $workingDirectory 'installer-stderr.log'
    $process = Start-Process -FilePath $remoteInstallerPath -WorkingDirectory $workingDirectory -ArgumentList $remoteInstallerArguments -PassThru -Wait -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    [pscustomobject]@{{
        RemoteUpdateRoot = $workingDirectory
        RemoteInstallerPath = $remoteInstallerPath
        ProcessId = $process.Id
        StartedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        ExitCode = $process.ExitCode
        StdOutPath = $stdoutPath
        StdErrPath = $stderrPath
        StandardOutput = if (Test-Path -LiteralPath $stdoutPath) {{ Get-Content -LiteralPath $stdoutPath -Raw }} else {{ '' }}
        StandardError = if (Test-Path -LiteralPath $stderrPath) {{ Get-Content -LiteralPath $stderrPath -Raw }} else {{ '' }}
    }}
}} | ConvertTo-Json -Depth 4 -Compress";

            progress?.Invoke("Starting remote repair installer and waiting for completion...");
            var output = useDirectMaintenanceSession
                ? await ExecuteDirectSessionScriptAsync(client, body, cancellationToken)
                : await ExecuteSessionScriptAsync(client, body, cancellationToken);
            var result = DeserializeSingle<RemoteInstallerLaunchResult>(output);
            progress?.Invoke($"Remote repair installer finished with exit code {result.ExitCode} (PID {result.ProcessId}).");
            return result;
        }
        finally
        {
            try
            {
                if (File.Exists(tempConfigPath))
                {
                    File.Delete(tempConfigPath);
                }
            }
            catch
            {
            }
        }
    }

    public void LaunchInteractivePowerShellSession(ClientRow client)
    {
        EnsureWinRmClient(client);

        var executablePath = ResolvePowerShellExecutable();
        var credentials = ResolveCredentials(client);
        var script = BuildInteractiveSessionScript(ResolveRemoteTarget(client), credentials);
        var command = $"& {{ {script} }}";
        var escapedCommand = command.Replace("\"", "`\"", StringComparison.Ordinal);

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"{escapedCommand}\"",
            UseShellExecute = true
        });
    }

    private static void EnsureWinRmClient(ClientRow client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (!client.IsOnline)
        {
            throw new InvalidOperationException($"{client.DeviceName} is currently offline.");
        }

        if (client.IsDirectAdminAccessAvailable)
        {
            return;
        }

        if (!client.HasLaunchableActiveSession || client.ActiveChannel is not RemoteChannel.WinRm)
        {
            throw new InvalidOperationException("This feature requires an active WinRm support session.");
        }
    }

    private void EnsureMaintenanceClient(ClientRow client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(ResolveRemoteTarget(client)))
        {
            throw new InvalidOperationException($"No WinRM target is available for {client.DeviceName}.");
        }

        _ = ResolveCredentials(client);
    }

    private async Task CreateDirectoryDirectAsync(ClientRow client, string targetPath, CancellationToken cancellationToken)
    {
        var body = $@"
Invoke-Command -Session $session -ArgumentList '{EscapeLiteral(targetPath)}' -ScriptBlock {{
    param($remotePath)
    New-Item -ItemType Directory -Path $remotePath -Force | Out-Null
}}";

        await ExecuteDirectSessionScriptAsync(client, body, cancellationToken);
    }

    private async Task UploadFileDirectAsync(ClientRow client, string localPath, string remotePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException("The selected local file does not exist.", localPath);
        }

        var escapedLocalPath = EscapeLiteral(localPath);
        var escapedRemotePath = EscapeLiteral(remotePath);
        var body = $@"
$remotePath = '{escapedRemotePath}'
$remoteDirectory = Split-Path -Path $remotePath -Parent
if (-not [string]::IsNullOrWhiteSpace($remoteDirectory)) {{
    Invoke-Command -Session $session -ArgumentList $remoteDirectory -ScriptBlock {{
        param($path)
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }}
}}

Copy-Item -LiteralPath '{escapedLocalPath}' -Destination $remotePath -ToSession $session -Force";

        await ExecuteDirectSessionScriptAsync(client, body, cancellationToken);
    }

    private async Task<string> ExecuteSessionScriptAsync(ClientRow client, string body, CancellationToken cancellationToken)
    {
        var script = BuildSessionScript(ResolveRemoteTarget(client), ResolveCredentials(client), body);
        var executablePath = ResolvePowerShellExecutable();
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var message = SanitizePowerShellMessage(string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "PowerShell remoting command failed." : message);
        }

        return standardOutput.Trim();
    }

    private async Task<string> ExecuteDirectSessionScriptAsync(ClientRow client, string body, CancellationToken cancellationToken)
    {
        var script = BuildSessionScript(ResolveRemoteTarget(client), ResolveCredentials(client), body);
        var executablePath = ResolvePowerShellExecutable();
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var message = SanitizePowerShellMessage(string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "PowerShell maintenance command failed." : message);
        }

        return standardOutput.Trim();
    }

    private string BuildInteractiveSessionScript(string machineName, ResolvedRemoteCredentials? credentials)
    {
        return
$@"$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Host.UI.RawUI.WindowTitle = 'StevensSupportHelper WinRM - {EscapeLiteral(machineName)}'
$sessionOption = New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck
{BuildCredentialScript(credentials)}
$session = $null
$remotePromptPath = ''
try {{
    Write-Host 'Connecting to {EscapeLiteral(machineName)} via WinRM/HTTPS...' -ForegroundColor Cyan
    $session = New-PSSession -ComputerName '{EscapeLiteral(machineName)}' -UseSSL -Port {WinRmSslPort} -SessionOption $sessionOption{BuildCredentialArgument(credentials)} -ErrorAction Stop
    Write-Host 'Connected. Starting interactive remote shell...' -ForegroundColor Green
    Write-Host 'Type exit to close the remote shell.' -ForegroundColor DarkGray

    while ($true) {{
        $remotePromptPath = Invoke-Command -Session $session -ScriptBlock {{ (Get-Location).Path }} -ErrorAction Stop
        $commandText = Read-Host ('[{EscapeLiteral(machineName)}] PS ' + $remotePromptPath)
        if ($null -eq $commandText) {{
            continue
        }}

        $trimmedCommand = $commandText.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmedCommand)) {{
            continue
        }}

        if ($trimmedCommand -ieq 'exit' -or $trimmedCommand -ieq 'Exit-PSSession') {{
            break
        }}

        try {{
            Invoke-Command -Session $session -ArgumentList $commandText -ScriptBlock {{
                param($remoteCommandText)
                & ([scriptblock]::Create($remoteCommandText))
            }} -ErrorAction Stop | Out-Host
        }}
        catch {{
            Write-Host $_.Exception.Message -ForegroundColor Red
        }}
    }}
}}
catch {{
    Write-Host 'WinRM precheck failed:' $_.Exception.Message -ForegroundColor Red
}}
finally {{
    if ($session) {{
        Remove-PSSession -Session $session -ErrorAction SilentlyContinue
    }}
}}";
    }

    private static string BuildSessionScript(string machineName, ResolvedRemoteCredentials? credentials, string body)
    {
        return
$@"$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$sessionOption = New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck
{BuildCredentialScript(credentials)}
$session = New-PSSession -ComputerName '{EscapeLiteral(machineName)}' -UseSSL -Port {WinRmSslPort} -SessionOption $sessionOption{BuildCredentialArgument(credentials)}
try {{
{body}
}}
finally {{
    if ($session) {{
        Remove-PSSession -Session $session
    }}
}}";
    }

    private static IReadOnlyList<T> DeserializeMany<T>(string json)
    {
        var payload = ExtractJsonPayload(json);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<T>>(payload, JsonOptions) ?? [],
            JsonValueKind.Object => [JsonSerializer.Deserialize<T>(payload, JsonOptions)!],
            JsonValueKind.String when typeof(T) == typeof(string) => [(T)(object)(document.RootElement.GetString() ?? string.Empty)],
            _ => []
        };
    }

    private static IReadOnlyList<string> DeserializeRegistrySubKeys(string json)
    {
        var payload = ExtractJsonPayload(json);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        using var document = JsonDocument.Parse(payload);
        var results = new List<string>();

        void AppendElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var stringValue = element.GetString();
                    if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        results.Add(stringValue);
                    }
                    break;
                case JsonValueKind.Object:
                    if (element.TryGetProperty("PSChildName", out var childName) && childName.ValueKind == JsonValueKind.String)
                    {
                        var value = childName.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            results.Add(value);
                        }
                    }
                    else if (element.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        var value = name.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            results.Add(value);
                        }
                    }
                    break;
            }
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in document.RootElement.EnumerateArray())
            {
                AppendElement(element);
            }
        }
        else
        {
            AppendElement(document.RootElement);
        }

        return results;
    }

    private static T DeserializeSingle<T>(string json)
    {
        var items = DeserializeMany<T>(json);
        return items.Count > 0
            ? items[0]
            : throw new InvalidOperationException("PowerShell remoting returned no payload.");
    }

    private static string EscapeLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }

    private static (RegistryHive Hive, string SubKeyPath) ParseRegistryPath(string registryPath)
    {
        var normalized = (registryPath ?? string.Empty)
            .Trim()
            .Replace("/", "\\", StringComparison.Ordinal);

        if (normalized.StartsWith("Registry::", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Registry::".Length..];
        }

        normalized = normalized.Replace("HKLM:", "HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCU:", "HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCR:", "HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase)
            .Replace("HKU:", "HKEY_USERS", StringComparison.OrdinalIgnoreCase)
            .Replace("HKCC:", "HKEY_CURRENT_CONFIG", StringComparison.OrdinalIgnoreCase)
            .Trim('\\');

        var firstSeparator = normalized.IndexOf('\\');
        var hiveText = firstSeparator >= 0 ? normalized[..firstSeparator] : normalized;
        var subKeyPath = firstSeparator >= 0 ? normalized[(firstSeparator + 1)..] : string.Empty;

        return hiveText.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => (RegistryHive.LocalMachine, subKeyPath),
            "HKEY_CURRENT_USER" => (RegistryHive.CurrentUser, subKeyPath),
            "HKEY_CLASSES_ROOT" => (RegistryHive.ClassesRoot, subKeyPath),
            "HKEY_USERS" => (RegistryHive.Users, subKeyPath),
            "HKEY_CURRENT_CONFIG" => (RegistryHive.CurrentConfig, subKeyPath),
            _ => throw new InvalidOperationException($"Unsupported registry hive in path '{registryPath}'.")
        };
    }

    private ResolvedRemoteCredentials? ResolveCredentials(ClientRow client)
    {
        var userName = !string.IsNullOrWhiteSpace(client.RemoteUserName)
            ? client.RemoteUserName.Trim()
            : _defaultRemoteUserName;
        var password = !string.IsNullOrWhiteSpace(client.RemotePassword)
            ? client.RemotePassword
            : _defaultRemotePassword;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException($"Remote credentials for {client.DeviceName} are missing a password.");
        }

        return new ResolvedRemoteCredentials(userName, password);
    }

    private static string BuildCredentialScript(ResolvedRemoteCredentials? credentials)
    {
        if (credentials is null)
        {
            return string.Empty;
        }

        return
$@"$securePassword = ConvertTo-SecureString '{EscapeLiteral(credentials.Password)}' -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ('{EscapeLiteral(credentials.UserName)}', $securePassword)";
    }

    private static string BuildCredentialArgument(ResolvedRemoteCredentials? credentials)
    {
        return credentials is null ? string.Empty : " -Credential $credential";
    }

    private static string ResolvePowerShellExecutable()
    {
        var candidate = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(candidate)
            ? candidate
            : Path.Combine(Environment.SystemDirectory, "powershell.exe");
    }

    private static string ResolveRemoteTarget(ClientRow client)
    {
        return client.TailscaleIpAddresses.FirstOrDefault(static address => !string.IsNullOrWhiteSpace(address))
            ?? client.MachineName;
    }

    private static string SanitizePowerShellMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return string.Empty;
        }

        var message = rawMessage.Trim();
        message = Regex.Replace(message, "<[^>]+>", " ", RegexOptions.Singleline);
        message = Regex.Replace(message, @"#<\s*CLIXML", " ", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"_x000D_|_x000A_", " ", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"Module werden fuer erstmalige Verwendung vorbereitet\.", " ", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"\s+", " ").Trim();
        return message;
    }

    private static string ExtractJsonPayload(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return string.Empty;
        }

        var trimmed = rawPayload.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[') || trimmed.StartsWith('"'))
        {
            return trimmed;
        }

        foreach (var startChar in new[] { '[', '{', '"' })
        {
            var startIndex = trimmed.IndexOf(startChar);
            if (startIndex < 0)
            {
                continue;
            }

            for (var endIndex = trimmed.Length - 1; endIndex >= startIndex; endIndex--)
            {
                var endChar = trimmed[endIndex];
                if ((startChar == '[' && endChar != ']') ||
                    (startChar == '{' && endChar != '}') ||
                    (startChar == '"' && endChar != '"'))
                {
                    continue;
                }

                var candidate = trimmed[startIndex..(endIndex + 1)].Trim();
                try
                {
                    using var _ = JsonDocument.Parse(candidate);
                    return candidate;
                }
                catch
                {
                }
            }
        }

        return trimmed;
    }

    private static string EnsureSilentUninstallCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = command.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
            if (!normalized.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            {
                normalized += " /qn /norestart";
            }

            return normalized;
        }

        if (!command.Contains("/quiet", StringComparison.OrdinalIgnoreCase) &&
            !command.Contains("/silent", StringComparison.OrdinalIgnoreCase) &&
            !command.Contains("/S", StringComparison.OrdinalIgnoreCase))
        {
            return $"{command} /quiet /norestart";
        }

        return command;
    }

    private sealed record ResolvedRemoteCredentials(string UserName, string Password);
}

public sealed record RemoteInstallerLaunchResult(
    string RemoteUpdateRoot,
    string RemoteInstallerPath,
    int ProcessId,
    string StartedAtUtc,
    int ExitCode,
    string StdOutPath,
    string StdErrPath,
    string StandardOutput,
    string StandardError);

public sealed record RemoteInstallerConfigPayload(
    string ConfigTextBase64,
    bool IsSynthesized,
    string Message);
