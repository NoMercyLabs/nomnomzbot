# Close a shipped slice: delete its bullet from the execution plan and commit that deletion.
# The tracker holds REMAINING work only (see CLAUDE.md), so a shipped slice is deleted, never
# annotated as done. Optionally append replacement bullets for follow-up slices the work exposed.
#
#   scripts/close-slice.ps1 -Slice S006 -Message "live-game money refunds on settle failure"
#   scripts/close-slice.ps1 -Slice S006 -Message "..." -Follow @(
#       "- **S006b** Something the slice uncovered.",
#       "  Done-when: ..." )
#
# Only the plan file is committed, by explicit path — other agents share this tree.

param(
    [Parameter(Mandatory = $true)][string]$Slice,
    [Parameter(Mandatory = $true)][string]$Message,
    [string[]]$Follow = @()
)

$ErrorActionPreference = 'Stop'
$repo = (Join-Path $PSScriptRoot '..' | Resolve-Path).Path
$plan = Join-Path $repo '.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md'
if (-not (Test-Path $plan)) { throw "plan not found: $plan" }

[string[]]$lines = Get-Content -LiteralPath $plan
[System.Collections.Generic.List[string]]$kept = [System.Collections.Generic.List[string]]::new()
[bool]$found = $false
[int]$i = 0

while ($i -lt $lines.Length) {
    [string]$line = $lines[$i]
    if ($line -like "- **$Slice***") {
        $found = $true
        $i++
        # a bullet runs until the next bullet, the next heading, or a rule
        while ($i -lt $lines.Length -and
               -not $lines[$i].StartsWith('- **') -and
               -not $lines[$i].StartsWith('## ') -and
               $lines[$i].Trim() -ne '---') { $i++ }
        foreach ($f in $Follow) { $kept.Add($f) }
        continue
    }
    $kept.Add($line)
    $i++
}

if (-not $found) { throw "slice $Slice not found in the plan - already closed, or a typo" }

Set-Content -LiteralPath $plan -Value $kept
git -C $repo commit --only -m "docs(plan): close $Slice - $Message" -- $plan
if ($LASTEXITCODE -ne 0) { throw 'commit failed' }
Write-Host "closed $Slice$(if ($Follow.Count) { " (+$($Follow.Count) follow-up lines)" })"
