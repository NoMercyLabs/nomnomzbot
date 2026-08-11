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
# reinstall-local.ps1 — dev-loop helper: build the plugin, install it into this machine's
# Elgato Stream Deck plugins folder, restart the app, and confirm a clean reconnect from
# its own log (no crash). Run from tools/streamdeck/.

$ErrorActionPreference = "Stop"

npm run build
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$src = Join-Path $PSScriptRoot "..\bot.nomnomzbot.streamdeck.sdPlugin" | Resolve-Path
$dst = "$env:APPDATA\Elgato\StreamDeck\Plugins\bot.nomnomzbot.streamdeck.sdPlugin"

Get-Process -Name "StreamDeck" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
Copy-Item -Recurse $src $dst

Start-Process "C:\Program Files\Elgato\StreamDeck\StreamDeck.exe"
Start-Sleep -Seconds 8

$log = "$env:APPDATA\Elgato\StreamDeck\logs\StreamDeck.log"
$connected = Select-String -Path $log -Pattern "\[bot\.nomnomzbot\.streamdeck\] Plugin connected" |
    Select-Object -Last 1
if ($connected) {
    Write-Host "REINSTALL: OK — $($connected.Line)" -ForegroundColor Green
} else {
    Write-Host "REINSTALL: plugin did not report connected — check $log" -ForegroundColor Red
    exit 1
}
