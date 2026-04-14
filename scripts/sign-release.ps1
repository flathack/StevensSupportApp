param(
    [string]$Path = ".\publish",
    [string]$ConfigPath = ".\scripts\code-signing.json",
    [string]$TimestampUrl,
    [string]$CertificateThumbprint,
    [string]$PfxPath,
    [string]$PfxPassword,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param([string]$Candidate)

    $resolved = Resolve-Path -LiteralPath $Candidate -ErrorAction Stop
    return $resolved.Path
}

function Load-OptionalConfig {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Find-SignTool {
    $configured = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($configured) {
        return $configured.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitsRoot -PathType Container)) {
        return $null
    }

    $candidates = Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending

    return $candidates | Select-Object -First 1 -ExpandProperty FullName
}

function Get-FilesToSign {
    param(
        [string]$RootPath,
        [string[]]$Patterns
    )

    $files = foreach ($pattern in $Patterns) {
        Get-ChildItem -LiteralPath $RootPath -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
    }

    return $files | Sort-Object FullName -Unique
}

function Build-SignArguments {
    param(
        [string]$FilePath,
        [string]$TimestampServer,
        [string]$Thumbprint,
        [string]$CertificateFile,
        [string]$CertificatePassword
    )

    $arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256')

    if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
        $arguments += @('/tr', $TimestampServer)
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateFile)) {
        $arguments += @('/f', $CertificateFile)
        if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
            $arguments += @('/p', $CertificatePassword)
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $arguments += @('/sha1', $Thumbprint)
    }
    else {
        throw 'Either CertificateThumbprint or PfxPath must be provided.'
    }

    $arguments += $FilePath
    return $arguments
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$config = Load-OptionalConfig -Path (Join-Path $repoRoot $ConfigPath)
$resolvedPath = Resolve-FullPath -Candidate (Join-Path $repoRoot $Path)

$effectiveTimestampUrl = if ($PSBoundParameters.ContainsKey('TimestampUrl')) { $TimestampUrl } else { [string]$config.TimestampUrl }
$effectiveThumbprint = if ($PSBoundParameters.ContainsKey('CertificateThumbprint')) { $CertificateThumbprint } else { [string]$config.CertificateThumbprint }
$effectivePfxPath = if ($PSBoundParameters.ContainsKey('PfxPath')) { $PfxPath } else { [string]$config.PfxPath }
$effectivePfxPassword = if ($PSBoundParameters.ContainsKey('PfxPassword')) { $PfxPassword } else { [string]$config.PfxPassword }
$filePatterns = @($config.FilePatterns)
if ($filePatterns.Count -eq 0) {
    $filePatterns = @('*.exe', '*.dll')
}

if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
    throw ("Target path does not exist or is not a directory: {0}" -f $resolvedPath)
}

if (-not [string]::IsNullOrWhiteSpace($effectivePfxPath)) {
    $effectivePfxPath = Resolve-FullPath -Candidate (Join-Path $repoRoot $effectivePfxPath)
}

$filesToSign = @(Get-FilesToSign -RootPath $resolvedPath -Patterns $filePatterns)
if ($filesToSign.Count -eq 0) {
    throw ("No files matching patterns {0} were found under {1}" -f ($filePatterns -join ', '), $resolvedPath)
}

$signToolPath = Find-SignTool
if (-not $DryRun -and [string]::IsNullOrWhiteSpace($signToolPath)) {
    throw 'signtool.exe was not found. Install the Windows SDK or add signtool.exe to PATH.'
}

Write-Host ''
Write-Host 'StevensSupportHelper Release Signing' -ForegroundColor Cyan
Write-Host ("Target path: {0}" -f $resolvedPath)
Write-Host ("Files found: {0}" -f $filesToSign.Count)
Write-Host ("Mode: {0}" -f ($(if ($DryRun) { 'DryRun' } else { 'Sign' })))
Write-Host ''

foreach ($file in $filesToSign) {
    $arguments = Build-SignArguments `
        -FilePath $file.FullName `
        -TimestampServer $effectiveTimestampUrl `
        -Thumbprint $effectiveThumbprint `
        -CertificateFile $effectivePfxPath `
        -CertificatePassword $effectivePfxPassword

    if ($DryRun) {
        Write-Host ("DRYRUN  {0} {1}" -f 'signtool.exe', ($arguments -join ' '))
        continue
    }

    & $signToolPath @arguments | Out-Host
}

Write-Host ''
Write-Host 'Release signing completed.' -ForegroundColor Green
