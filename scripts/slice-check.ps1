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

# Every native call here is judged by its exit code, never by stderr: docker/dotnet/jb all write
# ordinary progress to stderr, and under Windows PowerShell 5.1 that becomes a terminating
# NativeCommandError while ErrorActionPreference is 'Stop'. Same helper shape as switchover.ps1.
function Invoke-Native {
    param([Parameter(Mandatory = $true)][string]$FailureMessage,
        [Parameter(Mandatory = $true)][scriptblock]$Command)

    [string]$previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

if ($AtCommit) {
    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) ("nnb-slice-" + $AtCommit.Substring(0, 8))
    Invoke-Native "could not create worktree at $AtCommit" { git -C $repo worktree add -f $worktree $AtCommit }
    $server = Join-Path $worktree 'server'
}
else {
    $server = Join-Path $repo 'server'
}

Push-Location $server
try {
    Write-Host '== build =='
    Invoke-Native 'build failed' { dotnet build NomNomzBot.slnx -c Debug }

    Write-Host '== test (slice filter) =='
    Invoke-Native 'slice tests failed' { dotnet test $TestProject -c Debug --no-build --filter $Filter }

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

    Invoke-Native 'csharpier format failed on slice files' { dotnet csharpier format @resolvedPaths }
    Invoke-Native 'csharpier check failed on slice files' { dotnet csharpier check @resolvedPaths }

    # Repo-wide CHECK (never a repo-wide format - the scoped format above stays scoped on purpose).
    # A slice's -Paths list cannot contain a file the slice has not created yet, and `dotnet ef
    # migrations add` emits UNFORMATTED files: a builder that formats its slice and THEN generates a
    # migration passes every scoped gate and still lands a repo that fails CI's `csharpier check .`.
    # That is not hypothetical - it turned master's CI red on 04db9e97 (2026-08-25) with the two
    # AddSongRequestQueueItemIsInFlight migrations. Repo-wide drift is gone since S115, so this check
    # is ~10s over ~2850 files and any hit is a real regression, not legacy noise.
    Write-Host '== csharpier check (repo-wide, catches files created after the scoped format) =='
    Invoke-Native 'csharpier check failed OUTSIDE the slice files - a generated or forgotten file is unformatted (run: dotnet csharpier format . from server/)' { dotnet csharpier check . }

    Write-Host '== cleanup (unused usings, slice files only) =='
    # IDE0005 only (S-cleanup, server/.editorconfig): csharpier is formatting-only and never touches
    # this. Scoped to the slice's own paths for the same reason as the csharpier calls above - don't
    # let one slice fix files another in-flight session owns. Migrations are excluded in .editorconfig
    # itself ([**/Migrations/*.cs] -> IDE0005 = none), not here, so this include list stays simple.
    # Not [System.IO.Path]::GetRelativePath - that is .NET Core only and throws under Windows
    # PowerShell 5.1, which is the shell this script actually runs in.
    [string]$serverRoot = (Resolve-Path $server).Path.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    [string[]]$relativePaths = $resolvedPaths | ForEach-Object {
        if ($_.StartsWith($serverRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $_.Substring($serverRoot.Length)
        }
        else { $_ }
    }
    Invoke-Native 'dotnet format style failed on slice files' {
        dotnet format style NomNomzBot.slnx --include @relativePaths --severity warn --no-restore
    }

    Write-Host '== inspect (ReSharper, slice files only) =='
    # Two inspections Roslyn has no equivalent for, so dotnet format above cannot see them:
    # RedundantSuppressNullableWarningExpression (a needless `!`) and MergeIntoPattern. ReSharper
    # DETECTS both but cleanupcode does not auto-fix either (verified against all three cleanup
    # tasks), so this gates instead of fixing - the point is that the mess never reaches a review.
    [string]$inspectReport = Join-Path ([System.IO.Path]::GetTempPath()) 'slice-inspect.xml'
    # jb takes ONE semicolon-joined wildcard list on --include=<value>; splatted args are rejected.
    [string]$inspectInclude = ($relativePaths -join ';')
    Invoke-Native 'jb inspectcode failed on slice files' {
        dotnet jb inspectcode NomNomzBot.slnx --include="$inspectInclude" --no-build --format=Xml `
            --output="$inspectReport" --severity=WARNING
    }

    # Gate by CATEGORY, not by a list of ids. The owner's examples (a redundant `!`, a mergeable
    # pattern) are two members of two families; a hardcoded pair would just hand him the siblings
    # instead. CodeRedundancy + LanguageUsage are the families those two live in. Unused-symbol
    # noise (UnusedMember/UnusedType/...) is deliberately NOT gated: a symbol used only by a
    # not-yet-written caller is normal mid-slice, and failing on it would make the gate lie.
    [string[]]$gatedCategories = @('CodeRedundancy', 'LanguageUsage')
    [xml]$inspected = Get-Content $inspectReport
    [hashtable]$categoryOf = @{}
    foreach ($type in $inspected.SelectNodes('//IssueType')) { $categoryOf[$type.Id] = $type.CategoryId }
    [object[]]$hits = $inspected.SelectNodes('//Issue') | Where-Object {
        $gatedCategories -contains $categoryOf[$_.TypeId]
    }
    if ($hits.Count -gt 0) {
        $hits | ForEach-Object { Write-Host ("  {0}:{1} {2}" -f $_.File, $_.Line, $_.Message) }
        throw "ReSharper found $($hits.Count) gated issue(s) - fix them before committing"
    }

    Write-Host 'SLICE CHECK OK - safe to commit with: git commit --only -m "..." -- <paths>'
}
finally {
    Pop-Location
    if ($worktree) { git -C $repo worktree remove --force $worktree }
}
