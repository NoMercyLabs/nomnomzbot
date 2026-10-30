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

# Gates and commits a set of dashboard files while other builders still have half-written files in
# the main tree. The gate runs on a clean checkout of HEAD plus only the files being committed, so
# the commit is proven to build and pass on its own.
#
#   scripts/dashboard-commit.ps1 -Paths dashboard/src/api/home.ts,dashboard/src/views/dashboard/home -MessageFile msg.txt
#   -DryRun runs the gate and skips the commit.
#
# The gate worktree is kept between runs and owns its own node_modules (npm ci, again when the lock
# file changes). Remove it only with scripts/remove-worktree.ps1.

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

# The linter fixes what it can in the slice's own files first, so a fixable rule never fails the gate
# after minutes of type-check (2026-10-14: the same fixable annotation rule failed two gates in a row).
$lintable = @($Paths | Where-Object { ($_ -match '^dashboard/.+\.(ts|vue)$' -or (Test-Path "$repo/$_" -PathType Container)) -and $_ -match '^dashboard/' -and (Test-Path "$repo/$_") } | ForEach-Object { $_ -replace '^dashboard/', '' })
if ($lintable.Count -gt 0) {
    Push-Location "$repo/dashboard"
    npx eslint --fix @lintable | Out-Null
    Pop-Location
}

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
    # Untracked files from the last run only; node_modules is ignored, so its install is left alone.
    git -C $wt clean -fd -- dashboard/src | Out-Null
}
# The gate worktree owns its packages, the same install CI runs. A link to the main tree's node_modules is
# refused by restore-guard (2026-10-27: a shared link let another worktree damage a running dev server).
# Installed once, and again only when the lock file changes.
$lockHash = (Get-FileHash "$wt/dashboard/package-lock.json").Hash
$stamp = "$wt/dashboard/node_modules/.gate-lock-hash"
if (-not (Test-Path $stamp) -or (Get-Content $stamp) -ne $lockHash) {
    Push-Location "$wt/dashboard"
    npm ci --no-audit --no-fund | Out-Null
    $installed = $LASTEXITCODE -eq 0
    Pop-Location
    if (-not $installed) { 'NPM CI FAILED in the gate worktree'; exit 1 }
    Set-Content -Path $stamp -Value $lockHash
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
