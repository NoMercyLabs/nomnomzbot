# Verify the WHOLE tree the way an orchestrator must before accepting an agent's work.
#
# scripts/slice-check.ps1 is the pre-commit gate for ONE slice (scoped tests + scoped format + jb
# inspection). This is the other half: the full-tree, all-suites check that answers "is HEAD actually
# green?" — the question that caught, in one session, an ungated endpoint, a save-blocking registry bug,
# five unscoped domain events and a null content-type, every one of which sat behind an agent's report
# of "all green" from a FILTERED run.
#
#   scripts/verify-tree.ps1                      # build + all 4 server suites + csharpier
#   scripts/verify-tree.ps1 -IncludeApp          # also force the Kotlin jvmTest suite
#   scripts/verify-tree.ps1 -AtCommit <sha>      # verify a commit in a throwaway worktree instead
#
# Three traps it encodes, each of which produced a WRONG green today:
#   1. A stray testhost/API process holds the build DLLs, so `dotnet build` "fails" with file-lock
#      errors that look like compile errors. Killed first.
#   2. `dotnet test --no-build` against a STALE test assembly silently runs a subset — a run once
#      reported 1243 tests where the truth was 4279. The test projects are rebuilt explicitly.
#   3. Gradle skips :composeApp:jvmTest as up-to-date and prints BUILD SUCCESSFUL in ~3s. That is not a
#      test run. `cleanJvmTest jvmTest` invalidates the TEST task only, so every test really runs (~30s)
#      without the full Kotlin recompile `--rerun-tasks` forced (~minutes). Counts are still read out of
#      the XML rather than trusted from "BUILD SUCCESSFUL".
#
# The Kotlin suite runs when app/ actually changed, or on demand with -IncludeApp. A backend-only slice
# does not pay for it.

param(
    [switch]$IncludeApp,
    [string]$AtCommit
)

$ErrorActionPreference = 'Stop'
$repo = (Join-Path $PSScriptRoot '..' | Resolve-Path).Path
$worktree = $null

if ($AtCommit) {
    $worktree = Join-Path $repo ".scratch/verify-tree-$AtCommit"
    if (Test-Path $worktree) { git -C $repo worktree remove --force $worktree | Out-Null }
    git -C $repo worktree add -f --detach $worktree $AtCommit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not create a worktree at $AtCommit" }
    $root = $worktree
    Write-Host "== verifying $AtCommit in a throwaway worktree =="
}
else { $root = $repo }

$server = Join-Path $root 'server'
[bool]$failed = $false

# Trap 1: stray processes hold the DLLs and the build reports file-lock errors that read like real ones.
Get-Process -Name 'testhost', 'NomNomzBot.Api' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Push-Location $server
try {
    Write-Host '== build (warnings are errors here) =='
    dotnet build -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    # Trap 2: --no-build over a stale assembly silently runs a SUBSET and still prints "Passed!".
    [string[]]$projects = @('Domain', 'Application', 'Infrastructure', 'Api')
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

[bool]$appChanged = [bool](git -C $repo status --porcelain -- app | Select-Object -First 1) -or
    [bool](git -C $repo diff --name-only HEAD~1 -- app 2>$null | Select-Object -First 1)

if ($IncludeApp -or $appChanged) {
    Write-Host '== jvmTest (test task invalidated so it cannot be skipped as up-to-date) =='
    Push-Location $root
    try {
        & (Join-Path $root 'app/gradlew.bat') -p (Join-Path $root 'app') `
            :composeApp:cleanJvmTest :composeApp:jvmTest --console=plain | Out-Host
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }
    finally { Pop-Location }

    # Read the REAL counts rather than trusting "BUILD SUCCESSFUL".
    [int]$total = 0
    [int]$bad = 0
    Get-ChildItem -Path (Join-Path $root 'app/composeApp/build/test-results/jvmTest') -Filter '*.xml' -ErrorAction SilentlyContinue |
        ForEach-Object {
            [xml]$xml = Get-Content -LiteralPath $_.FullName
            $total += [int]$xml.testsuite.tests
            $bad += [int]$xml.testsuite.failures + [int]$xml.testsuite.errors
        }
    Write-Host "jvmTest: $total tests, $bad failures/errors"
    if ($bad -gt 0) { $failed = $true }
}

if ($worktree) { git -C $repo worktree remove --force $worktree | Out-Null }

if ($failed) { Write-Host ''; Write-Host 'TREE IS RED'; exit 1 }
Write-Host ''
Write-Host 'TREE IS GREEN'
exit 0
