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
# guard-single-color.sh — runs ON the deploy host (via cron, see the install line in ship.ps1's
# sync step) independently of any deploy. scripts/switchover.ps1 and the CI deploy job both stop
# the losing blue/green colour correctly at the END of a deploy, but neither has any say over
# what happens BETWEEN deploys — a manual `docker restart`/`docker start` on the drained colour
# (exactly what happened 2026-09-02: api-green, already stopped by a correct deploy, was manually
# restarted and then ran fully live alongside api-blue for ~9 hours, each holding its own Twitch
# EventSub session, duplicating every chat command) is invisible to the deploy scripts because
# they aren't running at that point. This closes that gap: on every tick, if both colours are up,
# it keeps whichever one is actually passing /health/ready and stops the other — the same
# same-signal tie-break switchover.ps1/ship.ps1 use, just running continuously instead of only
# during a deploy.
#
# Install (already wired into ship.ps1's stack-definition sync step — this comment documents what
# that step does, not a separate manual step):
#   */5 * * * * /opt/nomnomzbot/guard-single-color.sh >> /opt/nomnomzbot/guard-single-color.log 2>&1

set -u
cd /opt/nomnomzbot || exit 1

ps_out=$(docker ps --filter name=nomnomzbot-api- --format '{{.Names}}' 2>/dev/null || true)
blue_up=$(echo "$ps_out" | grep -c 'nomnomzbot-api-blue' || true)
green_up=$(echo "$ps_out" | grep -c 'nomnomzbot-api-green' || true)

if [ "$blue_up" -eq 0 ] || [ "$green_up" -eq 0 ]; then
  # Normal state (one live, one drained) or neither up (host down / mid-bootstrap) — nothing to do.
  exit 0
fi

ts=$(date -u +%Y-%m-%dT%H:%M:%SZ)
blue_code=$(docker exec nomnomzbot-api-blue curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health/ready 2>/dev/null || echo 000)
green_code=$(docker exec nomnomzbot-api-green curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health/ready 2>/dev/null || echo 000)

if [ "$blue_code" = "200" ] && [ "$green_code" != "200" ]; then
  echo "$ts drift detected: both colours running (blue=$blue_code green=$green_code) - stopping api-green"
  docker compose stop -t 25 api-green
elif [ "$green_code" = "200" ] && [ "$blue_code" != "200" ]; then
  echo "$ts drift detected: both colours running (blue=$blue_code green=$green_code) - stopping api-blue"
  docker compose stop -t 25 api-blue
elif [ "$blue_code" = "200" ] && [ "$green_code" = "200" ]; then
  # Both genuinely healthy - a real mid-switchover overlap (deploy in progress) or a manual start
  # of the idle colour without a code change. Never guess which one to kill while both are
  # legitimately serving; log it and let a human or the next switchover resolve it.
  echo "$ts both colours healthy (blue=$blue_code green=$green_code) - not touching either, resolve manually if this persists past a deploy window"
else
  echo "$ts both colours up but NEITHER healthy (blue=$blue_code green=$green_code) - not guessing which to stop, resolve manually"
fi
