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
# switchover.ps1 — the blue/green deploy step: acquire the new image (pull if API_IMAGE points at
# a registry, build if it's a bare local tag — see step 2), start the IDLE colour alongside the
# live one, wait for it to pass /health/ready, and only then stop the old colour (letting it
# drain). The port (Caddy, docker-compose.yml) is never dark: it is fronted by TWO api-* services
# and Caddy only routes to whichever currently passes health — this script just decides which one
# that should be.
#
#   .\scripts\switchover.ps1                       # local compose stack (repo root)
#   .\scripts\switchover.ps1 -ReadyTimeoutSec 180   # slower box / cold image pull or build
#   .\scripts\switchover.ps1 -Build                 # force a local rebuild regardless of API_IMAGE
#
# Remote host (same convention as ship.ps1) — set both, or the script runs against the LOCAL
# compose stack in this repo instead:
#   NOMNOMZ_DEPLOY_SSH   e.g. "root@192.0.2.10"
#   NOMNOMZ_DEPLOY_KEY   e.g. "$HOME\.ssh\deploy_key"
#   NOMNOMZ_DEPLOY_DIR   compose directory on the host (default /opt/nomnomzbot)
#
# Re-runnable: the script inspects `docker ps` to work out which colour (api-blue / api-green)
# is currently live rather than being told, so running it twice in a row, or after a previous
# failed attempt left the idle colour started, converges instead of double-switching.
#
# Failure contract: if the new colour never becomes ready within the timeout, the new colour is
# stopped, the OLD colour is left serving (never touched until the new one has proven itself),
# and the script exits non-zero. There is no path that stops the old colour before the new one
# is confirmed ready.

param(
    [int]$ReadyTimeoutSec = 120,
    [int]$DrainSec = 35, # >= the app's ~30s /health/ready drain window (see Program.cs shutdown).
    [switch]$Build # force a local rebuild instead of pulling, regardless of API_IMAGE
)

$ErrorActionPreference = "Stop"

function Fail([string]$message) {
    Write-Host "SWITCHOVER: FAILED - $message" -ForegroundColor Red
    exit 1
}

$sshTarget = $env:NOMNOMZ_DEPLOY_SSH
$sshKey = $env:NOMNOMZ_DEPLOY_KEY
$deployDir = if ($env:NOMNOMZ_DEPLOY_DIR) { $env:NOMNOMZ_DEPLOY_DIR } else { "/opt/nomnomzbot" }
$repoRoot = Split-Path -Parent $PSScriptRoot
$remoteMode = [bool]$sshTarget

if ($remoteMode -and -not $sshKey) {
    Fail "NOMNOMZ_DEPLOY_SSH is set but NOMNOMZ_DEPLOY_KEY is not"
}

# Run one shell command against whichever target is active (remote host over SSH, or the local
# compose stack in this repo) and return its stdout. Non-zero exit -> Fail with the command shown.
# Runs a native command and judges success ONLY by $LASTEXITCODE, never by the presence of stderr
# output. Under Windows PowerShell 5.1, merging a native command's stderr into the pipeline via
# `2>&1` while $ErrorActionPreference = "Stop" is in effect turns ANY stderr write (docker's normal
# build/pull progress, warnings, etc.) into a terminating NativeCommandError, aborting the script
# before the exit code is ever checked — even on a successful command. PowerShell 7 doesn't have
# this problem, which is why a previous "verified" run (under pwsh) didn't catch it. The fix:
# temporarily relax $ErrorActionPreference to "Continue" for the duration of the native call (so a
# stderr write is just a stream write, not a terminating error), let stdout+stderr interleave to
# the host for readability, and decide pass/fail from $LASTEXITCODE alone afterwards.
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

# Local runs need the same profile visible to compose (see the remote branch below).
$env:COMPOSE_PROFILES = "green"

function Invoke-Target([string]$cmd) {
    if ($remoteMode) {
        # api-green is profiled in docker-compose.yml so a bare `up -d` cannot start BOTH
        # colours (two bots = duplicate EventSub + duplicate chat). Export it so the colour
        # commands below can still address green explicitly.
        $full = "cd $deployDir && export COMPOSE_PROFILES=green && $cmd"
        $result = Invoke-NativeCommand "ssh -i $sshKey -o StrictHostKeyChecking=accept-new $sshTarget `"$full`""
        if ($result.ExitCode -ne 0) { Fail "remote command failed (`"$cmd`"): $($result.Output)" }
        Write-Host $result.Output
        return $result.Output
    }
    else {
        Push-Location $repoRoot
        try {
            $result = Invoke-NativeCommand $cmd
            if ($result.ExitCode -ne 0) { Fail "local command failed (`"$cmd`"): $($result.Output)" }
            Write-Host $result.Output
            return $result.Output
        }
        finally { Pop-Location }
    }
}

# Same as Invoke-Target but never Fail()s on a non-zero exit — used for probes where "not ready
# yet" / "not running yet" is an expected, retried outcome, not a hard error.
function Invoke-TargetSoft([string]$cmd) {
    if ($remoteMode) {
        $full = "cd $deployDir && export COMPOSE_PROFILES=green && $cmd"
        return (Invoke-NativeCommand "ssh -i $sshKey -o StrictHostKeyChecking=accept-new $sshTarget `"$full`"").Output
    }
    else {
        Push-Location $repoRoot
        try { return (Invoke-NativeCommand $cmd).Output }
        finally { Pop-Location }
    }
}

Write-Host "SWITCHOVER: target = $(if ($remoteMode) { "$sshTarget`:$deployDir" } else { "local ($repoRoot)" })"

# ── 1. Work out which colour is currently live ────────────────────────────────
$psOut = Invoke-Target "docker ps --filter name=nomnomzbot-api- --format '{{.Names}}'"
$blueUp = $psOut -match "nomnomzbot-api-blue"
$greenUp = $psOut -match "nomnomzbot-api-green"

if ($blueUp -and $greenUp) {
    Fail "both api-blue and api-green are already running - resolve the ambiguous state manually before switching over (a prior run may have failed mid-way)"
}

# Neither running (first-ever deploy on this host) -> bootstrap on blue as the live colour, so
# this run brings up green as the first switch.
$liveColor = if ($greenUp) { "green" } elseif ($blueUp) { "blue" } else { "blue" }
$idleColor = if ($liveColor -eq "blue") { "green" } else { "blue" }
Write-Host "SWITCHOVER: live = api-$liveColor, deploying to idle = api-$idleColor"

# ── 2. Acquire the new image, start the idle colour alongside the live one ───
# API_IMAGE selects the path: a registry ref (e.g. ghcr.io/nomercylabs/nomnomzbot:latest, used on
# the Proxmox box) is PULLED — a real pull failure (network down, bad creds, tag missing) must
# still abort here via Invoke-Target's normal Fail(), same as before, so a stale image is never
# served silently. A bare local tag (the default `nomnomzbot-api:local` from `docker compose up -d
# --build`, no registry/namespace segment) was never pushed anywhere, so pulling it always fails
# hard even though the image is right there — that's the defect this branch fixes. -Build forces
# the local-build path regardless of the configured tag.
# `docker compose config --images <service>` scopes to that service plus its dependency closure
# (api-* depends_on postgres + redis), so it comes back as 3 lines, not 1 — filter out the known
# infra images rather than assume ordering.
$imageLines = (Invoke-Target "docker compose config --images api-$idleColor") -split "`r?`n"
$resolvedImage = ($imageLines | Where-Object { $_ -and $_ -notmatch "^(postgres|redis):" } | Select-Object -First 1)
if (-not $resolvedImage) { Fail "could not resolve the image for api-$idleColor from 'docker compose config --images'" }
$resolvedImage = $resolvedImage.Trim()
$isLocalTag = $resolvedImage -notmatch "/"
if ($Build -or $isLocalTag) {
    Write-Host "SWITCHOVER: image '$resolvedImage' is a local-build tag - building instead of pulling"
    Invoke-Target "docker compose build api-$idleColor"
}
else {
    Invoke-Target "docker compose pull -q api-$idleColor"
}
Invoke-Target "docker compose up -d api-$idleColor"

# ── 3. Wait for the idle colour to pass its OWN /health/ready (in-container, not through Caddy —
#      this proves the new instance itself is ready, independent of the proxy's poll cadence) ──
$ready = $false
$deadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
while ((Get-Date) -lt $deadline) {
    $code = Invoke-TargetSoft "docker exec nomnomzbot-api-$idleColor curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health/ready"
    if ("$code".Trim() -eq "200") { $ready = $true; break }
    Start-Sleep -Seconds 5
}

if (-not $ready) {
    Write-Host "SWITCHOVER: api-$idleColor did not become ready within ${ReadyTimeoutSec}s - rolling back, api-$liveColor keeps serving" -ForegroundColor Yellow
    Invoke-TargetSoft "docker compose stop api-$idleColor" | Out-Null
    Fail "new instance (api-$idleColor) never passed /health/ready - old instance (api-$liveColor) was left running and untouched; nothing was cut over"
}
Write-Host "SWITCHOVER: api-$idleColor is ready"

# ── 4. Only now stop the old colour, with a stop timeout >= the drain window ─
# `docker stop -t <seconds>` sends SIGTERM immediately and waits up to <seconds> before SIGKILL;
# the app itself starts failing /health/ready on SIGTERM so Caddy stops routing to it right away,
# while in-flight requests it already accepted get up to $DrainSec to finish.
Invoke-Target "docker compose stop -t $DrainSec api-$liveColor"
Write-Host "SWITCHOVER: api-$liveColor stopped (drained, up to ${DrainSec}s)"

Write-Host ""
Write-Host "SWITCHOVER: DONE - api-$idleColor is now live, api-$liveColor is idle" -ForegroundColor Green
exit 0
