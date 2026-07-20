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
    $json = gh run view $id --json status, conclusion 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { return $null }
    try { $o = $json | ConvertFrom-Json } catch { return $null }
    return "$($o.status)|$($o.conclusion)"
}

$deadlineMin = 45          # CI image build is ~25 min; this is generous headroom.
$pollSec = 15
$maxTransient = 40         # ~10 min of consecutive API failures before we give up (never as "red").
$status = ""; $conclusion = ""; $transient = 0
for ($elapsed = 0; $elapsed -lt ($deadlineMin * 60); $elapsed += $pollSec) {
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

# ── 3. Deploy: pull + restart the API, poll readiness, verify image freshness ─
$remote = @"
cd $deployDir && docker compose pull -q api && docker compose up -d api >/dev/null 2>&1
code=000
for i in `$(seq 1 $([math]::Ceiling($HealthTimeoutSec / 8))); do
  code=`$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5080/health/ready)
  [ "`$code" = "200" ] && break
  sleep 8
done
echo "health=`$code"
echo "image_created=`$(docker inspect --format '{{.Created}}' ghcr.io/nomercylabs/nomnomzbot:latest)"
echo "image_digest=`$(docker inspect --format '{{index .RepoDigests 0}}' ghcr.io/nomercylabs/nomnomzbot:latest)"
echo "container=`$(docker ps --filter name=nomnomzbot-api --format '{{.Status}}')"
"@
$deployOut = ssh -i $sshKey -o StrictHostKeyChecking=accept-new $sshTarget $remote
if ($LASTEXITCODE -ne 0) { Fail "ssh deploy step failed" }

$health = ($deployOut | Select-String '^health=(\d+)').Matches.Groups[1].Value
$imageCreated = ($deployOut | Select-String '^image_created=(.+)').Matches.Groups[1].Value
$imageDigest = ($deployOut | Select-String '^image_digest=(.+)').Matches.Groups[1].Value
$container = ($deployOut | Select-String '^container=(.+)').Matches.Groups[1].Value
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
