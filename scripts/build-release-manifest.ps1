param(
    [string]$Version,
    [string]$Channel = "stable",
    [string]$ServerUrl = "http://localhost:5000",
    [string]$OutputRoot = ".\publish\release",
    [string]$ManifestUrlBase = "https://example.invalid/stevenssupporthelper",
    [string]$Notes = "Automated client release bundle"
)

$ErrorActionPreference = "Stop"

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$OutputPath
    )

    dotnet publish $ProjectPath -c Release -o $OutputPath | Out-Host
}

function Get-Sha256 {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$bundleRoot = Join-Path $resolvedOutputRoot "bundle"
$serviceOutput = Join-Path $bundleRoot "client-service"
$trayOutput = Join-Path $bundleRoot "client-tray"
$bundlePath = Join-Path $resolvedOutputRoot ("StevensSupportHelper-client-{0}.zip" -f $Version)
$manifestPath = Join-Path $resolvedOutputRoot "release-manifest.json"

New-Item -ItemType Directory -Path $serviceOutput -Force | Out-Null
New-Item -ItemType Directory -Path $trayOutput -Force | Out-Null

Publish-Project -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Client.Service\StevensSupportHelper.Client.Service.csproj') -OutputPath $serviceOutput
Publish-Project -ProjectPath (Join-Path $repoRoot 'src\StevensSupportHelper.Client.Tray\StevensSupportHelper.Client.Tray.csproj') -OutputPath $trayOutput

$serviceSettingsPath = Join-Path $serviceOutput 'appsettings.json'
if (Test-Path -LiteralPath $serviceSettingsPath) {
    $settings = Get-Content -LiteralPath $serviceSettingsPath -Raw | ConvertFrom-Json
    $settings.StevensSupportHelper.ServerBaseUrl = $ServerUrl
    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $serviceSettingsPath
}

if (Test-Path -LiteralPath $bundlePath) {
    Remove-Item -LiteralPath $bundlePath -Force
}

Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $bundlePath -CompressionLevel Optimal

$bundleName = Split-Path -Leaf $bundlePath
$bundleUrl = ($ManifestUrlBase.TrimEnd('/') + '/' + $bundleName)
$manifest = [ordered]@{
    product = "StevensSupportHelper"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    releases = @(
        [ordered]@{
            channel = $Channel
            version = $Version
            publishedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            notes = $Notes
            bundle = [ordered]@{
                url = $bundleUrl
                sha256 = (Get-Sha256 -Path $bundlePath)
                sizeBytes = (Get-Item -LiteralPath $bundlePath).Length
            }
        }
    )
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath

Write-Host ""
Write-Host "Release bundle created." -ForegroundColor Green
Write-Host ("Bundle: {0}" -f $bundlePath)
Write-Host ("Manifest: {0}" -f $manifestPath)
