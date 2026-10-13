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
    if (-not (Test-Path "$(git rev-parse --show-toplevel)/$path") -and -not (git ls-files -- $path)) { throw "Unknown path: $path" }
}
$repo = (git rev-parse --show-toplevel).Trim()
$wt = "$repo/.claude/worktrees/dashboard-gate"
$head = (git -C $repo rev-parse HEAD).Trim()
$id = @('-c', 'user.name=Stoney_Eagle', '-c', 'user.email=45034970+StoneyEagle@users.noreply.github.com')

if (-not $DryRun -and -not $MessageFile) { throw 'MessageFile is required unless -DryRun.' }

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
        '== tests =='
        npx vitest run --testTimeout 30000 2>&1 | Select-Object -Last 12
        if ($LASTEXITCODE -ne 0) { $failed = 'TESTS' }
    }
} finally {
    Pop-Location
}
if ($failed) { "$failed FAILED"; exit 1 }
if ($DryRun) { 'GATE OK (dry run, nothing committed)'; exit 0 }

'== commit =='
git -C $repo add -- $Paths
git -C $repo @id commit --only -F $MessageFile -- $Paths
if ($LASTEXITCODE -ne 0) { 'COMMIT FAILED'; exit 1 }
git -C $repo log -1 --format='%h %s'
'ALL OK'
