# Push the current branch to origin/master and BLOCK until CI reaches a verdict.
#
# The CI Gate says a push is not done until CI is green, and the watch is part of the push - never
# "CI will probably pass". This exists because the push -> find run id -> watch -> diagnose -> re-run
# loop was being re-derived by hand every time, which is exactly where a step gets skipped.
#
#   scripts/push-and-watch.ps1                  # push HEAD:master, watch, auto-retry one flake
#   scripts/push-and-watch.ps1 -NoRetry         # never re-run; a red is a red
#   scripts/push-and-watch.ps1 -DryRun          # watch the latest run without pushing
#
# Exit code is the verdict: 0 green, 1 red. On red it prints the failing jobs and the first error
# lines, so the next step is diagnosis rather than another round of gh incantations.
#
# One flake re-run is allowed by default because this repo has two known non-reproducing reds (the
# Application suite ~5%, and SQLite concurrent-writer contention under a loaded runner). A re-run is
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
    if (-not $DryRun) {
        Write-Host '== pushing HEAD to origin/master =='
        Invoke-Native 'git push failed - rebase onto origin/master and retry' { git push origin HEAD:master }
    }

    # The run for the just-pushed commit does not exist instantly; poll briefly for it by SHA rather
    # than grabbing "the latest run", which can be someone else's push.
    [string]$sha = (git rev-parse HEAD).Trim()
    # Filter in PowerShell rather than with `gh -q`: a jq expression containing the SHA has to survive
    # PowerShell quoting AND jq quoting, and it silently matched nothing when it did not.
    [string]$runId = $null
    foreach ($attempt in 1..12) {
        [string]$json = gh run list --limit 20 --json databaseId,headSha  # no space: PowerShell would parse `a, b` as an array and pass two args
        if ($json) {
            $match = ($json | ConvertFrom-Json) | Where-Object { $_.headSha -eq $sha } | Select-Object -First 1
            if ($match) { $runId = [string]$match.databaseId; break }
        }
        Start-Sleep -Seconds 5
    }
    if (-not $runId) { throw "no CI run appeared for $sha after 60s - check the workflow triggers" }

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
        exit 0
    }

    Write-Host ''
    Write-Host '== STILL RED after re-run - diagnose, do not push more on top =='
    gh run view $runId --log-failed | Select-String -Pattern 'error|FAIL|Failed:' | Select-Object -First 20 | Out-Host
    exit 1
}
finally { Pop-Location }
