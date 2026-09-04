# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
#
# proxmox-triage.ps1 — one command that answers "what is wrong with the deployed bot".
#
# Replaces the ad-hoc SSH sequence that gets re-derived by hand during every incident (and drifts a
# step each time). Read-only by default: it inspects, it never restarts or mutates anything.
#
#   .\scripts\proxmox-triage.ps1                  # full triage to the console
#   .\scripts\proxmox-triage.ps1 -PullLogs        # also copy full container logs + digest locally
#   .\scripts\proxmox-triage.ps1 -Since 30m       # narrow the log window
#
# Verification altitude: the reachability probe runs from THIS machine against the LAN origin
# (192.168.2.60:5080) and the public origin, not from inside the container. A container-local
# /health/ready cannot fail the way the owner's browser fails, so it is reported separately and
# never on its own.

[CmdletBinding()]
param(
    [string] $ServerHost = '192.168.2.60',
    [string] $SshKey     = "$env:USERPROFILE\.ssh\docker_proxmox",
    [string] $StackDir   = '/opt/nomnomzbot',
    [string] $PublicUrl  = 'https://dev.nomnomz.bot',
    [string] $Since      = '2h',
    [switch] $PullLogs,
    [string] $OutDir     = ".scratch/proxmox-triage-$(Get-Date -Format yyyyMMdd-HHmmss)"
)

$ErrorActionPreference = 'Stop'
$sshTarget = "root@$ServerHost"

function Invoke-Remote {
    param([Parameter(Mandatory)][string] $Command)
    # The script bodies below are bash containing both quote characters, regex, and backslash
    # continuations. Passing that through PowerShell's native-argument quoting mangles it (observed:
    # "unexpected EOF while looking for matching quote"). Base64 makes the payload a single opaque
    # token, so neither PowerShell nor the remote shell can reinterpret anything inside it.
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Command -replace "`r`n", "`n")))
    # -BatchMode so a missing key fails fast instead of hanging on a password prompt.
    $output = & ssh -o BatchMode=yes -o ConnectTimeout=10 -i $SshKey $sshTarget "echo $encoded | base64 -d | bash" 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Warning "remote command exited $LASTEXITCODE" }
    return $output
}

function Write-Section {
    param([Parameter(Mandatory)][string] $Title)
    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

# --- 1. Reachability, from HERE — the altitude the owner actually uses ------------------------
Write-Section 'Reachability (from this machine, not from inside the container)'
foreach ($probe in @(
    @{ Name = 'LAN    '; Url = "http://${ServerHost}:5080" },
    @{ Name = 'Public '; Url = $PublicUrl }
)) {
    foreach ($path in @('/health/ready', '/health/version')) {
        $url = "$($probe.Url)$path"
        try {
            $response = Invoke-WebRequest -Uri $url -TimeoutSec 15 -SkipHttpErrorCheck
            $body = $response.Content
            if ($body.Length -gt 160) { $body = $body.Substring(0, 160) }
            $colour = if ($response.StatusCode -eq 200) { 'Green' } else { 'Red' }
            Write-Host ("{0} {1,-18} {2}  {3}" -f $probe.Name, $path, $response.StatusCode, $body) -ForegroundColor $colour
        }
        catch {
            Write-Host ("{0} {1,-18} UNREACHABLE  {2}" -f $probe.Name, $path, $_.Exception.Message) -ForegroundColor Red
        }
    }
}

# --- 2. Containers ---------------------------------------------------------------------------
Write-Section 'Containers (blue/green — there is no "api" service)'
Invoke-Remote "docker ps -a --filter name=nomnomzbot --format '{{.Names}}\t{{.Status}}\t{{.Image}}'"

Write-Section 'Exit reason for any stopped colour'
Invoke-Remote @'
for c in nomnomzbot-api-blue nomnomzbot-api-green; do
  state=$(docker inspect "$c" --format '{{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} started={{.State.StartedAt}} finished={{.State.FinishedAt}}' 2>/dev/null) \
    && echo "$c  $state"
done
'@

# --- 3. Host resources -----------------------------------------------------------------------
Write-Section 'Host memory / swap (rules OOM in or out)'
Invoke-Remote 'free -m; echo; df -h / | tail -2'

# --- 4. Single-colour guard ------------------------------------------------------------------
Write-Section 'Blue/green drift guard (last 20 lines)'
Invoke-Remote "tail -20 $StackDir/guard-single-color.log 2>/dev/null || echo '(no guard log)'"

# --- 5. Deduplicated error profile — the highest-signal view ---------------------------------
Write-Section "Distinct ERR/WRN/FTL by frequency (last $Since)"
# Literal here-string (@'...'@): every $ and \ below belongs to bash, not PowerShell. The one value
# that must come from PowerShell is substituted by name afterwards.
$errorProfile = @'
for c in nomnomzbot-api-blue nomnomzbot-api-green; do
  docker inspect "$c" >/dev/null 2>&1 || continue
  echo "-- $c"
  docker logs --since __SINCE__ "$c" 2>&1 \
    | grep -hoE '\[[0-9:]+ (ERR|WRN|FTL)\].*' \
    | sed -E 's/^\[[0-9:]+ //' \
    | cut -c1-150 \
    | sed -E 's/[0-9a-f]{8}-[0-9a-f-]{27}/<guid>/g; s/[0-9]+/N/g' \
    | sort | uniq -c | sort -rn | head -25
  echo
done
'@
Invoke-Remote ($errorProfile -replace '__SINCE__', $Since)

# --- 6. EventSub health — the spiral signature ------------------------------------------------
Write-Section "EventSub health (4003 'connection unused' is the death-spiral tell)"
$eventSub = @'
c=nomnomzbot-api-blue
docker ps --format '{{.Names}}' | grep -q "$c" || c=nomnomzbot-api-green
echo "container: $c"
echo "session welcomes      : $(docker logs --since __SINCE__ $c 2>&1 | grep -c 'session welcome')"
echo "closed 4003 unused    : $(docker logs --since __SINCE__ $c 2>&1 | grep -c 'code 4003')"
echo "chat messages received: $(docker logs --since __SINCE__ $c 2>&1 | grep -c 'ChatMessageReceivedEvent')"
echo
echo 'Helix eventsub failures by code:'
docker logs --since __SINCE__ $c 2>&1 \
  | grep -oE 'Helix (POST|DELETE) eventsub/subscriptions failed: [0-9]+ \([a-z_]+\)' \
  | sort | uniq -c | sort -rn
echo
echo 'Twitch error details:'
docker logs --since __SINCE__ $c 2>&1 \
  | grep -oE 'error detail: .*' | cut -c1-90 | sort | uniq -c | sort -rn | head -10
'@
Invoke-Remote ($eventSub -replace '__SINCE__', $Since)

# --- 7. Enabling state — the empty-count check that gets skipped -------------------------------
Write-Section 'Enabling state (an empty count here explains more than any log line)'
$sql = @'
SELECT
  (SELECT count(*) FROM "Channels" WHERE "DeletedAt" IS NULL)                                    AS channels,
  (SELECT count(*) FROM "IntegrationConnections" WHERE "Provider"='twitch'     AND "Status"='connected') AS twitch_conns,
  (SELECT count(*) FROM "IntegrationConnections" WHERE "Provider"='twitch_bot' AND "Status"='connected') AS bot_conns,
  (SELECT count(*) FROM "BotAccounts" WHERE "IsActive" AND "DeletedAt" IS NULL)                  AS active_bots;
'@
Invoke-Remote "docker exec nomnomzbot-postgres psql -U nomnomzbot -d nomnomzbot -c `"$($sql -replace '"','\"' -replace "`r?`n",' ')`""

Write-Section 'Integration connections needing attention'
$sqlConns = @'
SELECT "Provider", "Status", "ConsecutiveFailureCount" AS fails, "LastErrorAt"
FROM "IntegrationConnections"
WHERE "Status" <> 'connected' OR "ConsecutiveFailureCount" > 0
ORDER BY "ConsecutiveFailureCount" DESC;
'@
Invoke-Remote "docker exec nomnomzbot-postgres psql -U nomnomzbot -d nomnomzbot -c `"$($sqlConns -replace '"','\"' -replace "`r?`n",' ')`""

Write-Section 'Expired provider tokens (still polled unless status-gated)'
$sqlTokens = @'
SELECT c."Provider", c."Status", t."TokenType", t."ExpiresAt",
       (t."ExpiresAt" < now()) AS expired
FROM "IntegrationTokens" t JOIN "IntegrationConnections" c ON c."Id" = t."ConnectionId"
WHERE t."TokenType" = 'access' AND t."ExpiresAt" < now()
ORDER BY t."ExpiresAt";
'@
Invoke-Remote "docker exec nomnomzbot-postgres psql -U nomnomzbot -d nomnomzbot -c `"$($sqlTokens -replace '"','\"' -replace "`r?`n",' ')`""

# --- 8. Optional full log pull ----------------------------------------------------------------
if ($PullLogs) {
    Write-Section "Pulling full logs to $OutDir"
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Invoke-Remote 'docker logs nomnomzbot-api-blue  > /tmp/nnz-blue.log  2>&1 || true; docker logs nomnomzbot-api-green > /tmp/nnz-green.log 2>&1 || true; wc -l /tmp/nnz-blue.log /tmp/nnz-green.log'
    foreach ($pair in @(@('nnz-blue.log', 'api-blue-full.log'), @('nnz-green.log', 'api-green-full.log'))) {
        & scp -o BatchMode=yes -i $SshKey "${sshTarget}:/tmp/$($pair[0])" (Join-Path $OutDir $pair[1]) 2>&1 | Out-Null
    }
    Get-ChildItem $OutDir | Format-Table Name, Length
    Write-Host "Logs in $OutDir" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Triage complete (read-only — nothing was restarted or modified).' -ForegroundColor Green
