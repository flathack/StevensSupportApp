param(
    [switch]$Server,
    [switch]$ClientService,
    [switch]$Tray,
    [switch]$Admin,
    [switch]$All
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $repoRoot '.runtime'
$processFile = Join-Path $runtimeDirectory 'dev-processes.json'

function Start-Component {
    param(
        [string]$Name,
        [string]$ProjectPath,
        [string[]]$AdditionalArgs = @()
    )

    $argumentList = @(
        '-NoExit',
        '-Command',
        ('Set-Location "{0}"; dotnet run --project "{1}" {2}' -f $repoRoot, $ProjectPath, ($AdditionalArgs -join ' ')).Trim()
    )

    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $argumentList -WorkingDirectory $repoRoot -PassThru
    return [pscustomobject]@{
        Name = $Name
        ProjectPath = $ProjectPath
        ProcessId = $process.Id
        StartedAtUtc = [DateTime]::UtcNow.ToString('o')
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is not available in PATH.'
}

New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null

$startAll = $All -or (-not ($Server -or $ClientService -or $Tray -or $Admin))
$startedProcesses = @()

if ($startAll -or $Server) {
    $startedProcesses += Start-Component -Name 'Server' -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Server\StevensSupportHelper.Server.csproj') -AdditionalArgs @('--launch-profile', 'http')
}

if ($startAll -or $ClientService) {
    $startedProcesses += Start-Component -Name 'ClientService' -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Client.Service\StevensSupportHelper.Client.Service.csproj')
}

if ($startAll -or $Tray) {
    $startedProcesses += Start-Component -Name 'Tray' -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Client.Tray\StevensSupportHelper.Client.Tray.csproj')
}

if ($startAll -or $Admin) {
    $startedProcesses += Start-Component -Name 'Admin' -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Admin\StevensSupportHelper.Admin.csproj')
}

$startedProcesses | ConvertTo-Json | Set-Content -Path $processFile

Write-Host ''
Write-Host 'Started components:' -ForegroundColor Green
$startedProcesses | ForEach-Object {
    Write-Host ("- {0} (PID {1})" -f $_.Name, $_.ProcessId)
}
Write-Host ''
Write-Host ("Process state file: {0}" -f $processFile)
Write-Host 'Use scripts\stop-dev.ps1 to stop the started processes.'