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

# Keeps the dashboard dev server (Vite, port 5090) running. dev.nomnomz.bot and the Stream Deck plugin reach
# the live bot through it, so a PC restart without it left both dead (Stoney, 2026-11-07).
# Runs from the scheduled task that install-dev-server-task.ps1 registers: at logon and every 2 minutes.
# When something already listens on the port it does nothing; otherwise it starts Vite hidden, pointed at the bot.
param(
  [int]$Port = 5090,
  [string]$Api = 'http://192.168.2.34:5080',
  [string]$LogDir = (Join-Path $env:LOCALAPPDATA 'NomNomzBot\dev-server')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$log = Join-Path $LogDir "keepalive-$((Get-Date).ToString('yyyy-MM-dd')).log"

$listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listening) { return }

$dashboard = Join-Path (Split-Path -Parent $PSScriptRoot) 'dashboard'
$viteLog = Join-Path $LogDir 'vite.log'
$env:NNZ_API = $Api
Start-Process -WindowStyle Hidden -WorkingDirectory $dashboard -FilePath 'cmd.exe' `
  -ArgumentList '/c', "npx.cmd vite --host >> `"$viteLog`" 2>&1"
Add-Content -LiteralPath $log -Value "$((Get-Date).ToString('s')) port $Port was free; started Vite with NNZ_API=$Api"
