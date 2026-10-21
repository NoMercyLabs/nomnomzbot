# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------

# Verify the WHOLE tree the way an orchestrator must before accepting an agent's work.
#
# scripts/slice-check.ps1 is the pre-commit gate for ONE slice (scoped tests + scoped format + jb
# inspection). This is the other half: the full-tree, all-suites check (Domain, Application,
# Infrastructure, Api and E2E - the same test projects CI runs) that answers "is HEAD actually
# green?" - the question that caught, in one session, an ungated endpoint, a save-blocking registry bug,
# five unscoped domain events and a null content-type, every one of which sat behind an agent's report
# of "all green" from a FILTERED run.
#
#   scripts/verify-tree.ps1                      # build + all 5 server suites (incl. E2E) + csharpier
#   scripts/verify-tree.ps1 -AtCommit <sha>      # verify a commit in a throwaway worktree instead
#
# Three traps it encodes, each of which produced a WRONG green today:
#   1. A stray testhost/API process holds the build DLLs, so `dotnet build` "fails" with file-lock
#      errors that look like compile errors. Killed first.
#   2. `dotnet test --no-build` against a STALE test assembly silently runs a subset - a run once
#      reported 1243 tests where the truth was 4279. The test projects are rebuilt explicitly.
#
# The dashboard (dashboard/) has its own gate: npm run type-check, npm run lint, npx vitest run and
# npm run build-only, the same commands as the CI dashboard job. This script covers the server only.

param(
    [string]$AtCommit
)

$ErrorActionPreference = 'Stop'
$repo = (Join-Path $PSScriptRoot '..' | Resolve-Path).Path
$worktree = $null

if ($AtCommit) {
    $worktree = Join-Path $repo ".scratch/verify-tree-$AtCommit"
    if (Test-Path $worktree) { & (Join-Path $PSScriptRoot 'remove-worktree.ps1') -Repo $repo $worktree | Out-Null }
    git -C $repo worktree add -f --detach $worktree $AtCommit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not create a worktree at $AtCommit" }
    $root = $worktree
    Write-Host "== verifying $AtCommit in a throwaway worktree =="
}
else { $root = $repo }

$server = Join-Path $root 'server'
[bool]$failed = $false

# Trap 1: stray processes hold the DLLs and the build reports file-lock errors that read like real ones.
# The process NAMES differ by host: Windows shows testhost/NomNomzBot.Api, but on Linux (the devbox
# container) both run as plain `dotnet`, so matching only the Windows names silently kills nothing.
# Linux does not lock open files the way Windows does, so there the kill is about a stale API holding
# the port, not the DLLs - matched on its command line instead of its name.
Get-Process -Name 'testhost', 'NomNomzBot.Api' -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not $IsWindows) {
    Get-Process -Name 'dotnet' -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'testhost|NomNomzBot\.Api' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 2

Push-Location $server
try {
    Write-Host '== build (warnings are errors here) =='
    dotnet build -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    # Trap 2: --no-build over a stale assembly silently runs a SUBSET and still prints "Passed!".
    [string[]]$projects = @('Domain', 'Application', 'Infrastructure', 'Api', 'E2E')
    foreach ($project in $projects) {
        [string]$path = "tests/NomNomzBot.$project.Tests"
        dotnet build $path -v quiet | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "could not build $path" }

        Write-Host "== $project =="
        dotnet test $path --no-build | Tee-Object -Variable output | Out-Host
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }

    Write-Host '== csharpier (repo-wide) =='
    dotnet csharpier check . | Out-Host
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
finally { Pop-Location }

if ($worktree) { & (Join-Path $PSScriptRoot 'remove-worktree.ps1') -Repo $repo $worktree | Out-Null }

if ($failed) { Write-Host ''; Write-Host 'TREE IS RED'; exit 1 }
Write-Host ''
Write-Host 'TREE IS GREEN'
exit 0
