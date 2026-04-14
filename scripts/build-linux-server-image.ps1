param(
    [string]$ImageName = "flathack/stevens-support-helper-server:latest"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot ".runtime\docker-server-publish"

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

Write-Host "Publishing server locally..." -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot "src\StevensSupportHelper.Server\StevensSupportHelper.Server.csproj") `
    -c Release `
    -o $publishRoot | Out-Host

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for server project."
}

Write-Host "Building linux/amd64 Docker image..." -ForegroundColor Cyan
docker build --platform linux/amd64 -t $ImageName $repoRoot

if ($LASTEXITCODE -ne 0) {
    throw "docker build failed."
}

Write-Host "Docker image created: $ImageName" -ForegroundColor Green
