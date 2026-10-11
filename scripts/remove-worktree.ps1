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
# Removes one of this repo's worktrees (under .scratch\ or .claude\worktrees\) without deleting the target of a
# junction or symlink inside it. Git on Windows and Windows PowerShell 5.1 Remove-Item -Recurse both walk into a
# junction and delete the target's files (2026-10-06: a node_modules junction target was emptied that way).
# Order: unlink every reparse point (the link only), then git removes the worktree, then a leftover folder is
# deleted only when no reparse point remains.
# Run: scripts/remove-worktree.ps1 <worktree path>        (-Repo <main checkout> defaults to this repo)

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$WorktreePath,
    [string]$Repo = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'

[string]$repoRoot = [System.IO.Path]::GetFullPath($Repo.Replace('/', '\')).TrimEnd('\')
[string]$full = [System.IO.Path]::GetFullPath($WorktreePath.Replace('/', '\')).TrimEnd('\')
[string[]]$roots = @("$repoRoot\.scratch\", "$repoRoot\.claude\worktrees\")
[bool]$allowed = @($roots | Where-Object {
        $full.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase) -and $full.Length -gt $_.Length
    }).Count -gt 0
if (-not $allowed) { throw "remove-worktree: refused: '$full' is not under $($roots -join ' or ')" }
if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "remove-worktree: '$full' is not a folder" }

# The \\?\ form reads paths past 260 characters; .Delete() on a reparse point removes the link, not its target.
[string]$long = '\\?\' + $full
[object[]]$links = @(Get-ChildItem -LiteralPath $long -Recurse -Force -Attributes ReparsePoint)
foreach ($link in $links) {
    (Get-Item -LiteralPath $link.FullName -Force).Delete()
    if (Test-Path -LiteralPath $link.FullName) { throw "remove-worktree: link '$($link.FullName)' still exists" }
}

# A folder without .git is a leftover git already dropped; only the folder delete below applies to it.
if (Test-Path -LiteralPath (Join-Path $full '.git')) {
    & git -c core.longpaths=true -C $repoRoot worktree remove --force $full
    if ($LASTEXITCODE -ne 0) { throw "remove-worktree: git could not remove '$full' (exit $LASTEXITCODE)" }
}
if (Test-Path -LiteralPath $full) {
    [object[]]$left = @(Get-ChildItem -LiteralPath $long -Recurse -Force -Attributes ReparsePoint)
    if ($left.Count -gt 0) { throw "remove-worktree: refused delete: $($left.Count) reparse point(s) still under $full" }
    [System.IO.Directory]::Delete($long, $true)
}
if (Test-Path -LiteralPath $full) { throw "remove-worktree: '$full' still exists" }
Write-Output "remove-worktree: removed $full; $($links.Count) link(s) unlinked, targets intact"
