param(
    [string]$InstallRoot = "$env:ProgramFiles\StevensSupportHelper",
    [string]$ServiceName = 'StevensSupportHelperClientService'
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'uninstall-client.ps1 must be run in an elevated PowerShell session.'
    }
}

function Wait-ForServiceDeletion {
    param(
        [string]$Name,
        [int]$TimeoutSeconds = 20
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        if (-not (Get-Service -Name $Name -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

    throw ("Timed out waiting for service '{0}' to be deleted." -f $Name)
}

Assert-Administrator

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Host
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Host
    Wait-ForServiceDeletion -Name $ServiceName
}

$startupDirectories = @(
    [Environment]::GetFolderPath('Startup'),
    [Environment]::GetFolderPath('CommonStartup')
)
$startupLaunchers = @(
    (Join-Path $startupDirectories[0] 'StevensSupportHelper Client Tray.cmd'),
    (Join-Path $startupDirectories[0] 'StevensSupportHelper Client Tray.lnk'),
    (Join-Path $startupDirectories[1] 'StevensSupportHelper Client Tray.cmd'),
    (Join-Path $startupDirectories[1] 'StevensSupportHelper Client Tray.lnk')
)

foreach ($startupLauncher in $startupLaunchers) {
    if (Test-Path -LiteralPath $startupLauncher) {
        Remove-Item -LiteralPath $startupLauncher -Force
    }
}

$runKeyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKeyPath -Name 'StevensSupportHelperClientTray' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKeyPath -Name 'StevensSupportHelperClientTray' -Force
}

$traySettingsPath = Join-Path $env:ProgramData 'StevensSupportHelper\tray-settings.json'
if (Test-Path -LiteralPath $traySettingsPath) {
    Remove-Item -LiteralPath $traySettingsPath -Force
}

$dynamicSettingsPath = Join-Path $env:ProgramData 'StevensSupportHelper\dynamic-client-settings.json'
if (Test-Path -LiteralPath $dynamicSettingsPath) {
    Remove-Item -LiteralPath $dynamicSettingsPath -Force
}

$trayProcesses = Get-Process -Name 'StevensSupportHelper.Client.Tray' -ErrorAction SilentlyContinue
if ($trayProcesses) {
    $trayProcesses | Stop-Process -Force
}

if (Test-Path $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

Write-Host 'Client components removed.' -ForegroundColor Green
