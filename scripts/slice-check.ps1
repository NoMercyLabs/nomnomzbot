# Slice gate: build, run the slice's own tests, and format-check only the slice's own files.
# Used by every builder agent instead of re-deriving build -> test -> commit by hand.
#
# Usage:
#   scripts/slice-check.ps1 -TestProject tests/NomNomzBot.Api.Tests -Filter "FullyQualifiedName~SecurityHeaders" -Paths a.cs,b.cs
#
# The repo currently has pre-existing CSharpier drift (slice S115), so a repo-wide
# `csharpier check .` is red for reasons unrelated to any slice. This script therefore
# format-checks ONLY the paths the slice touched. Delete the -Paths scoping once S115 lands.

param(
    [Parameter(Mandatory = $true)][string]$TestProject,
    [Parameter(Mandatory = $true)][string]$Filter,
    [Parameter(Mandatory = $true)][string[]]$Paths
)

$ErrorActionPreference = 'Stop'
$server = Join-Path $PSScriptRoot '..\server' | Resolve-Path

Push-Location $server
try {
    Write-Host '== build =='
    dotnet build NomNomzBot.slnx -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    Write-Host '== test (slice filter) =='
    dotnet test $TestProject -c Debug --no-build --filter $Filter
    if ($LASTEXITCODE -ne 0) { throw 'slice tests failed' }

    Write-Host '== format (slice files only) =='
    dotnet csharpier format @Paths
    dotnet csharpier check @Paths
    if ($LASTEXITCODE -ne 0) { throw 'csharpier check failed on slice files' }

    Write-Host 'SLICE CHECK OK - safe to commit with: git commit --only -m "..." -- <paths>'
}
finally {
    Pop-Location
}
