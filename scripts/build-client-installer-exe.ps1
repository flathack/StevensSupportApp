param(
    [string]$ServerUrl = "http://localhost:5000",
    [string]$Version = "1.0.0",
    [string]$OutputRoot = ".\\publish\\client-installer"
)

$ErrorActionPreference = "Stop"

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$OutputPath,
        [string]$ProjectVersion
    )

    & dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /nodeReuse:false /p:UseSharedCompilation=false /p:Version=$ProjectVersion -o $OutputPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$payloadRoot = Join-Path $resolvedOutputRoot "payload"
$serviceOutput = Join-Path $payloadRoot "client-service"
$trayOutput = Join-Path $payloadRoot "client-tray"
$installerProjectRoot = Join-Path $repoRoot "src\\StevensSupportHelper.Installer"
$embeddedPayloadDir = Join-Path $installerProjectRoot "Payload"
$embeddedPayloadPath = Join-Path $embeddedPayloadDir "client-payload.zip"
$installerOutput = Join-Path $resolvedOutputRoot "app"
$installerZipPath = Join-Path $resolvedOutputRoot ("ClientSetup_{0}_win-x64.zip" -f $Version)

New-Item -ItemType Directory -Path $serviceOutput -Force | Out-Null
New-Item -ItemType Directory -Path $trayOutput -Force | Out-Null
New-Item -ItemType Directory -Path $embeddedPayloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

Publish-Project -ProjectPath (Join-Path $repoRoot "src\\StevensSupportHelper.Client.Service\\StevensSupportHelper.Client.Service.csproj") -OutputPath $serviceOutput -ProjectVersion $Version
Publish-Project -ProjectPath (Join-Path $repoRoot "src\\StevensSupportHelper.Client.Tray\\StevensSupportHelper.Client.Tray.csproj") -OutputPath $trayOutput -ProjectVersion $Version

$serviceSettingsPath = Join-Path $serviceOutput "appsettings.json"
if (Test-Path -LiteralPath $serviceSettingsPath) {
    $settings = Get-Content -LiteralPath $serviceSettingsPath -Raw | ConvertFrom-Json
    $settings.StevensSupportHelper.ServerBaseUrl = $ServerUrl
    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $serviceSettingsPath
}

if (Test-Path -LiteralPath $embeddedPayloadPath) {
    Remove-Item -LiteralPath $embeddedPayloadPath -Force
}

Compress-Archive -Path (Join-Path $payloadRoot "*") -DestinationPath $embeddedPayloadPath -CompressionLevel Optimal

& dotnet publish (Join-Path $installerProjectRoot "StevensSupportHelper.Installer.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /nodeReuse:false `
    /p:UseSharedCompilation=false `
    /p:Version=$Version `
    -o $installerOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for installer project"
}

Copy-Item -LiteralPath (Join-Path $repoRoot "client.installer.config.sample") -Destination (Join-Path $installerOutput "client.installer.config.sample") -Force
if (Test-Path -LiteralPath (Join-Path $repoRoot "docs\\client-installer-oneclick.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\\client-installer-oneclick.md") -Destination (Join-Path $installerOutput "client-installer-oneclick.md") -Force
}

if (Test-Path -LiteralPath $installerZipPath) {
    Remove-Item -LiteralPath $installerZipPath -Force
}

Compress-Archive -Path (Join-Path $installerOutput "*") -DestinationPath $installerZipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Client EXE installer created." -ForegroundColor Green
Write-Host ("Installer EXE: {0}" -f (Join-Path $installerOutput "StevensSupportHelper.Installer.exe"))
Write-Host ("Installer ZIP: {0}" -f $installerZipPath)
