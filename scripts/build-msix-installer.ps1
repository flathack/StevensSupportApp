param(
    [string]$Version = "1.0.0.0",
    [string]$Publisher = "CN=StevensSupportHelper",
    [string]$PackageName = "StevensSupportHelper.Client",
    [string]$ServerUrl = "http://localhost:5000",
    [string]$OutputRoot = ".\\publish\\msix",
    [switch]$LayoutOnly
)

$ErrorActionPreference = "Stop"

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$OutputPath
    )

    dotnet publish $ProjectPath -c Release -o $OutputPath | Out-Host
}

function Update-ServiceSettings {
    param(
        [string]$SettingsPath,
        [string]$ServerUrlValue
    )

    $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $settings.StevensSupportHelper.ServerBaseUrl = $ServerUrlValue
    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SettingsPath
}

function Write-PlaceholderPng {
    param([string]$Path)

    $pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5uQAAAAASUVORK5CYII="
    [IO.File]::WriteAllBytes($Path, [Convert]::FromBase64String($pngBase64))
}

function Find-MakeAppx {
    $candidates = @(
        (Get-Command makeappx.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\makeappx.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $discovered = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -ExpandProperty FullName

    $allCandidates = @()
    $allCandidates += @($candidates)
    $allCandidates += @($discovered)
    return $allCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$layoutRoot = Join-Path $resolvedOutputRoot "layout"
$packageRoot = Join-Path $layoutRoot "package"
$assetsRoot = Join-Path $packageRoot "Assets"
$programFilesRoot = Join-Path $packageRoot "VFS\\ProgramFilesX64\\StevensSupportHelper"
$serviceOutput = Join-Path $programFilesRoot "client-service"
$trayOutput = Join-Path $programFilesRoot "client-tray"
$bootstrapRoot = Join-Path $packageRoot "Bootstrap"
$manifestTemplatePath = Join-Path $repoRoot "installer\\msix\\Package.appxmanifest.template"
$manifestOutputPath = Join-Path $packageRoot "AppxManifest.xml"
$msixPath = Join-Path $resolvedOutputRoot ("StevensSupportHelper.Client_{0}_x64.msix" -f $Version)
$zipPath = Join-Path $resolvedOutputRoot ("StevensSupportHelper.Client_{0}_layout.zip" -f $Version)

New-Item -ItemType Directory -Path $serviceOutput -Force | Out-Null
New-Item -ItemType Directory -Path $trayOutput -Force | Out-Null
New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $bootstrapRoot -Force | Out-Null

Publish-Project -ProjectPath (Join-Path $repoRoot "src\\StevensSupportHelper.Client.Service\\StevensSupportHelper.Client.Service.csproj") -OutputPath $serviceOutput
Publish-Project -ProjectPath (Join-Path $repoRoot "src\\StevensSupportHelper.Client.Tray\\StevensSupportHelper.Client.Tray.csproj") -OutputPath $trayOutput

Update-ServiceSettings -SettingsPath (Join-Path $serviceOutput "appsettings.json") -ServerUrlValue $ServerUrl
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\\install-client.ps1") -Destination (Join-Path $bootstrapRoot "install-client.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\\uninstall-client.ps1") -Destination (Join-Path $bootstrapRoot "uninstall-client.ps1") -Force

Write-PlaceholderPng -Path (Join-Path $assetsRoot "StoreLogo.png")
Write-PlaceholderPng -Path (Join-Path $assetsRoot "Square150x150Logo.png")
Write-PlaceholderPng -Path (Join-Path $assetsRoot "Square44x44Logo.png")

$manifest = Get-Content -LiteralPath $manifestTemplatePath -Raw
$manifest = $manifest.Replace("__PACKAGE_NAME__", $PackageName)
$manifest = $manifest.Replace("__PUBLISHER__", $Publisher)
$manifest = $manifest.Replace("__VERSION__", $Version)
Set-Content -LiteralPath $manifestOutputPath -Value $manifest

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

$makeAppxPath = if ($LayoutOnly) { $null } else { Find-MakeAppx }
if ($makeAppxPath) {
    if (Test-Path -LiteralPath $msixPath) {
        Remove-Item -LiteralPath $msixPath -Force
    }

    & $makeAppxPath pack /d $packageRoot /p $msixPath /o | Out-Host
    Write-Host ""
    Write-Host "MSIX package created." -ForegroundColor Green
    Write-Host ("MSIX: {0}" -f $msixPath)
} else {
    Write-Host ""
    Write-Host "MSIX SDK tool not found; generated package layout and ZIP fallback instead." -ForegroundColor Yellow
}

Write-Host ("Layout: {0}" -f $packageRoot)
Write-Host ("ZIP fallback: {0}" -f $zipPath)
