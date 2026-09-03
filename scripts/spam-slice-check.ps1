# Build -> test -> format gate for a Domain-layer slice, written down once instead of re-derived.
#
# The by-hand version drifted three times in one session and cost real time: $LASTEXITCODE after a
# PIPELINE reflects the last element (Select-String), not dotnet, so a failing build read as green and a
# failing test run read as "no output". This script judges every native call by its own exit code, and
# reads results from a TRX file rather than parsing stdout, which repeatedly arrived empty.
#
# Usage:
#   scripts/spam-slice-check.ps1                       # build + full Domain suite + csharpier check
#   scripts/spam-slice-check.ps1 -Filter MessageNorm   # only matching tests
#   scripts/spam-slice-check.ps1 -Format               # format instead of just checking

param(
    [string]$TestProject = 'tests/NomNomzBot.Domain.Tests',
    [string]$Filter = '',
    [switch]$Format
)

$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot '..' | Resolve-Path
$server = Join-Path $repo 'server'
$trx = Join-Path $repo '.scratch/slice-check.trx'

Push-Location $server
try {
    Write-Output '== build =='
    & dotnet build --nologo -v q 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # Re-run verbose ONLY on failure, so the error lines are actually visible.
        & dotnet build --nologo 2>&1 | Select-String 'error' | Select-Object -Unique -First 10
        throw "build failed ($LASTEXITCODE)"
    }
    Write-Output 'build GREEN'

    Write-Output '== test =='
    if (Test-Path $trx) { Remove-Item $trx -Force }
    $testArgs = @($TestProject, '--no-build', '--nologo', '--logger', "trx;LogFileName=$trx")
    if ($Filter) { $testArgs += @('--filter', "FullyQualifiedName~$Filter") }
    & dotnet test @testArgs 2>&1 | Out-Null
    $testExit = $LASTEXITCODE

    if (Test-Path $trx) {
        [xml]$report = Get-Content $trx
        $c = $report.TestRun.ResultSummary.Counters
        Write-Output "total=$($c.total) passed=$($c.passed) failed=$($c.failed)"
        if ([int]$c.failed -gt 0) {
            $report.TestRun.Results.UnitTestResult |
                Where-Object { $_.outcome -eq 'Failed' } |
                ForEach-Object {
                    Write-Output "FAIL: $($_.testName)"
                    if ($_.Output.ErrorInfo.Message) {
                        Write-Output "   $(($_.Output.ErrorInfo.Message -split "`n")[0].Trim())"
                    }
                }
        }
    }
    else {
        Write-Output 'NO TRX WRITTEN — treat as failure, not as a pass'
    }
    if ($testExit -ne 0) { throw "tests failed ($testExit)" }

    Write-Output '== csharpier =='
    if ($Format) {
        & dotnet csharpier format . | Out-Null
        Write-Output 'formatted'
    }
    else {
        & dotnet csharpier check . 2>&1 | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0) { throw 'csharpier check failed — run with -Format' }
        Write-Output 'csharpier CLEAN'
    }

    Write-Output 'SLICE GREEN'
}
finally {
    Pop-Location
}
