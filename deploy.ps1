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
# NomNomzBot deploy (Windows) — one script, three scenarios. Full guide: DEPLOY.md
#
#   .\deploy.ps1 desktop        single-file bot on this machine — no Docker, SQLite
#   .\deploy.ps1 docker         full stack in Docker — Postgres + Redis + API (+ Adminer)
#   .\deploy.ps1 saas           the Docker stack in multi-tenant SaaS mode
#   .\deploy.ps1 <any> -App     ALSO build the standalone desktop dashboard installer
#
# Idempotent: re-run any time. The web dashboard is bundled into every backend
# artifact automatically — after any scenario, open the API URL in a browser.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Scenario,

    [switch]$App,

    # Legacy alias for the desktop scenario.
    [switch]$Lite
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

# --- helpers ------------------------------------------------------------------

function Show-Guide {
    Write-Host @'
NomNomzBot deploy — pick a scenario (full guide: DEPLOY.md)

  .\deploy.ps1 desktop   Run the bot on THIS machine as one single file — no Docker,
                         SQLite, zero dependencies. Best for: one streamer, a PC/NUC.
  .\deploy.ps1 docker    Full stack in Docker: Postgres + Redis + API (+ Adminer).
                         Best for: a home server, database durability, room to grow.
  .\deploy.ps1 saas      The same Docker stack in multi-tenant SaaS mode, behind
                         YOUR HTTPS reverse proxy. Best for: hosting for others.

Dashboard (both work in every scenario):
  web app     nothing to build — the bot serves it; open the API URL in a browser.
  -App        also build the standalone desktop dashboard installer for THIS OS.

Example: .\deploy.ps1 desktop -App
'@
}

function Get-EnvValue([string]$Key) {
    if (-not (Test-Path .env)) { return '' }
    [string]$line = Get-Content .env | Where-Object { $_ -match "^$([regex]::Escape($Key))=" } | Select-Object -First 1
    if ([string]::IsNullOrEmpty($line)) { return '' }
    return $line.Substring($Key.Length + 1)
}

function Set-EnvValue([string]$Key, [string]$Value) {
    [string[]]$lines = Get-Content .env
    [bool]$replaced = $false
    [string[]]$updated = foreach ($line in $lines) {
        if (-not $replaced -and $line -match "^$([regex]::Escape($Key))=") {
            $replaced = $true
            "$Key=$Value"
        }
        else { $line }
    }
    if (-not $replaced) { $updated += "$Key=$Value" }
    Set-Content -Path .env -Value $updated
}

function New-RandomBase64 {
    [byte[]]$bytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function New-RandomHex {
    [byte[]]$bytes = [byte[]]::new(24)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

# Create .env from the template with real generated secrets; prompt for Twitch
# credentials when a terminal is attached (Enter = skip, use the dashboard wizard).
function Initialize-EnvFile {
    if (Test-Path .env) { return $true }

    Write-Host 'No .env found — creating one from .env.example with freshly generated secrets.'
    Copy-Item .env.example .env
    Set-EnvValue 'JWT_SECRET' (New-RandomBase64)
    Set-EnvValue 'ENCRYPTION_KEY' (New-RandomBase64)
    Set-EnvValue 'POSTGRES_PASSWORD' (New-RandomHex)

    if (-not [Console]::IsInputRedirected) {
        Write-Host ''
        Write-Host 'Twitch app credentials (https://dev.twitch.tv/console/apps).'
        Write-Host "Press Enter to skip any value — you can also enter them later in the dashboard's setup wizard."
        [string]$twId = Read-Host '  TWITCH_CLIENT_ID    '
        [string]$twSecret = Read-Host '  TWITCH_CLIENT_SECRET'
        [string]$twBot = Read-Host '  TWITCH_BOT_USERNAME '
        if ($twId) { Set-EnvValue 'TWITCH_CLIENT_ID' $twId }
        if ($twSecret) { Set-EnvValue 'TWITCH_CLIENT_SECRET' $twSecret }
        if ($twBot) { Set-EnvValue 'TWITCH_BOT_USERNAME' $twBot }
        return $true
    }

    Write-Host ''
    Write-Host '  >> .env created (secrets generated). Edit it to set TWITCH_CLIENT_ID,'
    Write-Host '     TWITCH_CLIENT_SECRET and TWITCH_BOT_USERNAME — or leave them blank and'
    Write-Host "     use the dashboard's setup wizard — then re-run this script."
    return $false
}

# Bring the compose stack up (pull the published image when configured, build otherwise),
# then block until /health/ready goes green.
function Start-ComposeStack {
    [string]$apiImage = Get-EnvValue 'API_IMAGE'
    [string]$port = Get-EnvValue 'API_HTTP_PORT'
    if (-not $port) { $port = '5080' }
    [string]$baseUrl = Get-EnvValue 'API_BASE_URL'
    if (-not $baseUrl) { $baseUrl = "http://localhost:$port" }
    [string]$adminerPort = Get-EnvValue 'ADMINER_PORT'
    if (-not $adminerPort) { $adminerPort = '8082' }

    if ($apiImage -and $apiImage -ne 'nomnomzbot-api:local') {
        Write-Host "Pulling the published image ($apiImage)..."
        docker compose pull api
        if ($LASTEXITCODE -ne 0) { Write-Error 'docker compose pull failed.'; exit $LASTEXITCODE }
        docker compose up -d --no-build
    }
    else {
        Write-Host 'Building the image locally (includes the web dashboard) and starting the stack...'
        docker compose up -d --build
    }
    if ($LASTEXITCODE -ne 0) { Write-Error 'docker compose up failed.'; exit $LASTEXITCODE }

    Write-Host 'Waiting for the API to report ready (this includes first-run migrations)' -NoNewline
    for ($i = 0; $i -lt 60; $i++) {
        try {
            Invoke-WebRequest -Uri "http://localhost:$port/health/ready" -TimeoutSec 3 | Out-Null
            Write-Host ' ready.'
            Write-Host ''
            Write-Host 'Stack is up:'
            Write-Host "  Dashboard (web) : $baseUrl"
            Write-Host "  Health          : $baseUrl/health"
            Write-Host "  Adminer (DB)    : http://localhost:$adminerPort"
            Write-Host ''
            Write-Host "Point the desktop dashboard app at $baseUrl, or just use the web dashboard."
            return
        }
        catch {
            Write-Host '.' -NoNewline
            Start-Sleep -Seconds 3
        }
    }

    Write-Host ''
    Write-Error 'The API did not become ready within 3 minutes — inspect it with: docker compose logs -f api'
    exit 1
}

# --- scenario: desktop (self_host_lite — single-file binary) -------------------

function Invoke-DesktopScenario {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error 'The .NET SDK is required to build the desktop binary. Install .NET 10 from https://dot.net'
        exit 1
    }

    [string]$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
        'AMD64' { 'x64' }
        'ARM64' { 'arm64' }
        default { Write-Error "Unsupported architecture for the desktop binary: $env:PROCESSOR_ARCHITECTURE"; exit 1 }
    }
    [string]$rid = "win-$arch"

    Write-Host "Publishing the single-file bot (self_host_lite) for $rid — the web dashboard is bundled in..."
    dotnet publish server/src/NomNomzBot.Api -c Release -r $rid --self-contained true
    if ($LASTEXITCODE -ne 0) { Write-Error 'dotnet publish failed.'; exit $LASTEXITCODE }

    [string]$out = "server/src/NomNomzBot.Api/bin/Release/net10.0/$rid/publish/nomnomz.exe"
    Write-Host ''
    Write-Host 'Done. Your single-file bot is at:'
    Write-Host "  $out"
    Write-Host 'Copy it anywhere and run it:'
    Write-Host "  Copy-Item `"$out`" .\nomnomz.exe; .\nomnomz.exe"
    Write-Host 'Its data (SQLite DB, keys, logs) lives in %LOCALAPPDATA%\NomNomzBot — override with NOMNOMZ_DATA_DIR.'
    Write-Host 'Then open the web dashboard at http://localhost:5080 — or use the desktop app (-App).'
}

# --- scenario: docker (self_host_full — compose stack) --------------------------

function Invoke-DockerScenario {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Error "Docker is required for this scenario. Install Docker Desktop from https://docs.docker.com/get-docker/ (or run '.\deploy.ps1 desktop' for the no-Docker single-file bot)."
        exit 1
    }
    if (-not (Initialize-EnvFile)) { exit 0 }

    [string]$mode = Get-EnvValue 'DEPLOYMENT_MODE'
    if ($mode -and $mode -ne 'self_host_full') {
        Write-Host "Note: .env has DEPLOYMENT_MODE=$mode — resetting it to self_host_full for this scenario."
        Set-EnvValue 'DEPLOYMENT_MODE' 'self_host_full'
    }

    Start-ComposeStack
}

# --- scenario: saas (multi-tenant fleet mode) -----------------------------------

function Invoke-SaasScenario {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Error 'Docker is required for this scenario. Install it from https://docs.docker.com/get-docker/'
        exit 1
    }
    if (-not (Initialize-EnvFile)) { exit 0 }

    # Fail-closed guards — SaaS is public and multi-tenant, so weak or local values are refused.
    [string]$baseUrl = Get-EnvValue 'API_BASE_URL'
    if ($baseUrl -notmatch '^https://') {
        Write-Error "SaaS requires API_BASE_URL in .env to be your public HTTPS origin (behind your reverse proxy) — currently '$baseUrl'. See DEPLOY.md (SaaS)."
        exit 1
    }
    if ($baseUrl -match 'localhost|127\.0\.0\.1') {
        Write-Error "SaaS requires a public API_BASE_URL — '$baseUrl' points at this machine. See DEPLOY.md (SaaS)."
        exit 1
    }
    if ((Get-EnvValue 'JWT_SECRET') -eq 'dev-secret-key-at-least-32-characters-long!!') {
        Write-Error 'SaaS refuses the dev JWT_SECRET. Set a strong one in .env (32+ random bytes, base64).'
        exit 1
    }
    if ((Get-EnvValue 'ENCRYPTION_KEY') -eq 'ZGV2LWVuY3J5cHRpb24ta2V5LWZvci1sb2NhbC1kZXY=') {
        Write-Error 'SaaS refuses the dev ENCRYPTION_KEY. Set a strong one in .env (changing it later invalidates all stored OAuth tokens).'
        exit 1
    }

    if ((Get-EnvValue 'DEPLOYMENT_MODE') -ne 'saas') {
        Write-Host 'Setting DEPLOYMENT_MODE=saas in .env.'
        Set-EnvValue 'DEPLOYMENT_MODE' 'saas'
    }

    Write-Host 'SaaS mode: TLS terminates at YOUR reverse proxy; set TRUSTED_PROXY_NETWORKS in .env if the'
    Write-Host 'proxy reaches the API over a docker network (e.g. 172.16.0.0/12). Scale-out guidance: DEPLOY.md.'
    Start-ComposeStack
}

# --- standalone desktop dashboard app (optional, any scenario) ------------------

function Build-DesktopApp {
    if (-not (Get-Command java -ErrorAction SilentlyContinue)) {
        Write-Error 'A JDK (21 recommended) is required to build the desktop dashboard app — https://adoptium.net'
        exit 1
    }

    Write-Host ''
    Write-Host 'Building the standalone desktop dashboard installer for this OS...'
    Push-Location app
    try {
        .\gradlew.bat :composeApp:packageDistributionForCurrentOS --console=plain
        if ($LASTEXITCODE -ne 0) {
            Write-Error 'Gradle packaging failed. On Windows, building the MSI additionally requires the WiX Toolset 3.x (https://wixtoolset.org) on PATH.'
            exit $LASTEXITCODE
        }
    }
    finally { Pop-Location }

    Write-Host ''
    Write-Host 'Desktop app installer(s) at:'
    Write-Host '  app\composeApp\build\compose\binaries\main\'
    Write-Host "Install it, launch it, and point it at your bot's URL (it also finds LAN bots automatically)."
}

# --- scenario dispatch ----------------------------------------------------------

if ($Lite) { $Scenario = 'desktop' }   # legacy alias

if (-not $Scenario) {
    Show-Guide
    exit 0
}

switch ($Scenario.ToLowerInvariant()) {
    'desktop' { Invoke-DesktopScenario }
    'docker'  { Invoke-DockerScenario }
    'saas'    { Invoke-SaasScenario }
    default {
        Write-Host "Unknown scenario: $Scenario" -ForegroundColor Red
        Write-Host ''
        Show-Guide
        exit 2
    }
}

if ($App) { Build-DesktopApp }

exit 0
