#!/usr/bin/env bash
# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------

# Deploys one release channel on the box with no gap: pulls ghcr.io/nomercylabs/nomnomzbot:<channel>, starts the
# idle colour, waits for its own /health/ready, then drains the live one. Run from /opt/nomnomzbot/channels:
#
#   ./deploy-channel.sh stable
#
# The channel's secrets live in <channel>.env next to this script (created once by init-channel.sh).
set -euo pipefail

channel="${1:-}"
case "$channel" in
  stable) ;;
  *) echo "usage: $0 stable" >&2; exit 2 ;;
esac

cd "$(dirname "$0")"
[ -f "${channel}.env" ] || { echo "${channel}.env is missing; run ./init-channel.sh ${channel} first" >&2; exit 1; }

export CHANNEL="$channel"
export COMPOSE_PROFILES=green
compose() { docker compose -p "nnz-${channel}" --env-file "${channel}.env" -f docker-compose.channel.yml "$@"; }

docker pull "ghcr.io/nomercylabs/nomnomzbot:${channel}"

running=$(docker ps --filter "name=nnz-${channel}-api-" --format '{{.Names}}' || true)
blue_up=$(echo "$running" | grep -c "nnz-${channel}-api-blue" || true)
green_up=$(echo "$running" | grep -c "nnz-${channel}-api-green" || true)

if [ "$blue_up" -gt 0 ] && [ "$green_up" -gt 0 ]; then
  echo "Both colours of ${channel} are running. Stop the one Caddy does not route to, then deploy again." >&2
  exit 1
fi

if [ "$green_up" -gt 0 ]; then live=green; idle=blue; else live=blue; idle=green; fi
echo "${channel}: live api-${live}, deploying api-${idle}"

compose up -d --pull always postgres redis
compose up -d --no-deps --pull always "api-${idle}"

for i in $(seq 1 30); do
  code=$(docker exec "nnz-${channel}-api-${idle}" curl -s -o /dev/null -w '%{http_code}' \
    http://localhost:5000/health/ready 2>/dev/null || echo 000)
  if [ "$code" = "200" ]; then
    echo "api-${idle} ready after ${i}0s"
    if [ "$blue_up" -gt 0 ] || [ "$green_up" -gt 0 ]; then
      compose stop -t 35 "api-${live}"
      echo "api-${live} drained"
    fi
    docker exec "nnz-${channel}-api-${idle}" curl -s http://localhost:5000/health/version
    echo
    exit 0
  fi
  sleep 10
done

echo "api-${idle} never passed /health/ready; api-${live} keeps serving." >&2
compose logs "api-${idle}" --tail=100 >&2
compose stop "api-${idle}" || true
exit 1
