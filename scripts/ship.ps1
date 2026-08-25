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
# ship.ps1 — the deterministic post-push pipeline: watch CI for a commit, and on
# green pull + restart the API on the deployment host, verify health + image
# freshness, and print one compact report. Red CI deploys nothing and exits 1.
#
#   .\scripts\ship.ps1                 # HEAD of the current branch
#   .\scripts\ship.ps1 -Sha <sha>      # a specific commit
#
# Host config comes from env vars (never committed):
#   NOMNOMZ_DEPLOY_SSH   e.g. "root@192.0.2.10"
#   NOMNOMZ_DEPLOY_KEY   e.g. "$HOME\.ssh\deploy_key"
#   NOMNOMZ_DEPLOY_DIR   compose directory on the host (default /opt/nomnomzbot)

param(
    [string]$Sha = "",
    [int]$HealthTimeoutSec = 150
)

$ErrorActionPreference = "Stop"

function Fail([string]$message) {
    Write-Host "SHIP: FAILED - $message" -ForegroundColor Red
    exit 1
}

# Runs a native command and judges success ONLY by $LASTEXITCODE, never by the presence of stderr
# output. Under Windows PowerShell 5.1, merging a native command's stderr into the pipeline via
# `2>&1` while $ErrorActionPreference = "Stop" is in effect turns ANY stderr write (docker's normal
# pull/build progress, ssh banners, etc.) into a terminating NativeCommandError, aborting the
# script before the exit code is ever checked - even on a successful command. The fix: temporarily
# relax $ErrorActionPreference to "Continue" for the duration of the native call, let stdout+stderr
# interleave to the host for readability, and decide pass/fail from $LASTEXITCODE alone afterwards.
# (Same fix as scripts/switchover.ps1, commit 7e2ba888.)
function Invoke-NativeCommand([string]$cmd) {
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $out = Invoke-Expression "$cmd 2>&1" | Out-String
    }
    finally {
        $ErrorActionPreference = $previousEap
    }
    return @{ Output = $out.TrimEnd("`r", "`n"); ExitCode = $LASTEXITCODE }
}

# ── Resolve inputs ────────────────────────────────────────────────────────────
# Always expand to the FULL sha — `gh run list -c` silently returns nothing for a short one.
$Sha = if ($Sha -eq "") { (git rev-parse HEAD).Trim() } else { (git rev-parse $Sha).Trim() }
$sshTarget = $env:NOMNOMZ_DEPLOY_SSH
$sshKey = $env:NOMNOMZ_DEPLOY_KEY
$deployDir = if ($env:NOMNOMZ_DEPLOY_DIR) { $env:NOMNOMZ_DEPLOY_DIR } else { "/opt/nomnomzbot" }
if (-not $sshTarget -or -not $sshKey) {
    Fail "set NOMNOMZ_DEPLOY_SSH (user@host) and NOMNOMZ_DEPLOY_KEY (ssh key path) first"
}

# ── 1. Find the CI run for the sha (it can lag the push by a few seconds) ────
$runId = $null
for ($i = 0; $i -lt 12 -and -not $runId; $i++) {
    $runId = gh run list -c $Sha --json databaseId --jq '.[0].databaseId' 2>$null
    if (-not $runId) { Start-Sleep -Seconds 5 }
}
if (-not $runId) { Fail "no CI run appeared for $Sha" }
Write-Host "SHIP: watching CI run $runId for $($Sha.Substring(0,8))..."

# ── 2. Block on CI — poll status, tolerating transient GitHub API 5xx/network blips ──
# `gh run watch --exit-status` aborts on ANY transient error, and during a GitHub API wobble a 503 looks
# identical to a red run — which is exactly the false "CI RED, nothing deployed" this pipeline hit
# repeatedly during the 2026-07-20 API outage. Poll the run's own status/conclusion instead: a failed API
# call is a transient blip to be retried, and ONLY an actual non-success conclusion is red.
function Get-RunState([string]$id) {
    # "status|conclusion" on success, or $null when the API call itself failed (a blip to retry, not red).
    # NO SPACE after the comma: PowerShell splits `status, conclusion` into TWO arguments, gh rejects it
    # with "accepts at most 1 arg(s)", every poll returns $null, and the loop then spends its whole
    # transient budget before reporting a GitHub outage that never happened. This script had never once
    # watched a run successfully because of that one space.
    $json = gh run view $id --json status,conclusion 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { return $null }
    try { $o = $json | ConvertFrom-Json } catch { return $null }
    return "$($o.status)|$($o.conclusion)"
}

$deadlineMin = 45          # CI image build is ~25 min; this is generous headroom.
$pollSec = 15
$heartbeatSec = 120        # Silence during a normal ~25min wait is indistinguishable from a hang -
                            # this is what actually got the script killed mid-run twice. Say something.
$maxTransient = 40         # ~10 min of consecutive API failures before we give up (never as "red").
# Prove the query works ONCE before entering the wait: a malformed gh call is indistinguishable from an
# outage inside the loop, and a wrong argument must not cost 10 minutes to surface.
if (-not (Get-RunState $runId)) {
    Fail "cannot read run $runId state - check the gh query itself before blaming the API (try: gh run view $runId --json status,conclusion)"
}

$status = ""; $conclusion = ""; $transient = 0; $lastHeartbeat = 0
for ($elapsed = 0; $elapsed -lt ($deadlineMin * 60); $elapsed += $pollSec) {
    if (($elapsed - $lastHeartbeat) -ge $heartbeatSec) {
        Write-Host "SHIP: still watching run $runId ($([math]::Round($elapsed / 60, 1)) min elapsed)..."
        $lastHeartbeat = $elapsed
    }
    $state = Get-RunState $runId
    if (-not $state) {
        $transient++
        if ($transient -ge $maxTransient) {
            Fail "GitHub API unreachable for ~$([math]::Round($maxTransient * $pollSec / 60)) min while watching run $runId (transient 5xx); CI status NOT confirmed - nothing deployed"
        }
        Start-Sleep -Seconds $pollSec
        continue
    }
    $transient = 0
    $parts = $state -split '\|', 2
    $status = $parts[0]; $conclusion = $parts[1]
    if ($status -eq "completed") { break }
    Start-Sleep -Seconds $pollSec
}
if ($status -ne "completed") { Fail "CI run $runId did not complete within $deadlineMin min - nothing deployed" }
if ($conclusion -ne "success") {
    Fail "CI run $runId concluded '$conclusion' for $($Sha.Substring(0,8)) - nothing deployed. Fix master now."
}
# The run concluded success, so every job (incl. the image build) passed. Label the image job best-effort.
$imageJob = gh run view $runId --json jobs --jq '[.jobs[] | select(.name | test("image"; "i"))][0].conclusion' 2>$null
if ([string]::IsNullOrWhiteSpace($imageJob)) { $imageJob = "success" }
Write-Host "SHIP: CI green (image job: $imageJob)."

# ── 3. Deploy: blue/green switchover, poll readiness, verify image freshness ─
# There is no `api` service — docker-compose.yml fronts api-blue/api-green with Caddy, which routes to
# whichever passes health. `docker compose up -d api` fails with "no such service: api" and deploys
# nothing (it did exactly that on 2026-08-25). Pick the idle colour, start it, then drain the old one.
$remote = @"
cd $deployDir
export COMPOSE_PROFILES=green
ps_out=`$(docker ps --filter name=nomnomzbot-api- --format '{{.Names}}' || true)
blue_up=`$(echo "`$ps_out" | grep -c 'nomnomzbot-api-blue' || true)
green_up=`$(echo "`$ps_out" | grep -c 'nomnomzbot-api-green' || true)
if [ "`$blue_up" -gt 0 ] && [ "`$green_up" -gt 0 ]; then
  echo "health=000"; echo "ambiguous=both-colours-running"; exit 0
fi
if [ "`$green_up" -gt 0 ]; then live=green; idle=blue; else live=blue; idle=green; fi
docker compose pull -q "api-`$idle" && docker compose up -d --no-deps "api-`$idle" >/dev/null 2>&1
code=000
for i in `$(seq 1 $([math]::Ceiling($HealthTimeoutSec / 8))); do
  code=`$(docker exec "nomnomzbot-api-`$idle" curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health/ready 2>/dev/null || echo 000)
  [ "`$code" = "200" ] && break
  sleep 8
done
echo "health=`$code"
echo "deployed_colour=`$idle"
if [ "`$code" = "200" ]; then
  if [ "`$blue_up" -gt 0 ] || [ "`$green_up" -gt 0 ]; then docker compose stop -t 25 "api-`$live" >/dev/null 2>&1; fi
else
  docker compose stop "api-`$idle" >/dev/null 2>&1 || true
fi
echo "image_created=`$(docker inspect --format '{{.Created}}' ghcr.io/nomercylabs/nomnomzbot:latest)"
echo "image_digest=`$(docker inspect --format '{{index .RepoDigests 0}}' ghcr.io/nomercylabs/nomnomzbot:latest)"
echo "container=`$(docker ps --filter name=nomnomzbot-api --format '{{.Status}}')"
"@
# The remote script goes over STDIN to `bash -s`, never inside a command STRING. Invoke-NativeCommand runs
# its argument through Invoke-Expression, so PowerShell re-parses whatever it is given: every `>/dev/null`
# in the script above became a LOCAL redirect and the deploy died on "Could not find a part of the path
# 'C:/dev/null'". Piping the script in means bash is the only thing that ever parses bash.
$ErrorActionPreference = "Continue"
$deployOutput = ($remote | & ssh -i $sshKey -o StrictHostKeyChecking=accept-new $sshTarget "bash -s" 2>&1 | Out-String)
$deployExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
$deployResult = @{ Output = $deployOutput.TrimEnd("`r", "`n"); ExitCode = $deployExit }
if ($deployResult.ExitCode -ne 0) { Fail "ssh deploy step failed: $($deployResult.Output)" }
$deployOut = $deployResult.Output

# Split to LINES first: the output is one multi-line string, and `^` against a single blob only ever
# matches its very first line — so every field after the first silently came back null.
$deployLines = $deployOut -split "`r?`n"
function Get-Field([string]$name) {
    $match = $deployLines | Select-String -Pattern "^$name=(.+)$" | Select-Object -First 1
    if (-not $match) { Fail "deploy output carried no '$name=' line - remote script output was:`n$deployOut" }
    return $match.Matches.Groups[1].Value
}

$health = Get-Field 'health'
$imageCreated = Get-Field 'image_created'
$imageDigest = Get-Field 'image_digest'
$container = Get-Field 'container'
if ($health -ne "200") { Fail "API did not become ready (health=$health) after deploy" }

# `docker compose pull` succeeded, so the host now runs EXACTLY the registry's :latest — which the
# green image job just (re)published for this commit. An old Created timestamp only means the cached
# build reproduced an identical image (no code change in the image), which is fine and reported as such.
$runStarted = gh api "repos/NoMercyLabs/nomnomzbot/actions/runs/$runId" --jq '.run_started_at' 2>$null
$freshness =
    if ([string]::IsNullOrWhiteSpace($runStarted)) { "unknown (could not read run start time)" }
    elseif ([datetime]$imageCreated -ge [datetime]$runStarted) { "rebuilt" }
    else { "unchanged (cache-identical build)" }

# ── 4. Report ─────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "SHIP: DEPLOYED" -ForegroundColor Green
Write-Host "  commit     $Sha"
Write-Host "  ci run     $runId (green; image job: $imageJob)"
Write-Host "  health     $health"
Write-Host "  image      $freshness - created $imageCreated"
Write-Host "  digest     $imageDigest"
Write-Host "  container  $container"
exit 0
