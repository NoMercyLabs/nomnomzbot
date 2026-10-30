# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot. You may use, change and share it for free,
#  for any noncommercial purpose, under the PolyForm Noncommercial License
#  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
#  See LICENSE for details, and TRADEMARKS.md for the name and logo.
#
#  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# -----------------------------------------------------------------------------
#
# land-worktree.ps1 - merge a finished worktree-agent branch into the current branch,
# sanity-check the files that break silently, and push+watch.
#
# Replaces the "git fetch -> check ff vs merge -> git merge --no-edit -> eyeball the diff ->
# push-and-watch" sequence that was being re-derived by hand for every dispatched agent this
# session (some 8+ times) - the step that got skipped was always the sanity check, since
# server/openapi/v1.json and strings.xml both fail SILENTLY (valid-looking diff, broken file)
# when two agents patch them independently and a merge produces technically-valid-JSON garbage.
#
# After a clean merge it removes the branch's worktree (through remove-worktree.ps1, which unlinks
# junctions first) and deletes the branch: landed work lives on master. A worktree with uncommitted
# files is kept and named. 2026-10-13: 41 landed worktrees had piled up because this step was missing.
#
#   scripts/land-worktree.ps1 -Branch worktree-agent-abc123
#   scripts/land-worktree.ps1 -Branch worktree-agent-abc123 -NoPush   # merge + sanity-check only

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Branch,
    [switch] $NoPush
)

$ErrorActionPreference = 'Stop'

function Remove-LandedWorktree([string] $Branch) {
    $worktree = $null
    $lock = $null
    $path = $null
    $pathLock = $null
    foreach ($line in (git worktree list --porcelain)) {
        if ($line -like 'worktree *') { $path = $line.Substring(9); $pathLock = $null }
        elseif ($line -like 'locked*') { $pathLock = $line.Substring(6).Trim(); if ($worktree -eq $path) { $lock = $pathLock } }
        elseif ($line -eq "branch refs/heads/$Branch") { $worktree = $path; $lock = $pathLock }
    }
    if ($worktree) {
        if (git -C $worktree status --porcelain) {
            Write-Host "kept worktree $worktree - it has uncommitted files" -ForegroundColor Yellow
            return
        }
        # The harness locks each agent worktree ("claude agent <id> (pid N)") and keeps the lock after the
        # agent ends; landing its branch means the agent is done (2026-10-30: the lock made removal exit 128).
        if ($null -ne $lock) {
            if ($lock -notlike 'claude agent *') {
                Write-Host "kept worktree $worktree - locked: $lock" -ForegroundColor Yellow
                return
            }
            git worktree unlock $worktree
            if ($LASTEXITCODE -ne 0) { throw "could not unlock $worktree" }
        }
        & (Join-Path $PSScriptRoot 'remove-worktree.ps1') $worktree | Out-Null
        Write-Host "removed worktree $worktree" -ForegroundColor DarkGray
    }
    git branch -d $Branch | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "branch $Branch is not fully merged - not deleted" }
    Write-Host "deleted branch $Branch" -ForegroundColor DarkGray
}

$repo =(Join-Path $PSScriptRoot '..' | Resolve-Path).Path
Push-Location $repo
try {
    git fetch origin --quiet
    if ($LASTEXITCODE -ne 0) { throw 'git fetch failed' }

    $headBefore = git rev-parse HEAD
    Write-Host "HEAD before: $headBefore" -ForegroundColor DarkGray

    git merge-base --is-ancestor HEAD $Branch
    $isFastForward = ($LASTEXITCODE -eq 0)

    if ($isFastForward) {
        Write-Host "fast-forwarding onto $Branch" -ForegroundColor Cyan
        git merge --ff-only $Branch
    }
    else {
        Write-Host "diverged - real merge with $Branch" -ForegroundColor Cyan
        git merge --no-edit $Branch
    }
    if ($LASTEXITCODE -ne 0) { throw "merge failed - resolve conflicts by hand, this script does not" }

    # --- sanity-check the files that fail silently when two agents patch them independently ---
    $changed = git diff --name-only $headBefore HEAD
    $failures = [System.Collections.Generic.List[string]]::new()

    foreach ($jsonFile in ($changed | Where-Object { $_ -eq 'server/openapi/v1.json' })) {
        try { Get-Content -Raw -LiteralPath $jsonFile | ConvertFrom-Json | Out-Null }
        catch { $failures.Add("$jsonFile is not valid JSON after merge: $_") }
    }
    foreach ($xmlFile in ($changed | Where-Object { $_ -like '*strings*.xml' })) {
        try { [xml](Get-Content -Raw -LiteralPath $xmlFile) | Out-Null }
        catch { $failures.Add("$xmlFile is not valid XML after merge: $_") }
    }
    # A builder's "csharpier passes" is a claim; CI fails master on it, so the merge checks it.
    $csFiles = @($changed | Where-Object { $_ -like 'server/*.cs' -and (Test-Path -LiteralPath $_) } |
        ForEach-Object { $_.Substring('server/'.Length) })
    if ($csFiles.Count -gt 0) {
        # csharpier reports on stderr; under 'Stop' that throws before the exit code is read.
        $ErrorActionPreference = 'Continue'
        Push-Location server
        try {
            $csOut = dotnet csharpier check @csFiles 2>&1 | Out-String
            $csExit = $LASTEXITCODE
        }
        finally { Pop-Location; $ErrorActionPreference = 'Stop' }
        if ($csExit -ne 0) { $failures.Add("csharpier check failed:`n$csOut") }
    }

    if ($failures.Count -gt 0) {
        Write-Host "SANITY CHECK FAILED - do not push:" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw 'post-merge sanity check failed'
    }
    Write-Host "sanity check OK ($($changed.Count) files changed)" -ForegroundColor Green

    Remove-LandedWorktree -Branch $Branch

    if ($NoPush) {
        Write-Host 'merged, -NoPush set - stopping before push.' -ForegroundColor Yellow
        return
    }

    & (Join-Path $PSScriptRoot 'push-and-watch.ps1')
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
