$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$processFile = Join-Path (Join-Path $repoRoot '.runtime') 'dev-processes.json'

if (-not (Test-Path $processFile)) {
    Write-Host 'No tracked dev processes found.'
    exit 0
}

$processes = Get-Content -Path $processFile -Raw | ConvertFrom-Json
foreach ($processEntry in @($processes)) {
    $process = Get-Process -Id $processEntry.ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        Stop-Process -Id $processEntry.ProcessId -Force
        Write-Host ("Stopped {0} (PID {1})." -f $processEntry.Name, $processEntry.ProcessId)
    }
    else {
        Write-Host ("Skipped {0}; PID {1} is no longer running." -f $processEntry.Name, $processEntry.ProcessId)
    }
}

Remove-Item -Path $processFile -Force
Write-Host 'Tracked dev processes cleared.'