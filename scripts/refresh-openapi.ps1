# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------

# refresh-openapi.ps1 — regenerate server/openapi/v1.json FROM A RUNNING API.
#
# Why this exists: the snapshot can only come from a served API (Program.cs uses AddOpenApi +
# MapOpenApi at runtime; there is no build-time generator). Hand-editing it is the failure this
# guards against — a slice once added DTO schemas by hand and none of the routes, so every client
# URL would have 404'd while ApiContractTest stayed green. ApiRouteContractTest catches that.
#
# Every step below was learned by paying for it:
#   1. The devbox container publishes host 5080, so a Windows-side `dotnet run` cannot bind it.
#      When the container is up, run the API INSIDE it.
#   2. The container has its OWN database, separate from the Windows one, and it starts empty.
#   3. `docker cp` leaves files root-owned; SQLite then fails with "attempt to write a readonly
#      database" and the migrator aborts at startup.
#   4. Linux has no DPAPI, so token decryption needs Encryption__Key passed explicitly.
#   5. A StartupSecretGuard rejects the bundled dev key unless ASPNETCORE_ENVIRONMENT=Development,
#      and `--no-launch-profile` drops that environment.
#   6. The API needs ~3 MINUTES to reach health 200. Anything shorter reads as a failed start.
#
# Usage:
#   scripts/refresh-openapi.ps1              # refresh the snapshot, leave the API stopped
#   scripts/refresh-openapi.ps1 -KeepRunning # leave it up (for a browser verification run)

param(
    [switch]$KeepRunning,
    [int]$TimeoutSeconds = 300,
    [string]$Container = 'nomnomzbot-devbox'
)

$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot '..' | Resolve-Path
[string]$snapshot = Join-Path $repo 'server/openapi/v1.json'

# Docker is the PREFERRED path (the container publishes host 5080, so a Windows-side `dotnet run`
# cannot bind it while the container is up) but it must not be the ONLY path: with the daemon hung or
# stopped this script used to throw and leave no supported way to regenerate the snapshot -- which is
# how a slice ends up hand-editing it, the exact failure this file exists to prevent.
# `docker ps` itself HANGS when the daemon is wedged, so the probe runs as a job with a timeout rather
# than inline; a hang is treated as "no container", the same as a clean absence.
[bool]$inContainer = $false
$probe = Start-Job { docker ps --filter "name=$using:Container" --format '{{.Names}}' 2>$null }
if (Wait-Job $probe -Timeout 15) {
    [string[]]$names = @(Receive-Job $probe)
    $inContainer = [bool]($names -match [regex]::Escape($Container))
}
else {
    Write-Host '== docker did not answer within 15s; treating it as unavailable =='
    Stop-Job $probe
}
Remove-Job $probe -Force
if (-not $inContainer) {
    Write-Host "== container '$Container' unavailable - running the API on the host instead =="
}

if ($inContainer) {
Write-Host '== preparing the container database =='
docker exec $Container sh -lc 'pkill -f "dotnet run" 2>/dev/null; true' | Out-Null
[string]$winDb = Join-Path $env:LOCALAPPDATA 'NomNomzBot/nomnomz.db'
if (Test-Path $winDb) {
    # The container's own store starts empty, which leaves Channels/Users at zero rows and makes
    # any authenticated check impossible. Copy the real dev database in, then fix ownership —
    # docker cp writes it as root and SQLite cannot migrate a read-only file.
    docker exec $Container sh -lc 'mkdir -p /home/dev/.local/share/NomNomzBot' | Out-Null
    docker cp $winDb "${Container}:/home/dev/.local/share/NomNomzBot/nomnomz.db"
    docker exec -u root $Container sh -lc 'chown -R dev:dev /home/dev/.local/share/NomNomzBot && chmod 664 /home/dev/.local/share/NomNomzBot/nomnomz.db'
}
}

# The dev key lives in appsettings.Development.json, which carries // comments that JSON.parse rejects.
[string]$settings = Join-Path $repo 'server/src/NomNomzBot.Api/appsettings.Development.json'
[string]$raw = (Get-Content -Raw -LiteralPath $settings) -replace '(?m)//.*$', ''
[string]$key = ($raw | ConvertFrom-Json).Encryption.Key
if ([string]::IsNullOrWhiteSpace($key)) { throw 'no Encryption:Key in appsettings.Development.json' }

Write-Host '== starting the API (allow ~3 minutes) =='
[string]$hostLog = Join-Path ([System.IO.Path]::GetTempPath()) 'nnz-openapi-run.log'
$hostApi = $null
if ($inContainer) {
    # DOTNET_gcServer=0: with 24 cores visible, server GC reserves a heap per core and Roslyn dies with
    # OutOfMemoryException compiling Infrastructure — even with ~11 GB free in the container, so it is heap
    # RESERVATION, not real pressure. Workstation GC compiles the same tree clean. Verified 2026-09-07:
    # 83 errors under server GC, 0 errors with this set.
    docker exec -e ASPNETCORE_ENVIRONMENT=Development -e DOTNET_gcServer=0 -e "Encryption__Key=$key" -d $Container `
        sh -lc 'cd /workspace/server/src/NomNomzBot.Api && dotnet run --no-launch-profile --urls http://0.0.0.0:5080 > /tmp/openapi-run.log 2>&1'
}
else {
    # No --urls on the host run: the API binds the port recorded in its data dir on first boot and
    # ignores the switch, so this reuses the machine's own already-locked 5080 rather than inventing one.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Encryption__Key = $key
    $hostApi = Start-Process -PassThru -WindowStyle Hidden -FilePath 'dotnet' `
        -ArgumentList 'run', '--no-launch-profile' `
        -WorkingDirectory (Join-Path $repo 'server/src/NomNomzBot.Api') `
        -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"
}

[int]$waited = 0
[string]$health = '000'
while ($waited -lt $TimeoutSeconds) {
    Start-Sleep -Seconds 6
    $waited += 6
    if ($inContainer) {
        $health = docker exec $Container sh -lc 'curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:5080/health' 2>$null
    }
    else {
        try {
            $health = [string](Invoke-WebRequest -Uri 'http://localhost:5080/health' -TimeoutSec 5 -UseBasicParsing).StatusCode
        }
        catch { $health = '000' }
    }
    if ($health -eq '200') { break }
    [string]$fatal = ''
    if ($inContainer) {
        $fatal = docker exec $Container sh -lc 'grep -iE "FTL|Hosting failed" /tmp/openapi-run.log | head -1' 2>$null
    }
    elseif (Test-Path $hostLog) {
        $fatal = (Select-String -Path $hostLog -Pattern 'FTL|Hosting failed' | Select-Object -First 1).Line
    }
    if ($fatal) { throw "API failed to start: $fatal" }
}
if ($health -ne '200') { throw "API did not reach health 200 within ${TimeoutSeconds}s" }
Write-Host "   health 200 after ${waited}s"

Write-Host '== fetching the document =='
if ($inContainer) {
    docker exec $Container sh -lc 'curl -sf http://localhost:5080/openapi/v1.json -o /tmp/openapi-fetched.json && wc -c < /tmp/openapi-fetched.json' | Out-Host
    docker cp "${Container}:/tmp/openapi-fetched.json" $snapshot
}
else {
    Invoke-WebRequest -Uri 'http://localhost:5080/openapi/v1.json' -OutFile $snapshot -UseBasicParsing
    Write-Host "   fetched $((Get-Item $snapshot).Length) bytes"
}

# A freshly generated document line-diffs enormously against the committed one purely from key
# ordering, so judge it SEMANTICALLY. A path or schema DISAPPEARING is the signal that matters.
# @(...) first: on a PSCustomObject, `.PSObject.Properties.Count` member-enumerates and yields an
# Object[] (one Count per property), which then fails to cast to [int]. Wrapping forces one array.
[int]$paths = @(
    (Get-Content -Raw -LiteralPath $snapshot | ConvertFrom-Json).paths.PSObject.Properties
).Count
Write-Host "   snapshot now carries $paths paths"

if (-not $KeepRunning) {
    if ($inContainer) {
        docker exec $Container sh -lc 'pkill -f "dotnet run" 2>/dev/null; true' | Out-Null
    }
    elseif ($hostApi -and -not $hostApi.HasExited) {
        Stop-Process -Id $hostApi.Id -Force
    }
    Write-Host '== API stopped =='
}
else {
    Write-Host '== API left running on http://localhost:5080 =='
}

Write-Host ''
Write-Host 'Now run the contract guards:'
Write-Host '  & app\gradlew.bat -p app :composeApp:jvmTest --tests "*ApiContractTest*" --tests "*ApiRouteContractTest*"'
