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
# Tests for scripts/remove-worktree.ps1, in a throwaway repo under TEMP.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/remove-worktree.test.ps1

$ErrorActionPreference = 'Stop'
[string]$remover = Join-Path $PSScriptRoot 'remove-worktree.ps1'
[string]$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ("rwt-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
[string]$repo = Join-Path $sandbox 'repo'
[string]$precious = Join-Path $sandbox 'precious'
[int]$failed = 0

function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Output "PASS $name" } else { Write-Output "FAIL $name"; $script:failed++ }
}

function Invoke-Remover([string[]]$arguments) {
    # A refusal writes to stderr; under Stop, Windows PowerShell 5.1 would throw on it instead of returning the code.
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = 0
    & powershell -NoProfile -ExecutionPolicy Bypass -File $remover @arguments 2>&1 | Out-Null
    return $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path $repo, $precious | Out-Null
Set-Content -LiteralPath (Join-Path $precious 'keep.txt') -Value 'keep'
& git -C $repo init -q
& git -C $repo -c user.name=t -c user.email=t@t commit -q --allow-empty -m init

# A worktree that holds a junction to a folder outside it: the junction target must survive.
[string]$worktree = Join-Path $repo '.scratch\nnb-slice-test'
& git -C $repo worktree add -q -f --detach $worktree HEAD
New-Item -ItemType Junction -Path (Join-Path $worktree 'node_modules') -Target $precious | Out-Null
[int]$code = Invoke-Remover @('-Repo', $repo, $worktree)
Check 'removes a worktree that holds a junction' ($code -eq 0 -and -not (Test-Path -LiteralPath $worktree))
Check 'leaves the junction target intact' (Test-Path -LiteralPath (Join-Path $precious 'keep.txt'))
Check 'drops the git registration' (-not ((& git -C $repo worktree list --porcelain) -match 'nnb-slice-test'))

# A leftover folder with no .git (git already dropped it) under .claude\worktrees is deleted.
[string]$leftover = Join-Path $repo '.claude\worktrees\agent-leftover'
New-Item -ItemType Directory -Force -Path (Join-Path $leftover 'deep') | Out-Null
$code = Invoke-Remover @('-Repo', $repo, $leftover)
Check 'deletes a leftover folder without .git' ($code -eq 0 -and -not (Test-Path -LiteralPath $leftover))

# Anything outside .scratch\ and .claude\worktrees\ is refused and left in place.
[string]$outside = Join-Path $repo 'src'
New-Item -ItemType Directory -Force -Path $outside | Out-Null
$code = Invoke-Remover @('-Repo', $repo, $outside)
Check 'refuses a folder outside the worktree roots' ($code -ne 0 -and (Test-Path -LiteralPath $outside))
$code = Invoke-Remover @('-Repo', $repo, (Join-Path $repo '.scratch'))
Check 'refuses the .scratch root itself' ($code -ne 0 -and (Test-Path -LiteralPath (Join-Path $repo '.scratch')))

# Git writes its object files read-only; clear the flag so the sandbox can go. No reparse point is left in it.
Get-ChildItem -LiteralPath $sandbox -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
[System.IO.Directory]::Delete($sandbox, $true)
Write-Output "remove-worktree.test: $failed failed"
exit $(if ($failed -eq 0) { 0 } else { 1 })
