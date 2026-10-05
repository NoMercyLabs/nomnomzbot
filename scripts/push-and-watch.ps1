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
# Push the current branch to origin/master and BLOCK until CI reaches a verdict.
#
# The CI Gate says a push is not done until CI is green, and the watch is part of the push - never
# "CI will probably pass". This exists because the push -> find run id -> watch -> diagnose -> re-run
# loop was being re-derived by hand every time, which is exactly where a step gets skipped.
#
#   scripts/push-and-watch.ps1                  # push HEAD:master, watch, auto-retry one flake
#   scripts/push-and-watch.ps1 -NoRetry         # never re-run; a red is a red
#   scripts/push-and-watch.ps1 -DryRun          # watches the run for the current HEAD, without pushing
#
# Exit code is the verdict: 0 green, 1 red. On red it prints the failing jobs and the first error
# lines, so the next step is diagnosis rather than another round of gh incantations.
#
# One flake re-run is allowed by default because this repo has a known non-reproducing red (the
# Application suite ~5%; the SQLite concurrent-writer soak was replaced by a deterministic lock test on
# 2026-09-30 after it failed twice in a row on CI). A re-run is
# NOT a fix: the script says loudly when it retried, so a test that "only fails in CI" cannot quietly
# become normal.

param(
    [switch]$NoRetry,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repo = (Join-Path $PSScriptRoot '..' | Resolve-Path).Path

function Invoke-Native {
    param([Parameter(Mandatory = $true)][string]$FailureMessage, [Parameter(Mandatory = $true)][scriptblock]$Command)
    # Judge native commands by $LASTEXITCODE. Never merge stderr with 2>&1 under ErrorActionPreference
    # = Stop: git and gh write ordinary progress to stderr and it becomes a terminating error.
    & $Command
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

Push-Location $repo
try {
    # Read the SHA once, BEFORE the push, and push exactly that commit: a commit landed on this shared
    # tree during the push moved HEAD, and the watch then waited for a run that could never exist.
    [string]$sha = (git rev-parse HEAD).Trim()
    if (-not $DryRun) {
        Write-Host "== pushing $($sha.Substring(0,9)) to origin/master =="
        Invoke-Native 'git push failed - rebase onto origin/master and retry' { git push origin "${sha}:refs/heads/master" }
    }

    # The run for the just-pushed commit does not exist instantly; poll briefly for it by SHA rather
    # than grabbing "the latest run", which can be someone else's push.
    # Filter in PowerShell rather than with `gh -q`: a jq expression containing the SHA has to survive
    # PowerShell quoting AND jq quoting, and it silently matched nothing when it did not.
    [string]$runId = $null
    [int]$runWaitSeconds = 180
    foreach ($attempt in 1..($runWaitSeconds / 5)) {
        [string]$json = gh run list --limit 20 --json databaseId,headSha  # no space: PowerShell would parse `a, b` as an array and pass two args
        if ($json) {
            $match = ($json | ConvertFrom-Json) | Where-Object { $_.headSha -eq $sha } | Select-Object -First 1
            if ($match) { $runId = [string]$match.databaseId; break }
        }
        Start-Sleep -Seconds 5
    }
    if (-not $runId) {
        # No run created is NOT the same as a run that failed. GitHub Actions can be degraded or the
        # trigger dropped; the commit is pushed and will build when Actions recovers. Say which, so this
        # is never mistaken for a red build, and never for a deploy either.
        Write-Host ''
        Write-Host "NO CI RUN was created for $sha after ${runWaitSeconds}s."
        try {
            [string]$actions = (Invoke-RestMethod -Uri 'https://www.githubstatus.com/api/v2/components.json' -TimeoutSec 10).components |
                Where-Object { $_.name -eq 'Actions' } | ForEach-Object { $_.status }
            Write-Host "githubstatus.com reports Actions: $actions"
        }
        catch { Write-Host 'could not reach githubstatus.com' }
        Write-Host 'The commit IS pushed. Nothing is building and nothing will deploy until a run exists.'
        exit 1
    }

    Write-Host "== watching run $runId for $($sha.Substring(0,8)) =="
    gh run watch $runId --exit-status | Out-Host
    [bool]$green = ($LASTEXITCODE -eq 0)

    if (-not $green -and -not $NoRetry) {
        Write-Host ''
        Write-Host '== CI RED - failing jobs =='
        (gh run view $runId --json jobs | ConvertFrom-Json).jobs |
            Where-Object { $_.conclusion -eq 'failure' } |
            ForEach-Object { Write-Host "  $($_.name)" }
        gh run view $runId --log-failed | Select-String -Pattern 'error|FAIL|Failed:' | Select-Object -First 10 | Out-Host

        Write-Host ''
        Write-Host '== RE-RUNNING FAILED JOBS ONCE (flake check - this is not a fix) =='
        Invoke-Native 'gh run rerun failed' { gh run rerun $runId --failed }
        Start-Sleep -Seconds 8
        gh run watch $runId --exit-status | Out-Host
        $green = ($LASTEXITCODE -eq 0)
        if ($green) {
            Write-Host ''
            Write-Host 'NOTE: green only AFTER a re-run. That test failed once in CI and not on the'
            Write-Host '      re-run - treat it as a real flake to fix, not as a pass.'
        }
    }

    if ($green) {
        Write-Host ''
        (gh run view $runId --json jobs | ConvertFrom-Json).jobs |
            ForEach-Object { Write-Host "  $($_.name): $($_.conclusion)" }
        Write-Host 'CI GREEN'

        # A green run is not a deployed commit. A run can be CANCELLED by the concurrency rule when a
        # newer push lands mid-run, which skips the image build and the deploy entirely while every
        # local signal still says "pushed, green". The only ground truth is what the box reports.
        #
        # Compare against the sha THIS RUN shipped, not `git rev-parse HEAD`: on a tree several agents
        # push to, HEAD moves while the ~25min image build runs, and comparing against the moved HEAD
        # reported NOT DEPLOYED three times for runs that had each deployed their own commit correctly.
        # A newer commit not being live is the NEXT run's business, not this one's.
        [string]$head = $sha
        Write-Host ''
        Write-Host "== confirming the box is actually serving $($head.Substring(0,8)) =="

        # Probe the LAN origin first, the public tunnel second. The tunnel can answer 530 while the box is
        # healthy and already serving the new commit; a probe of the tunnel alone reported such a landed
        # deploy as NOT DEPLOYED. The LAN host comes from NOMNOMZ_DEPLOY_SSH (user@host) when set.
        [string]$lanHost = if ($env:NOMNOMZ_DEPLOY_SSH) { ($env:NOMNOMZ_DEPLOY_SSH -split '@')[-1] } else { '192.168.2.60' }
        [string[]]$origins = @("http://${lanHost}:5080", 'https://dev.nomnomz.bot')
        [string]$live = ''
        [bool]$reachable = $false
        for ([int]$i = 0; $i -lt 30; $i++) {
            foreach ($origin in $origins) {
                try {
                    $live = [string](Invoke-RestMethod -Uri "$origin/health/version" -TimeoutSec 10).version
                    $reachable = $true
                    break
                }
                catch { $live = '' }
            }
            if ($live -like "*$head*") { break }
            Start-Sleep -Seconds 10
        }
        if ($live -like "*$head*") {
            Write-Host "DEPLOYED: $live"
            exit 0
        }

        Write-Host ''
        if (-not $reachable) {
            Write-Host "UNREACHABLE. Neither $($origins -join ' nor ') answered /health/version - cannot say what the box runs."
            Write-Host 'This is NOT proof of a failed deploy. Check the box directly (scripts/proxmox-triage.ps1).'
            exit 1
        }
        Write-Host "NOT DEPLOYED. box reports '$live', HEAD is $head."
        Write-Host 'A cancelled or skipped run ships nothing - re-run the workflow for THIS commit.'
        exit 1
    }

    Write-Host ''
    Write-Host '== STILL RED after re-run - diagnose, do not push more on top =='
    gh run view $runId --log-failed | Select-String -Pattern 'error|FAIL|Failed:' | Select-Object -First 20 | Out-Host
    exit 1
}
finally { Pop-Location }
