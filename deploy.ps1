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
# NomNomzBot quickstart (Windows). Two ways to run:
#
#   .\deploy.ps1          self_host_full — Docker stack (Postgres + Redis + API + Adminer)
#   .\deploy.ps1 -Lite    self_host_lite — single-file binary, no Docker, no Postgres/Redis
#
# Idempotent: re-run any time. The full path copies .env.example -> .env on first run and
# brings the stack up; the lite path publishes the self-contained nomnomz.exe binary.

[CmdletBinding()]
param(
    [switch]$Lite
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

# --- lite: publish the single self-contained binary --------------------------
if ($Lite) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "The .NET SDK is required to build the lite binary. Install .NET 10 from https://dot.net"
        exit 1
    }

    # Map the host architecture to a .NET runtime identifier (Windows host => win-*).
    $arch = switch ($env:PROCESSOR_ARCHITECTURE) {
        'AMD64' { 'x64' }
        'ARM64' { 'arm64' }
        default { Write-Error "Unsupported architecture for the lite binary: $env:PROCESSOR_ARCHITECTURE"; exit 1 }
    }
    $rid = "win-$arch"

    Write-Host "Publishing the self_host_lite single-file binary for $rid..."
    dotnet publish server/src/NomNomzBot.Api -c Release -r $rid --self-contained true
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed."; exit $LASTEXITCODE }

    $out = "server/src/NomNomzBot.Api/bin/Release/net10.0/$rid/publish/nomnomz.exe"
    Write-Host ""
    Write-Host "Done. Your single-file bot is at:"
    Write-Host "  $out"
    Write-Host "Copy it anywhere and run it (it creates nomnomz.db beside itself on first start):"
    Write-Host "  Copy-Item `"$out`" .\nomnomz.exe; .\nomnomz.exe"
    exit 0
}

# --- full: the Docker stack --------------------------------------------------
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "Docker is required for the full stack. Install Docker Desktop from https://docs.docker.com/get-docker/ (or run '.\deploy.ps1 -Lite' for the no-Docker single-file binary)."
    exit 1
}

if (-not (Test-Path .env)) {
    Write-Host "No .env found - creating one from .env.example."
    Copy-Item .env.example .env
    Write-Host ""
    Write-Host "  >> Edit .env and set TWITCH_CLIENT_ID, TWITCH_CLIENT_SECRET and TWITCH_BOT_USERNAME,"
    Write-Host "     then re-run .\deploy.ps1. Generate secrets with: openssl rand -base64 32"
    exit 0
}

Write-Host "Building and starting the NomNomzBot stack..."
docker compose up -d --build
if ($LASTEXITCODE -ne 0) { Write-Error "docker compose up failed."; exit $LASTEXITCODE }

# Pull the few URLs we echo from .env (fall back to defaults).
$baseUrl = (Select-String -Path .env -Pattern '^\s*API_BASE_URL\s*=\s*(.+)$' | Select-Object -First 1).Matches.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($baseUrl)) { $baseUrl = 'http://localhost:5080' }
$adminerPort = (Select-String -Path .env -Pattern '^\s*ADMINER_PORT\s*=\s*(.+)$' | Select-Object -First 1).Matches.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($adminerPort)) { $adminerPort = '8082' }

Write-Host ""
Write-Host "Stack is up. Once the API reports ready:"
Write-Host "  Dashboard / API : $baseUrl"
Write-Host "  API docs        : $baseUrl/scalar"
Write-Host "  Health          : $baseUrl/health"
Write-Host "  Adminer (DB)    : http://localhost:$adminerPort"
Write-Host ""
Write-Host "Follow startup with: docker compose logs -f api"
