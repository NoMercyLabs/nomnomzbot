# Local dev API lifecycle: start it in the background, wait for /health, or stop whatever is
# listening on its port. Replaces the by-hand "dotnet run -> Get-NetTCPConnection -> Stop-Process"
# sequence (drifts when a step gets skipped by hand) used for things like re-fetching openapi/v1.json.
#
# Usage:
#   scripts/dev-api.ps1 start [-Port 5080] [-TimeoutSeconds 30]   # dotnet run --no-build, waits for /health
#   scripts/dev-api.ps1 stop  [-Port 5080]                        # stops whatever owns that port
#   scripts/dev-api.ps1 status [-Port 5080]

param(
    [Parameter(Mandatory = $true)][ValidateSet('start', 'stop', 'status')][string]$Action,
    [int]$Port = 5080,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot '..' | Resolve-Path
$apiProject = Join-Path $repo 'server/src/NomNomzBot.Api'

# Who owns the port? Get-NetTCPConnection is WINDOWS-ONLY, and this script now also has to run
# under pwsh inside the devbox container (devbox/), so the Linux/macOS path uses `ss` (falling
# back to `lsof`) and parses the pid out. Same contract on every host: a pid, or $null.
function Get-ApiProcessId {
    param([int]$Port)

    if ($IsWindows) {
        $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($conn) { return [int]$conn.OwningProcess }
        return $null
    }

    # `ss -lptnH` prints e.g. `users:(("dotnet",pid=1234,fd=200))` on the listening socket's row.
    [string]$line = $null
    if (Get-Command ss -ErrorAction SilentlyContinue) {
        $line = (ss -lptnH "sport = :$Port" 2>$null | Select-Object -First 1)
        if ($line -match 'pid=(\d+)') { return [int]$Matches[1] }
    }
    if (Get-Command lsof -ErrorAction SilentlyContinue) {
        [string]$found = (lsof -nP -iTCP:$Port -sTCP:LISTEN -t 2>$null | Select-Object -First 1)
        if ($found) { return [int]$found }
    }
    return $null
}

switch ($Action) {
    'status' {
        $procId = Get-ApiProcessId -Port $Port
        if ($procId) { Write-Output "listening on $Port (pid $procId)" }
        else { Write-Output "not listening on $Port" }
    }
    'stop' {
        $procId = Get-ApiProcessId -Port $Port
        if (-not $procId) { Write-Output "nothing listening on $Port"; return }
        Stop-Process -Id $procId -Force
        Start-Sleep -Seconds 1
        if (Get-ApiProcessId -Port $Port) { throw "port $Port still held after stopping pid $procId" }
        Write-Output "stopped pid $procId"
    }
    'start' {
        if (Get-ApiProcessId -Port $Port) { throw "port $Port already in use - stop it first" }
        Push-Location $apiProject
        try {
            dotnet build --nologo -v q
            if ($LASTEXITCODE -ne 0) { throw "build failed" }
            # -WindowStyle is a Windows-only parameter; passing it under Linux pwsh (the devbox)
            # fails the call outright, so it is splatted in only where it means something.
            [hashtable]$startArgs = @{
                FilePath               = 'dotnet'
                ArgumentList           = "run --no-build --urls http://localhost:$Port"
                WorkingDirectory       = $apiProject
                RedirectStandardOutput = (Join-Path $repo 'server/api-dev.log')
                RedirectStandardError  = (Join-Path $repo 'server/api-dev.err.log')
            }
            if ($IsWindows) { $startArgs['WindowStyle'] = 'Hidden' }
            Start-Process @startArgs
        }
        finally { Pop-Location }

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            try {
                $resp = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 2
                if ($resp.StatusCode -eq 200) { Write-Output "up after healthy /health response (pid $(Get-ApiProcessId -Port $Port))"; return }
            }
            catch { Start-Sleep -Milliseconds 500 }
        }
        throw "API did not become healthy on port $Port within ${TimeoutSeconds}s - see server/api-dev.log"
    }
}
