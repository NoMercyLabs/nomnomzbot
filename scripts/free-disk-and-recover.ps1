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
# free-disk-and-recover.ps1 — the disk-full / Postgres-crash-loop recovery, made deterministic.
#
# Incident shape this fixes (seen more than once): the Proxmox host's root disk fills up, Postgres
# panics mid-checkpoint ("could not write to file pg_logical/replorigin_checkpoint.tmp: No space
# left on device") and crash-loops in a sub-second restart cycle, which blocks every deploy — not
# just the one whose image happened to fill the disk. Root cause of the disk usage is almost always
# accumulated dangling/untagged Docker image layers from repeated CI builds (the tagged, in-use
# image is never touched by this script).
#
# Replaces the ad-hoc "ssh df -> ssh docker -> ssh docker" sequence that was being re-derived by
# hand during every incident. Mutates exactly one thing — `docker image prune -f`, which only ever
# removes images with zero referencing containers — then verifies Postgres actually came back and
# the API is serving traffic again. If pruning doesn't recover enough space, it says so and stops;
# it does not escalate to anything more destructive (volumes, build cache, tagged images) on its own.
#
#   .\scripts\free-disk-and-recover.ps1              # triage + prune + verify recovery
#   .\scripts\free-disk-and-recover.ps1 -WhatIf       # report disk state only, prune nothing

[CmdletBinding()]
param(
    [string] $ServerHost = '192.168.2.60',
    [string] $SshKey     = "$env:USERPROFILE\.ssh\docker_proxmox",
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
$sshTarget = "root@$ServerHost"

function Invoke-Remote {
    param([Parameter(Mandatory)][string] $Command)
    # Base64 the payload so neither PowerShell's nor the remote shell's quoting can mangle bash
    # containing quotes/backslashes (see proxmox-triage.ps1 for the same pattern and rationale).
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Command -replace "`r`n", "`n")))
    $output = & ssh -o BatchMode=yes -o ConnectTimeout=10 -i $SshKey $sshTarget "echo $encoded | base64 -d | bash" 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Warning "remote command exited $LASTEXITCODE" }
    return $output
}

function Write-Section {
    param([Parameter(Mandatory)][string] $Title)
    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

Write-Section 'Disk usage before'
Invoke-Remote 'df -h / | tail -1; echo; docker system df'

if ($WhatIf) {
    Write-Host ''
    Write-Host '-WhatIf: stopping before any mutation.' -ForegroundColor Yellow
    exit 0
}

Write-Section 'Pruning dangling images (zero-container-referenced only)'
Invoke-Remote 'docker image prune -f'

Write-Section 'Disk usage after'
Invoke-Remote 'df -h / | tail -1'

Write-Section 'Postgres recovery'
$pgWaited = 0
$pgHealthy = $false
while ($pgWaited -lt 60) {
    $status = (Invoke-Remote "docker inspect --format='{{.State.Health.Status}}' nomnomzbot-postgres 2>/dev/null") -join ''
    if ($status -eq 'healthy') { $pgHealthy = $true; break }
    Start-Sleep -Seconds 5
    $pgWaited += 5
}
if ($pgHealthy) {
    Write-Host "Postgres healthy after ${pgWaited}s." -ForegroundColor Green
}
else {
    Write-Host "Postgres NOT healthy after 60s — pruning didn't free enough space, or a different fault. Escalate manually (proxmox-triage.ps1), do not prune further without checking what's actually using the disk." -ForegroundColor Red
    exit 1
}

Write-Section 'API health'
Invoke-Remote "curl -s http://localhost:5080/health/version; echo; curl -sw '\nHTTP:%{http_code}\n' http://localhost:5080/health/ready"

Write-Host ''
Write-Host 'Recovery complete.' -ForegroundColor Green
