param(
    [string]$InstallRoot = "$env:ProgramFiles\RDP Wrapper",
    [string]$PackageUrl = "https://github.com/stascorp/rdpwrap/releases/download/v1.6.2/RDPWrap-v1.6.2.zip",
    [string]$IniUrl = "https://raw.githubusercontent.com/sebaxakerhtc/rdpwrap.ini/master/rdpwrap.ini",
    [switch]$ForceReinstall
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-WindowsEdition {
    return (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name ProductName).ProductName
}

function Test-RdpWrapperInstalled {
    param([string]$Root)

    $requiredFiles = @(
        (Join-Path $Root 'RDPConf.exe'),
        (Join-Path $Root 'RDPWInst.exe'),
        (Join-Path $Root 'rdpwrap.dll')
    )

    return ($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0
}

function Invoke-Download {
    param(
        [string]$Url,
        [string]$TargetPath
    )

    Write-Step "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $TargetPath -UseBasicParsing
}

function Wait-ForServiceState {
    param(
        [string]$ServiceName,
        [string]$DesiredState,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status.ToString().Equals($DesiredState, [StringComparison]::OrdinalIgnoreCase)) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Service '$ServiceName' did not reach state '$DesiredState' within $TimeoutSeconds seconds."
}

function Restart-TermServiceSafe {
    Write-Step "Restarting TermService"
    try {
        Stop-Service -Name TermService -Force -ErrorAction Stop
        Wait-ForServiceState -ServiceName TermService -DesiredState Stopped -TimeoutSeconds 30
    }
    catch {
        Write-Step "TermService stop was skipped or failed: $($_.Exception.Message)"
    }

    Start-Service -Name TermService -ErrorAction Stop
    Wait-ForServiceState -ServiceName TermService -DesiredState Running -TimeoutSeconds 30
}

function Invoke-RdpWrapInstaller {
    param(
        [string]$ExecutablePath,
        [string[]]$Arguments
    )

    Write-Step ("Running {0} {1}" -f $ExecutablePath, ($Arguments -join ' '))
    $process = Start-Process -FilePath $ExecutablePath -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "RDPWInst.exe failed with exit code $($process.ExitCode) for arguments '$($Arguments -join ' ')'."
    }
}

function Set-TermServiceOwnProcess {
    Write-Step "Configuring TermService to run in its own process"
    $process = Start-Process -FilePath "sc.exe" -ArgumentList 'config', 'TermService', 'type=', 'own' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "sc.exe config TermService type= own failed with exit code $($process.ExitCode)."
    }
}

if (-not (Test-IsAdministrator)) {
    throw "install-rdp-wrapper.ps1 must be run as Administrator."
}

$edition = Get-WindowsEdition
Write-Step "Detected Windows edition: $edition"
if ($edition -notmatch 'Home') {
    Write-Step "Windows edition is not Home. RDP Wrapper is not required."
    exit 0
}

$alreadyInstalled = Test-RdpWrapperInstalled -Root $InstallRoot
if ($alreadyInstalled -and -not $ForceReinstall) {
    Write-Step "Existing RDP Wrapper installation detected in '$InstallRoot'."
}
else {
    $tempRoot = Join-Path $env:TEMP ("rdpwrap-" + [Guid]::NewGuid().ToString("N"))
    $zipPath = Join-Path $tempRoot "rdpwrap.zip"
    $extractPath = Join-Path $tempRoot "extract"

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null

    try {
        Invoke-Download -Url $PackageUrl -TargetPath $zipPath
        Write-Step "Extracting RDP Wrapper package"
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath -Force

        $installerExecutable = Join-Path $extractPath 'RDPWInst.exe'
        if (-not (Test-Path -LiteralPath $installerExecutable)) {
            throw "RDPWInst.exe was not found in the downloaded RDP Wrapper package."
        }

        Write-Step "Copying package files to '$InstallRoot'"
        Copy-Item -Path (Join-Path $extractPath '*') -Destination $InstallRoot -Recurse -Force

        $installedExecutable = Join-Path $InstallRoot 'RDPWInst.exe'
        if ($ForceReinstall -and (Test-RdpWrapperInstalled -Root $InstallRoot)) {
            Invoke-RdpWrapInstaller -ExecutablePath $installedExecutable -Arguments @('-u')
        }

        try {
            Invoke-RdpWrapInstaller -ExecutablePath $installedExecutable -Arguments @('-i', '-o')
        }
        catch {
            Write-Step "Initial RDP Wrapper install failed. Retrying with TermService isolation."
            Set-TermServiceOwnProcess
            Invoke-RdpWrapInstaller -ExecutablePath $installedExecutable -Arguments @('-i', '-o')
        }

        Invoke-RdpWrapInstaller -ExecutablePath $installedExecutable -Arguments @('-r')
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if (-not (Test-RdpWrapperInstalled -Root $InstallRoot)) {
    throw "RDP Wrapper files are still missing after the installation step."
}

$iniPath = Join-Path $InstallRoot 'rdpwrap.ini'
if ($IniUrl) {
    Invoke-Download -Url $IniUrl -TargetPath $iniPath
}

Write-Step "Enabling Remote Desktop"
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name 'fDenyTSConnections' -Value 0
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name 'fAllowToGetHelp' -PropertyType DWord -Value 1 -Force | Out-Null
Enable-NetFirewallRule -DisplayGroup 'Remote Desktop' | Out-Null

Restart-TermServiceSafe

$rdpConf = Join-Path $InstallRoot 'RDPConf.exe'
if (Test-Path -LiteralPath $rdpConf) {
    Write-Step "RDP Wrapper is installed. Optional validation GUI: $rdpConf"
}

Write-Step "RDP Wrapper setup completed."
