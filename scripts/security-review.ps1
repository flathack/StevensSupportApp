param(
    [switch]$FailOnWarnings
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverSettingsPath = Join-Path $repoRoot 'src\StevensSupportHelper.Server\appsettings.json'
$serviceSettingsPath = Join-Path $repoRoot 'src\StevensSupportHelper.Client.Service\appsettings.json'
$launchSettingsPath = Join-Path $repoRoot 'src\StevensSupportHelper.Server\Properties\launchSettings.json'

function Add-Finding {
    param(
        [string]$Severity,
        [string]$Message
    )

    $script:findings.Add([pscustomobject]@{
        Severity = $Severity
        Message = $Message
    }) | Out-Null
}

function Assert-File {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ("Required file not found: {0}" -f $Path)
    }
}

function Load-Json {
    param([string]$Path)

    Assert-File -Path $Path
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

$findings = New-Object System.Collections.Generic.List[object]

$serverSettings = Load-Json -Path $serverSettingsPath
$serviceSettings = Load-Json -Path $serviceSettingsPath
$launchSettings = Load-Json -Path $launchSettingsPath

$adminAccounts = @($serverSettings.StevensSupportHelperAdminAuth.Accounts)
foreach ($account in $adminAccounts) {
    if ($account.ApiKey -match '^change-me-') {
        Add-Finding -Severity 'FAIL' -Message ("Admin account '{0}' still uses placeholder API key." -f $account.DisplayName)
    }
}

$registrationKey = [string]$serverSettings.StevensSupportHelperClientRegistration.SharedKey
$serviceRegistrationKey = [string]$serviceSettings.StevensSupportHelper.RegistrationSharedKey
if ([string]::IsNullOrWhiteSpace($registrationKey) -or $registrationKey -match '^change-me-') {
    Add-Finding -Severity 'FAIL' -Message 'Server registration shared key still uses placeholder value.'
}

if ([string]::IsNullOrWhiteSpace($serviceRegistrationKey) -or $serviceRegistrationKey -match '^change-me-') {
    Add-Finding -Severity 'FAIL' -Message 'Client service registration shared key still uses placeholder value.'
}

if ($registrationKey -ne $serviceRegistrationKey) {
    Add-Finding -Severity 'FAIL' -Message 'Server and client service registration shared keys do not match.'
}

if (-not [bool]$serverSettings.StevensSupportHelperClientRegistration.RequireSignedRegistration) {
    Add-Finding -Severity 'FAIL' -Message 'Signed client registration is disabled.'
}

if (-not [bool]$serverSettings.StevensSupportHelperRateLimiting.Enabled) {
    Add-Finding -Severity 'FAIL' -Message 'Rate limiting is disabled.'
}

$serverUrl = [string]$serviceSettings.StevensSupportHelper.ServerBaseUrl
if ($serverUrl -like 'http://*') {
    Add-Finding -Severity 'WARN' -Message ("Client service still targets non-TLS server URL '{0}'." -f $serverUrl)
}

$httpsProfile = $launchSettings.profiles.https
if ($null -eq $httpsProfile) {
    Add-Finding -Severity 'FAIL' -Message 'HTTPS launch profile is missing.'
}
elseif ([string]$httpsProfile.applicationUrl -notmatch '^https://') {
    Add-Finding -Severity 'WARN' -Message 'HTTPS launch profile does not begin with an HTTPS binding.'
}

if ([int]$serverSettings.StevensSupportHelperServer.SessionTimeoutMinutes -lt 15) {
    Add-Finding -Severity 'WARN' -Message 'Session timeout is configured below 15 minutes.'
}

if ([int]$serverSettings.StevensSupportHelperServer.ConsentTimeoutMinutes -lt 1) {
    Add-Finding -Severity 'FAIL' -Message 'Consent timeout is configured below 1 minute.'
}

$logDirectory = Join-Path $env:ProgramData 'StevensSupportHelper\Logs'
if (-not (Test-Path -LiteralPath $logDirectory)) {
    Add-Finding -Severity 'WARN' -Message ("Crash log directory does not exist yet: {0}" -f $logDirectory)
}

Write-Host ''
Write-Host 'StevensSupportHelper Security Review Check' -ForegroundColor Cyan
Write-Host ('Repository: {0}' -f $repoRoot)
Write-Host ''

if ($findings.Count -eq 0) {
    Write-Host 'PASS  No findings.' -ForegroundColor Green
    exit 0
}

foreach ($finding in $findings) {
    $color = switch ($finding.Severity) {
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        default { 'White' }
    }

    Write-Host ('{0}  {1}' -f $finding.Severity, $finding.Message) -ForegroundColor $color
}

$failCount = @($findings | Where-Object Severity -eq 'FAIL').Count
$warnCount = @($findings | Where-Object Severity -eq 'WARN').Count

Write-Host ''
Write-Host ('Summary: {0} fail, {1} warning' -f $failCount, $warnCount)

if ($failCount -gt 0 -or ($FailOnWarnings -and $warnCount -gt 0)) {
    exit 1
}

exit 0
