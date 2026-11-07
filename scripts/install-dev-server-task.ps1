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

# Registers the scheduled task "NomNomzBot dev-server keepalive" for the signed-in user: it runs
# dev-server-keepalive.ps1 at logon and every 2 minutes after, with no window. Run again to update it.
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'dev-server-keepalive.ps1'
$action = New-ScheduledTaskAction -Execute 'conhost.exe' `
  -Argument "--headless pwsh.exe -NoProfile -NonInteractive -File `"$script`""
$atLogon = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$every2 = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 2)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
  -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'NomNomzBot dev-server keepalive' -Action $action `
  -Trigger @($atLogon, $every2) -Settings $settings -Force | Out-Null
Write-Output 'registered: NomNomzBot dev-server keepalive (at logon + every 2 minutes)'
