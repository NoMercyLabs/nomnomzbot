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
    [string]$AtCommit,
    # Force the ReSharper leg inside the devbox, where it is skipped by default.
    [switch]$Inspect
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

# A stray testhost, a running API, or a lingering VBCSCompiler holds the output DLLs, and the build
# then reports CS2012 file-lock errors that read exactly like real compile errors. verify-tree.ps1
# has always killed these first; this gate did not, and the false red sends the slice hunting a
# compiler bug that is not there.
Get-Process -Name 'testhost', 'NomNomzBot.Api', 'VBCSCompiler' -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not $IsWindows) {
    Get-Process -Name 'dotnet' -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'testhost|NomNomzBot\.Api' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 2

Push-Location $server
try {
    Write-Host '== build =='
    Invoke-Native 'build failed' { dotnet build NomNomzBot.slnx -c Debug }

    Write-Host '== test (slice filter) =='
    Invoke-Native 'slice tests failed' { dotnet test $TestProject -c Debug --no-build --filter $Filter }

    # Blast radius: every suite that covers a layer this slice edited, UNFILTERED.
    # A filtered run only proves the slice's own tests pass. It cannot see the test somebody else
    # wrote against the thing you just changed - which is exactly how a seeder gaining a seventh
    # system role passed this gate, was committed, and turned CI red on a test in a project the
    # filter never touched. CI runs everything; so does this now.
    [System.Collections.Generic.HashSet[string]]$affected = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $affected.Add($TestProject) | Out-Null
    foreach ($path in $Paths) {
        [string]$p = $path -replace '\\', '/'
        # a test file already names its own project; source maps to the suite that covers it
        if ($p -match 'server/tests/(NomNomzBot\.[A-Za-z0-9.]+Tests)/') { $affected.Add("tests/$($Matches[1])") | Out-Null }
        elseif ($p -match 'server/src/NomNomzBot\.(Domain|Application|Infrastructure|Api)/') { $affected.Add("tests/NomNomzBot.$($Matches[1]).Tests") | Out-Null }
    }
    foreach ($project in $affected) {
        if (-not (Test-Path (Join-Path $server $project))) { continue }
        Write-Host "== test (full suite: $project) =="
        Invoke-Native "$project failed unfiltered - the slice broke a test it does not own" {
            dotnet test $project -c Debug --no-build
        }
    }

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

    # inspectcode reads the WHOLE solution regardless of --include, so inside the devbox every file
    # crosses the Docker Desktop share and the leg goes from ~2min to 20+. Skipped there, loudly:
    # it still has to run on the host before the slice is called done.
    if ($env:NOMNOMZ_DEVBOX -eq '1' -and -not $Inspect) {
        Write-Host '== inspect SKIPPED (devbox) - re-run this gate on the host, or pass -Inspect ==' -ForegroundColor Yellow
        return
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
