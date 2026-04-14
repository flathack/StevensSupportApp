param(
    [string]$ServerUrl = 'http://localhost:5000',
    [string]$InstallRoot = "$env:ProgramFiles\StevensSupportHelper",
    [string]$ServiceName = 'StevensSupportHelperClientService',
    [string]$DeviceName = $env:COMPUTERNAME,
    [string]$RegistrationSharedKey = 'change-me-registration-key',
    [bool]$ConsentRequired = $true,
    [bool]$AutoApproveSupportRequests = $false,
    [switch]$InstallRustDesk,
    [string]$RustDeskInstallerPath = '',
    [string]$RustDeskId = '',
    [switch]$PublishOnly
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'install-client.ps1 must be run in an elevated PowerShell session.'
    }
}

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$OutputPath
    )

    dotnet publish $ProjectPath -c Release -o $OutputPath | Out-Host
}

function Assert-FileExists {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ("Required file not found: {0}" -f $Path)
    }
}

function Wait-ForServiceStatus {
    param(
        [string]$Name,
        [string]$DesiredStatus,
        [int]$TimeoutSeconds = 20
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            return $DesiredStatus -eq 'Deleted'
        }

        if ($DesiredStatus -eq 'Deleted') {
            Start-Sleep -Milliseconds 500
            continue
        }

        if ($service.Status.ToString() -eq $DesiredStatus) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    } while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

    throw ("Timed out waiting for service '{0}' to reach status '{1}'." -f $Name, $DesiredStatus)
}

function Update-ServiceSettings {
    param(
        [string]$SettingsPath,
        [string]$ServerUrlValue,
        [string]$DeviceNameValue,
        [string]$RegistrationSharedKeyValue,
        [bool]$ConsentRequiredValue,
        [bool]$AutoApproveValue,
        [string]$RustDeskIdValue,
        [string[]]$TailscaleIpAddresses
    )

    $settings = Get-Content -Path $SettingsPath -Raw | ConvertFrom-Json
    $settings.StevensSupportHelper.ServerBaseUrl = $ServerUrlValue
    $settings.StevensSupportHelper.DeviceName = $DeviceNameValue
    $settings.StevensSupportHelper.RegistrationSharedKey = $RegistrationSharedKeyValue
    $settings.StevensSupportHelper.ConsentRequired = $ConsentRequiredValue
    $settings.StevensSupportHelper.AutoApproveSupportRequests = $AutoApproveValue
    $settings.StevensSupportHelper.AutoDetectTailscaleIps = $true
    $settings.StevensSupportHelper.AutoDetectRustDeskId = $true
    $settings.StevensSupportHelper.TailscaleConnected = ($TailscaleIpAddresses.Count -gt 0)
    $settings.StevensSupportHelper.TailscaleIpAddresses = @($TailscaleIpAddresses)
    $settings.StevensSupportHelper.RustDeskId = $RustDeskIdValue
    $settings | ConvertTo-Json -Depth 10 | Set-Content -Path $SettingsPath
}

function Write-DynamicClientSettings {
    param(
        [bool]$ConsentRequiredValue,
        [bool]$AutoApproveValue,
        [string]$RustDeskIdValue,
        [string[]]$TailscaleIpAddresses
    )

    $programDataRoot = Join-Path $env:ProgramData 'StevensSupportHelper'
    New-Item -ItemType Directory -Path $programDataRoot -Force | Out-Null
    @{
        StevensSupportHelper = @{
            ConsentRequired = $ConsentRequiredValue
            AutoApproveSupportRequests = $AutoApproveValue
            TailscaleConnected = ($TailscaleIpAddresses.Count -gt 0)
            TailscaleIpAddresses = @($TailscaleIpAddresses)
            RustDeskId = $RustDeskIdValue
        }
    } | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $programDataRoot 'dynamic-client-settings.json')
}

function Write-TraySettings {
    param([string]$ServerUrlValue)

    $programDataRoot = Join-Path $env:ProgramData 'StevensSupportHelper'
    New-Item -ItemType Directory -Path $programDataRoot -Force | Out-Null
    @{ ServerBaseUrl = $ServerUrlValue } | ConvertTo-Json | Set-Content -Path (Join-Path $programDataRoot 'tray-settings.json')
}

function Get-TrayStartupPaths {
    $startupDirectory = [Environment]::GetFolderPath('CommonStartup')
    return @{
        Directory = $startupDirectory
        ShortcutPath = (Join-Path $startupDirectory 'StevensSupportHelper Client Tray.lnk')
        LegacyCommandPath = (Join-Path $startupDirectory 'StevensSupportHelper Client Tray.cmd')
    }
}

function Install-Service {
    param(
        [string]$Name,
        [string]$ExecutablePath
    )

    Assert-FileExists -Path $ExecutablePath

    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        sc.exe stop $Name | Out-Host
        Wait-ForServiceStatus -Name $Name -DesiredStatus 'Stopped'
        sc.exe delete $Name | Out-Host
        Wait-ForServiceStatus -Name $Name -DesiredStatus 'Deleted'
    }

    $binPath = '"' + $ExecutablePath + '"'
    sc.exe create $Name binPath= $binPath start= auto obj= LocalSystem DisplayName= 'StevensSupportHelper Client Service' | Out-Host
    sc.exe description $Name 'StevensSupportHelper background agent for registration, heartbeats and managed support orchestration.' | Out-Host
    sc.exe config $Name start= delayed-auto obj= LocalSystem | Out-Host
    sc.exe failure $Name reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Host
    sc.exe failureflag $Name 1 | Out-Host
    sc.exe start $Name | Out-Host
    Wait-ForServiceStatus -Name $Name -DesiredStatus 'Running'
}

function Install-TrayStartup {
    param([string]$TrayExecutablePath)

    Assert-FileExists -Path $TrayExecutablePath

    $startupPaths = Get-TrayStartupPaths
    New-Item -ItemType Directory -Path $startupPaths.Directory -Force | Out-Null

    if (Test-Path -LiteralPath $startupPaths.LegacyCommandPath) {
        Remove-Item -LiteralPath $startupPaths.LegacyCommandPath -Force
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupPaths.ShortcutPath)
    $shortcut.TargetPath = $TrayExecutablePath
    $shortcut.WorkingDirectory = Split-Path -Parent $TrayExecutablePath
    $shortcut.WindowStyle = 1
    $shortcut.Description = 'Launch StevensSupportHelper Client Tray at user sign-in.'
    $shortcut.Save()

    $runKeyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKeyPath -Force | Out-Null
    New-ItemProperty -Path $runKeyPath -Name 'StevensSupportHelperClientTray' -Value ('"{0}"' -f $TrayExecutablePath) -PropertyType String -Force | Out-Null
}

function Resolve-TailscaleExecutablePath {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'),
        (Join-Path $env:LOCALAPPDATA 'Tailscale\tailscale.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

function Get-TailscaleIpAddresses {
    $addresses = [System.Collections.Generic.List[string]]::new()

    $tailscaleAdapters = Get-NetIPAddress -ErrorAction SilentlyContinue |
        Where-Object {
            $_.InterfaceAlias -like '*Tailscale*' -or $_.InterfaceAlias -like '*tailscale*'
        } |
        Where-Object {
            $_.IPAddress -and
            $_.IPAddress -notlike 'fe80:*' -and
            $_.IPAddress -ne '127.0.0.1'
        } |
        Select-Object -ExpandProperty IPAddress -Unique

    foreach ($address in $tailscaleAdapters) {
        if (-not [string]::IsNullOrWhiteSpace($address)) {
            $addresses.Add($address.Trim())
        }
    }

    if ($addresses.Count -eq 0) {
        $tailscaleExe = Resolve-TailscaleExecutablePath
        if ($tailscaleExe) {
            foreach ($arguments in @('ip -4', 'ip -6')) {
                try {
                    $output = & $tailscaleExe $arguments.Split(' ') 2>$null
                    foreach ($line in $output) {
                        if (-not [string]::IsNullOrWhiteSpace($line)) {
                            $addresses.Add($line.Trim())
                        }
                    }
                }
                catch {
                }
            }
        }
    }

    return @($addresses | Sort-Object -Unique)
}

function Resolve-RustDeskExecutablePath {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'RustDesk\rustdesk.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\RustDesk\rustdesk.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

function Get-RustDeskId {
    param([string]$ConfiguredRustDeskId)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredRustDeskId)) {
        return $ConfiguredRustDeskId.Trim()
    }

    $candidatePaths = @(
        (Join-Path $env:ProgramData 'RustDesk\config\RustDesk2.toml'),
        (Join-Path $env:APPDATA 'RustDesk\config\RustDesk2.toml'),
        (Join-Path $env:LOCALAPPDATA 'RustDesk\config\RustDesk2.toml')
    ) | Select-Object -Unique

    foreach ($candidatePath in $candidatePaths) {
        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            continue
        }

        $content = Get-Content -LiteralPath $candidatePath -Raw
        $match = [regex]::Match($content, "(?m)^\s*id\s*=\s*['""]?(?<id>[0-9\-]+)['""]?\s*$")
        if ($match.Success) {
            return $match.Groups['id'].Value.Trim()
        }
    }

    return ''
}

function Install-RustDeskIfRequested {
    param([string]$InstallerPath)

    if (-not $InstallRustDesk) {
        return
    }

    if (Resolve-RustDeskExecutablePath) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
        Assert-FileExists -Path $InstallerPath
        & $InstallerPath /S | Out-Host
        Start-Sleep -Seconds 5
        return
    }

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($winget) {
        & $winget.Source install --id RustDesk.RustDesk -e --silent --accept-package-agreements --accept-source-agreements | Out-Host
        Start-Sleep -Seconds 5
        return
    }

    throw 'RustDesk installation requested, but no installer path was provided and winget.exe is not available.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$serviceOutput = Join-Path $InstallRoot 'client-service'
$trayOutput = Join-Path $InstallRoot 'client-tray'

New-Item -ItemType Directory -Path $serviceOutput -Force | Out-Null
New-Item -ItemType Directory -Path $trayOutput -Force | Out-Null

Publish-Project -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Client.Service\StevensSupportHelper.Client.Service.csproj') -OutputPath $serviceOutput
Publish-Project -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Client.Tray\StevensSupportHelper.Client.Tray.csproj') -OutputPath $trayOutput

Assert-Administrator

Install-RustDeskIfRequested -InstallerPath $RustDeskInstallerPath

$tailscaleIpAddresses = Get-TailscaleIpAddresses
$resolvedRustDeskId = Get-RustDeskId -ConfiguredRustDeskId $RustDeskId

Update-ServiceSettings `
    -SettingsPath (Join-Path $serviceOutput 'appsettings.json') `
    -ServerUrlValue $ServerUrl `
    -DeviceNameValue $DeviceName `
    -RegistrationSharedKeyValue $RegistrationSharedKey `
    -ConsentRequiredValue $ConsentRequired `
    -AutoApproveValue $AutoApproveSupportRequests `
    -RustDeskIdValue $resolvedRustDeskId `
    -TailscaleIpAddresses $tailscaleIpAddresses

Write-DynamicClientSettings `
    -ConsentRequiredValue $ConsentRequired `
    -AutoApproveValue $AutoApproveSupportRequests `
    -RustDeskIdValue $resolvedRustDeskId `
    -TailscaleIpAddresses $tailscaleIpAddresses

Write-TraySettings -ServerUrlValue $ServerUrl

if ($PublishOnly) {
    Write-Host ''
    Write-Host 'Client projects published only; service registration and tray autostart were skipped.' -ForegroundColor Yellow
    Write-Host ("Output root: {0}" -f $InstallRoot)
    Write-Host ("Server URL: {0}" -f $ServerUrl)
    Write-Host ("RustDesk ID: {0}" -f ($(if ($resolvedRustDeskId) { $resolvedRustDeskId } else { '<none>' })))
    Write-Host ("Tailscale IPs: {0}" -f ($(if ($tailscaleIpAddresses.Count -gt 0) { ($tailscaleIpAddresses -join ', ') } else { '<none>' })))
    exit 0
}

Install-Service -Name $ServiceName -ExecutablePath (Join-Path $serviceOutput 'StevensSupportHelper.Client.Service.exe')
Install-TrayStartup -TrayExecutablePath (Join-Path $trayOutput 'StevensSupportHelper.Client.Tray.exe')

Write-Host ''
Write-Host 'Client components installed.' -ForegroundColor Green
Write-Host ("Service: {0}" -f $ServiceName)
Write-Host ("Service account: LocalSystem")
Write-Host ("Install root: {0}" -f $InstallRoot)
Write-Host ("Server URL: {0}" -f $ServerUrl)
Write-Host ("RustDesk ID: {0}" -f ($(if ($resolvedRustDeskId) { $resolvedRustDeskId } else { '<none>' })))
Write-Host ("Tailscale IPs: {0}" -f ($(if ($tailscaleIpAddresses.Count -gt 0) { ($tailscaleIpAddresses -join ', ') } else { '<none>' })))
