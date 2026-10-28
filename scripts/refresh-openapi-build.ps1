# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot. You may use, change and share it for free,
#  for any noncommercial purpose, under the PolyForm Noncommercial License
#  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
#  See LICENSE for details, and TRADEMARKS.md for the name and logo.
#
#  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# -----------------------------------------------------------------------------

# refresh-openapi-build.ps1 - regenerate server/openapi/v1.json WITHOUT running the API.
#
# The API host never starts: Microsoft.Extensions.ApiDescription.Server writes the document at
# build time. That package is not part of the shipped API, so this script adds it to the csproj
# for one build and always puts the csproj back (finally block). A running API on this PC joins the
# owner's channel and answers chat twice, so prefer this over refresh-openapi.ps1.
#
# Usage:
#   scripts/refresh-openapi-build.ps1

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repo 'server/src/NomNomzBot.Api/NomNomzBot.Api.csproj'
$outDir = Join-Path $repo 'server/src/NomNomzBot.Api/obj/openapi-out'
$generated = Join-Path $outDir 'NomNomzBot.Api.json'
$snapshot = Join-Path $repo 'server/openapi/v1.json'

$original = [System.IO.File]::ReadAllText($csproj)
$generatorProps = @'
    <OpenApiGenerateDocuments>true</OpenApiGenerateDocuments>
    <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/obj/openapi-out</OpenApiDocumentsDirectory>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" VersionOverride="10.0.8" PrivateAssets="all" />
  </ItemGroup>
'@

$marker = '  </PropertyGroup>'
$at = $original.IndexOf($marker)
if ($at -lt 0) { throw "no </PropertyGroup> in $csproj" }

try {
    $patched = $original.Substring(0, $at) + $generatorProps.TrimEnd() + $original.Substring($at + $marker.Length)
    [System.IO.File]::WriteAllText($csproj, $patched)
    if (Test-Path $generated) { Remove-Item $generated }

    Write-Host '== building the API with document generation (the host never runs) =='
    dotnet build $csproj -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
    if (-not (Test-Path $generated)) { throw "build wrote no document at $generated" }
}
finally {
    [System.IO.File]::WriteAllText($csproj, $original)
    Write-Host '   csproj restored'
}

# The generated document has no servers entry; the snapshot keeps the one the served API reports.
$text = [System.IO.File]::ReadAllText($generated)
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$pathsLine = "$nl  `"paths`": {"
$hits = ([regex]::Matches($text, [regex]::Escape($pathsLine))).Count
if ($hits -ne 1) { throw "expected one top-level paths entry, found $hits" }

$servers = "$nl  `"servers`": [$nl    {$nl      `"url`": `"http://localhost:5080/`"$nl    }$nl  ],$pathsLine"
$text = $text.Replace($pathsLine, $servers)
[System.IO.File]::WriteAllText($snapshot, $text, (New-Object System.Text.UTF8Encoding($false)))

$paths = (Get-Content $snapshot -Raw | ConvertFrom-Json).paths.PSObject.Properties.Name.Count
Write-Host "== snapshot written: $paths paths =="
