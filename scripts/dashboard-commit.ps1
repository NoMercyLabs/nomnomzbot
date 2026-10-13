# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------

# Gates and commits a set of dashboard files while other builders still have half-written files in
# the main tree. The gate runs on a clean checkout of HEAD plus only the files being committed, so
# the commit is proven to build and pass on its own.
#
#   scripts/dashboard-commit.ps1 -Paths dashboard/src/api/home.ts,dashboard/src/views/dashboard/home -MessageFile msg.txt
#   -DryRun runs the gate and skips the commit.
#
# The gate worktree is kept between runs. Its node_modules is a junction to the main tree's, so it
# is only ever removed with .claude/scripts/remove-worktree.ps1, which unlinks the junction first.

param(
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [string]$MessageFile,
    [switch]$DryRun
)

# Native tools write warnings to stderr; 'Stop' would turn those into errors. Exit codes decide.
$ErrorActionPreference = 'Continue'
# 'powershell -File' from bash passes the list as one comma-joined string.
$Paths = @($Paths | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($path in $Paths) {
    # A staged rename drops the old path from the index; HEAD still tracks it, so it is a deletion to commit.
    if (-not (Test-Path "$(git rev-parse --show-toplevel)/$path") -and -not (git ls-files -- $path) -and -not (git ls-tree --name-only HEAD -- $path)) { throw "Unknown path: $path" }
}
$repo = (git rev-parse --show-toplevel).Trim()
$wt = "$repo/.claude/worktrees/dashboard-gate"
$id = @('-c', 'user.name=Stoney_Eagle', '-c', 'user.email=45034970+StoneyEagle@users.noreply.github.com')

if (-not $DryRun -and -not $MessageFile) { throw 'MessageFile is required unless -DryRun.' }

# One gate at a time: a second run force-checks-out and cleans the shared worktree under the first
# (2026-10-13: a parallel run deleted a builder's new file mid-lint). Held through the commit so HEAD
# cannot move between the gate and the commit. Released when this process exits, even on a crash.
$gateLock = [System.Threading.Mutex]::new($false, 'Global\nnz-dashboard-gate')
try { $held = $gateLock.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $held = $true }
if (-not $held) {
    'Another dashboard gate is running; waiting for it (at most 30 minutes).'
    try { $held = $gateLock.WaitOne([TimeSpan]::FromMinutes(30)) } catch [System.Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { 'GATE LOCK TIMEOUT'; exit 1 }
}
$head = (git -C $repo rev-parse HEAD).Trim()

if (-not (Test-Path $wt)) {
    git -C $repo worktree add --detach $wt $head | Out-Null
} else {
    git -C $wt checkout --force --detach $head | Out-Null
    # Untracked files from the last run only; node_modules is ignored, so its junction is left alone.
    git -C $wt clean -fd -- dashboard/src | Out-Null
}
if (-not (Test-Path "$wt/dashboard/node_modules")) {
    New-Item -ItemType Junction -Path "$wt/dashboard/node_modules" -Target "$repo/dashboard/node_modules" | Out-Null
}

foreach ($path in $Paths) {
    $source = "$repo/$path"
    $target = "$wt/$path"
    if (Test-Path $source -PathType Container) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item -Recurse -Force "$source/*" $target
    } elseif (Test-Path $source) {
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -Force $source $target
    } elseif (Test-Path $target) {
        Remove-Item -Force $target
    }
}

Push-Location "$wt/dashboard"
try {
    $failed = $null
    '== type-check =='
    npx vue-tsc --build
    if ($LASTEXITCODE -ne 0) { $failed = 'TYPE CHECK' }
    if (-not $failed) {
        '== lint =='
        npx oxlint .
        if ($LASTEXITCODE -ne 0) { $failed = 'OXLINT' }
    }
    if (-not $failed) {
        npx eslint .
        if ($LASTEXITCODE -ne 0) { $failed = 'ESLINT' }
    }
    if (-not $failed) {
        # American English only (Stoney 2026-10-13); cspell.config.yaml lists the British words.
        npx cspell lint --no-progress --report flagged
        if ($LASTEXITCODE -ne 0) { $failed = 'SPELLING' }
    }
    if (-not $failed) {
        '== tests =='
        $tests = npx vitest run --testTimeout 30000 2>&1
        if ($LASTEXITCODE -ne 0) { $failed = 'TESTS' }
        # The tail alone cuts off which tests failed; their FAIL lines are printed first.
        $tests | Select-String -Pattern '^\s*FAIL\s' | Sort-Object -Unique
        $tests | Select-Object -Last 12
    }
} finally {
    Pop-Location
}
if ($failed) { "$failed FAILED"; exit 1 }

# The commit takes the main-tree files, so they must still be the ones gated: an edit made while the gate
# ran would land unproven (2026-10-13: b049ce26e took a half-done edit that did not type-check).
$changed = foreach ($path in $Paths) {
    $files = if (Test-Path "$repo/$path" -PathType Container) {
        Get-ChildItem -Recurse -File "$repo/$path" | ForEach-Object { $_.FullName.Substring($repo.Length + 1) }
    } else { @($path) }
    foreach ($file in $files) {
        $now = if (Test-Path "$repo/$file") { (Get-FileHash "$repo/$file").Hash } else { '' }
        $gated = if (Test-Path "$wt/$file") { (Get-FileHash "$wt/$file").Hash } else { '' }
        if ($now -ne $gated) { $file }
    }
}
if ($changed) { 'CHANGED DURING GATE (run it again):'; $changed; exit 1 }
if ($DryRun) { 'GATE OK (dry run, nothing committed)'; exit 0 }

'== commit =='
# One unknown pathspec makes `git add` add nothing; a deleted path is staged through `git rm --cached` instead.
$present = @($Paths | Where-Object { Test-Path "$repo/$_" })
$gone = @($Paths | Where-Object { -not (Test-Path "$repo/$_") -and (git -C $repo ls-files -- $_) })
if ($present) { git -C $repo add -- $present }
if ($gone) { git -C $repo rm --cached --quiet -- $gone }
git -C $repo @id commit --only -F $MessageFile -- $Paths
if ($LASTEXITCODE -ne 0) { 'COMMIT FAILED'; exit 1 }
git -C $repo log -1 --format='%h %s'
'ALL OK'
