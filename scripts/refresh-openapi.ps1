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

[bool]$inContainer = [bool](docker ps --filter "name=$Container" --format '{{.Names}}' 2>$null)
if (-not $inContainer) {
    throw "container '$Container' is not running; start the devbox or adapt this script for a host-side run"
}

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

# The dev key lives in appsettings.Development.json, which carries // comments that JSON.parse rejects.
[string]$settings = Join-Path $repo 'server/src/NomNomzBot.Api/appsettings.Development.json'
[string]$raw = (Get-Content -Raw -LiteralPath $settings) -replace '(?m)//.*$', ''
[string]$key = ($raw | ConvertFrom-Json).Encryption.Key
if ([string]::IsNullOrWhiteSpace($key)) { throw 'no Encryption:Key in appsettings.Development.json' }

Write-Host '== starting the API (allow ~3 minutes) =='
docker exec -e ASPNETCORE_ENVIRONMENT=Development -e "Encryption__Key=$key" -d $Container `
    sh -lc 'cd /workspace/server/src/NomNomzBot.Api && dotnet run --no-launch-profile --urls http://0.0.0.0:5080 > /tmp/openapi-run.log 2>&1'

[int]$waited = 0
[string]$health = '000'
while ($waited -lt $TimeoutSeconds) {
    Start-Sleep -Seconds 6
    $waited += 6
    $health = docker exec $Container sh -lc 'curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:5080/health' 2>$null
    if ($health -eq '200') { break }
    [string]$fatal = docker exec $Container sh -lc 'grep -iE "FTL|Hosting failed" /tmp/openapi-run.log | head -1' 2>$null
    if ($fatal) { throw "API failed to start: $fatal" }
}
if ($health -ne '200') { throw "API did not reach health 200 within ${TimeoutSeconds}s" }
Write-Host "   health 200 after ${waited}s"

Write-Host '== fetching the document =='
docker exec $Container sh -lc 'curl -sf http://localhost:5080/openapi/v1.json -o /tmp/openapi-fetched.json && wc -c < /tmp/openapi-fetched.json' | Out-Host
docker cp "${Container}:/tmp/openapi-fetched.json" $snapshot

# A freshly generated document line-diffs enormously against the committed one purely from key
# ordering, so judge it SEMANTICALLY. A path or schema DISAPPEARING is the signal that matters.
[int]$paths = (Get-Content -Raw -LiteralPath $snapshot | ConvertFrom-Json).paths.PSObject.Properties.Count
Write-Host "   snapshot now carries $paths paths"

if (-not $KeepRunning) {
    docker exec $Container sh -lc 'pkill -f "dotnet run" 2>/dev/null; true' | Out-Null
    Write-Host '== API stopped =='
}
else {
    Write-Host '== API left running on http://localhost:5080 =='
}

Write-Host ''
Write-Host 'Now run the contract guards:'
Write-Host '  & app\gradlew.bat -p app :composeApp:jvmTest --tests "*ApiContractTest*" --tests "*ApiRouteContractTest*"'
