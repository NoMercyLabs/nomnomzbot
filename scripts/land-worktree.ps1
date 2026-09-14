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
# land-worktree.ps1 — merge a finished worktree-agent branch into the current branch,
# sanity-check the files that break silently, and push+watch.
#
# Replaces the "git fetch -> check ff vs merge -> git merge --no-edit -> eyeball the diff ->
# push-and-watch" sequence that was being re-derived by hand for every dispatched agent this
# session (some 8+ times) — the step that got skipped was always the sanity check, since
# server/openapi/v1.json and strings.xml both fail SILENTLY (valid-looking diff, broken file)
# when two agents patch them independently and a merge produces technically-valid-JSON garbage.
#
# Does NOT delete the worktree or its branch — this project's rule is "never sweep a worktree
# while related work might still be reviewed"; clean those up separately once you're done.
#
#   scripts/land-worktree.ps1 -Branch worktree-agent-abc123
#   scripts/land-worktree.ps1 -Branch worktree-agent-abc123 -NoPush   # merge + sanity-check only

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Branch,
    [switch] $NoPush
)

$ErrorActionPreference = 'Stop'
$repo = (Join-Path $PSScriptRoot '..' | Resolve-Path).Path
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
        Write-Host "diverged — real merge with $Branch" -ForegroundColor Cyan
        git merge --no-edit $Branch
    }
    if ($LASTEXITCODE -ne 0) { throw "merge failed — resolve conflicts by hand, this script does not" }

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

    if ($failures.Count -gt 0) {
        Write-Host "SANITY CHECK FAILED — do not push:" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw 'post-merge sanity check failed'
    }
    Write-Host "sanity check OK ($($changed.Count) files changed)" -ForegroundColor Green

    if ($NoPush) {
        Write-Host 'merged, -NoPush set — stopping before push.' -ForegroundColor Yellow
        return
    }

    & (Join-Path $PSScriptRoot 'push-and-watch.ps1')
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
