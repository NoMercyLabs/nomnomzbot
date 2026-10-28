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
#
# reinstall-local.ps1 - dev-loop helper: build the Music plugin, install it into this machine's
# Elgato Stream Deck plugins folder, restart the app, and confirm a clean reconnect from
# its own log (no crash). Run from tools/streamdeck/music/.

$ErrorActionPreference = "Stop"

npm run build
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$src = Join-Path $PSScriptRoot "..\bot.nomnomzbot.streamdeck.music.sdPlugin" | Resolve-Path
$dst = "$env:APPDATA\Elgato\StreamDeck\Plugins\bot.nomnomzbot.streamdeck.music.sdPlugin"

Get-Process -Name "StreamDeck" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
Copy-Item -Recurse $src $dst

Start-Process "C:\Program Files\Elgato\StreamDeck\StreamDeck.exe"

# App boot + plugin spin-up is not instant (device attach alone can take 10-15s) - poll rather than
# guess a fixed sleep, which has produced false "did not connect" failures on a slower boot.
$log = "$env:APPDATA\Elgato\StreamDeck\logs\StreamDeck.log"
$connected = $null
for ($i = 0; $i -lt 15 -and -not $connected; $i++) {
    Start-Sleep -Seconds 2
    if (Test-Path $log) {
        $connected = Select-String -Path $log -Pattern "\[bot\.nomnomzbot\.streamdeck\.music\] Plugin connected" |
            Select-Object -Last 1
    }
}
if ($connected) {
    Write-Host "REINSTALL: OK - $($connected.Line)" -ForegroundColor Green
} else {
    Write-Host "REINSTALL: plugin did not report connected within 30s - check $log" -ForegroundColor Red
    exit 1
}
