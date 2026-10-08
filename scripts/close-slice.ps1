# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------

# Close a shipped slice: delete its bullet from the execution plan and commit that deletion.
# The tracker holds REMAINING work only (CLAUDE.md, Workflow - vertical slices, committed when
# validated; and the header of .claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md), so a shipped
# slice is deleted, never annotated as done. Optionally append replacement bullets for follow-up slices the work exposed.
#
#   scripts/close-slice.ps1 -Slice S006 -Message "live-game money refunds on settle failure"
#   scripts/close-slice.ps1 -Slice S006 -Message "..." -Follow @(
#       "- **S006b** Something the slice uncovered.",
#       "  Done-when: ..." )
#
# Only the plan file is committed, by explicit path - other agents share this tree.

param(
    [Parameter(Mandatory = $true)][string]$Slice,
    [Parameter(Mandatory = $true)][string]$Message,
    [string[]]$Follow = @()
)

$ErrorActionPreference = 'Stop'
# the commit subject is built below as "docs(plan): close <Slice> - <Message>"; a Message that repeats
# that prefix doubled it twice on 2026-10-03
if ($Message -match '^\s*(docs\(plan\):\s*)?close\s') {
    throw "-Message is only the description; the script adds 'docs(plan): close $Slice - ' itself"
}
$repo =(Join-Path $PSScriptRoot '..' | Resolve-Path).Path
$plan = Join-Path $repo '.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md'
if (-not (Test-Path $plan)) { throw "plan not found: $plan" }

[string[]]$lines = Get-Content -LiteralPath $plan
[System.Collections.Generic.List[string]]$kept = [System.Collections.Generic.List[string]]::new()
[bool]$found = $false
[int]$i = 0
# a slice bullet is "- **ID**" or "- [ ] **ID**": both forms are in the plan, and a terminator that knew
# only the first ran a closed slice on through every checkbox sibling after it
[string]$bulletStart = '^- (\[[ xX]\] )?\*\*'
[string]$headingStart = '^#{1,6} '
[string]$sliceStart = $bulletStart + [regex]::Escape($Slice) + '\*\*'

while ($i -lt $lines.Length) {
    [string]$line = $lines[$i]
    # exact-id match, first hit only: a wildcard here also deleted siblings (S006 took S006b with it)
    if (-not $found -and $line -cmatch $sliceStart) {
        $found = $true
        $i++
        # a bullet runs until the next bullet, the next heading of any level, a marker comment, or a rule
        # (the last slice before <!-- parity:end --> once took the marker with it)
        while ($i -lt $lines.Length -and
               $lines[$i] -cnotmatch $bulletStart -and
               $lines[$i] -cnotmatch $headingStart -and
               -not $lines[$i].StartsWith('<!--') -and
               $lines[$i].Trim() -ne '---') { $i++ }
        foreach ($f in $Follow) { $kept.Add($f) }
        # a bullet block absorbs the blank line that separated it from a following heading;
        # put it back so sections keep their spacing as slices are deleted over time
        if ($i -lt $lines.Length -and $lines[$i] -cmatch $headingStart -and
            $kept.Count -gt 0 -and $kept[$kept.Count - 1].Trim() -ne '') {
            $kept.Add('')
        }
        continue
    }
    $kept.Add($line)
    $i++
}

if (-not $found) { throw "slice $Slice not found in the plan - already closed, or a typo" }
[int]$markersBefore = @($lines | Where-Object { $_.StartsWith('<!--') }).Count
[int]$markersAfter = @($kept | Where-Object { $_.StartsWith('<!--') }).Count
if ($markersAfter -ne $markersBefore) { throw "closing $Slice would drop a marker comment from the plan" }

Set-Content -LiteralPath $plan -Value $kept
git -C $repo commit --only -m "docs(plan): close $Slice - $Message" -- $plan
if ($LASTEXITCODE -ne 0) { throw 'commit failed' }
Write-Host "closed $Slice$(if ($Follow.Count) { " (+$($Follow.Count) follow-up lines)" })"
