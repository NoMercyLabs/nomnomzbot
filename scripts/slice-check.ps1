# Slice gate: build, run the slice's own tests, and format-check only the slice's own files.
# Used by every builder agent instead of re-deriving build -> test -> commit by hand.
#
# Usage:
#   scripts/slice-check.ps1 -TestProject tests/NomNomzBot.Api.Tests -Filter "FullyQualifiedName~SecurityHeaders" -Paths a.cs,b.cs
#
# The repo-wide drift is gone (S115, commit 2282847c: `csharpier check .` is clean over 2609 files),
# so `CLAUDE.md`'s per-commit format gate is enforceable again. Path scoping is kept because it is
# fast and keeps a slice from reformatting files it does not own while other agents share the tree.

param(
    [Parameter(Mandatory = $true)][string]$TestProject,
    [Parameter(Mandatory = $true)][string]$Filter,
    [Parameter(Mandatory = $true)][string[]]$Paths,
    # Verify a committed sha in a throwaway worktree instead of the shared tree. Use this whenever
    # another agent's uncommitted work breaks the build on a file you do not own. Never `git stash`.
    [string]$AtCommit
)

$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot '..' | Resolve-Path
$worktree = $null

if ($AtCommit) {
    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) ("nnb-slice-" + $AtCommit.Substring(0, 8))
    git -C $repo worktree add -f $worktree $AtCommit
    if ($LASTEXITCODE -ne 0) { throw "could not create worktree at $AtCommit" }
    $server = Join-Path $worktree 'server'
}
else {
    $server = Join-Path $repo 'server'
}

Push-Location $server
try {
    Write-Host '== build =='
    dotnet build NomNomzBot.slnx -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    Write-Host '== test (slice filter) =='
    dotnet test $TestProject -c Debug --no-build --filter $Filter
    if ($LASTEXITCODE -ne 0) { throw 'slice tests failed' }

    Write-Host '== format (slice files only) =='
    # -Paths are given repo-relative. In -AtCommit mode the build runs inside the worktree, so they must
    # be rebased onto the worktree root; formatting the shared tree's copies from here would be wrong.
    [string[]]$resolvedPaths = @()
    foreach ($path in $Paths) {
        if ([System.IO.Path]::IsPathRooted($path)) {
            $resolvedPaths += $path
            continue
        }
        [string]$root = if ($worktree) { $worktree } else { $repo }
        [string]$candidate = Join-Path $root $path
        if (-not (Test-Path $candidate)) { throw "slice path not found under ${root}: $path" }
        $resolvedPaths += (Resolve-Path $candidate).Path
    }

    dotnet csharpier format @resolvedPaths
    dotnet csharpier check @resolvedPaths
    if ($LASTEXITCODE -ne 0) { throw 'csharpier check failed on slice files' }

    Write-Host '== cleanup (unused usings, slice files only) =='
    # IDE0005 only (S-cleanup, server/.editorconfig): csharpier is formatting-only and never touches
    # this. Scoped to the slice's own paths for the same reason as the csharpier calls above — don't
    # let one slice fix files another in-flight session owns. Migrations are excluded in .editorconfig
    # itself ([**/Migrations/*.cs] -> IDE0005 = none), not here, so this include list stays simple.
    [string[]]$relativePaths = $resolvedPaths | ForEach-Object { [System.IO.Path]::GetRelativePath($server, $_) }
    dotnet format style NomNomzBot.slnx --include @relativePaths --severity warn --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet format style failed on slice files' }

    Write-Host 'SLICE CHECK OK - safe to commit with: git commit --only -m "..." -- <paths>'
}
finally {
    Pop-Location
    if ($worktree) { git -C $repo worktree remove --force $worktree }
}
