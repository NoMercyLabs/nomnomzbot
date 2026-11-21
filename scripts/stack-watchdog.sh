#!/usr/bin/env bash
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
# stack-watchdog.sh - runs ON the deploy host by cron, every minute. It restarts a stack container
# that Docker lists as running while its process, or its published port, is gone.
#
# Why: when containerd restarts, the container processes and port proxies can die while Docker
# keeps showing "Up". The `restart: unless-stopped` policy never fires, because Docker saw no exit
# (2026-11-21: the API, Redis, the tunnel and the Caddy port were dead for 7 hours).
#
# A stopped container (the drained blue/green colour) is not listed as running, so it stays stopped.
#
#   * * * * * <deploy dir>/stack-watchdog.sh >> <deploy dir>/stack-watchdog.log 2>&1

# A container younger than this may not listen on its port yet (a deploy or a restart in progress).
PORT_GRACE_SEC=180
PROC_ROOT="${PROC_ROOT:-/proc}"
# Optional, root-only (chmod 600). Two lines: DISCORD_BOT_TOKEN=... and DISCORD_ALERT_USER_ID=...
WATCHDOG_ENV="${WATCHDOG_ENV:-$(dirname "$0")/stack-watchdog.env}"

set -u

process_alive() { # <container id> <pid>
  grep -q "$1" "$PROC_ROOT/$2/cgroup" 2>/dev/null
}

age_sec() { # <startedAt>
  started_epoch=$(date -u -d "$1" +%s 2>/dev/null || echo 0)
  echo $(($(date -u +%s) - started_epoch))
}

# Prints the first published IPv4 host port that refuses a connection.
closed_port() { # <container name>
  docker port "$1" 2>/dev/null | sed -n 's/.*-> [0-9.]*:\([0-9]*\)$/\1/p' | sort -u | while read -r port; do
    if ! timeout 3 bash -c "exec 3<>/dev/tcp/127.0.0.1/$port" 2>/dev/null; then
      echo "$port"
      break
    fi
  done
}

restarted=""

restart() { # <container name> <reason>
  echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $1: $2 - restarting"
  docker restart "$1" >/dev/null
  restarted="$restarted\\n- $1: $2"
}

discord_post() { # <api path> <json body>
  # The header comes from a file descriptor, so the token never shows in the process list.
  curl -s -m 15 -X POST "https://discord.com/api/v10$1" \
    -H @<(printf 'Authorization: Bot %s' "$DISCORD_BOT_TOKEN") \
    -H 'Content-Type: application/json' -d "$2"
}

# Sends one Discord DM from the bot's own Discord app. Without the config file it sends nothing.
alert() { # <message, with \n as the line break>
  [ -r "$WATCHDOG_ENV" ] || return 0
  # shellcheck disable=SC1090
  . "$WATCHDOG_ENV"
  [ -n "${DISCORD_BOT_TOKEN:-}" ] && [ -n "${DISCORD_ALERT_USER_ID:-}" ] || return 0

  channel=$(discord_post /users/@me/channels "{\"recipient_id\":\"$DISCORD_ALERT_USER_ID\"}" \
    | grep -o '"id": *"[0-9]*"' | head -1 | grep -o '[0-9]*')
  if [ -z "$channel" ]; then
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) alert: Discord gave no DM channel - no alert sent"
    return 0
  fi
  discord_post "/channels/$channel/messages" "{\"content\":\"$1\"}" >/dev/null
}

for name in $(docker ps --filter name=nomnomzbot- --format '{{.Names}}' 2>/dev/null); do
  read -r id pid started <<<"$(docker inspect --format '{{.Id}} {{.State.Pid}} {{.State.StartedAt}}' "$name" 2>/dev/null)"
  [ -n "${started:-}" ] || continue

  if ! process_alive "$id" "$pid"; then
    restart "$name" "Docker lists it as running, but process $pid is gone"
    continue
  fi

  [ "$(age_sec "$started")" -ge "$PORT_GRACE_SEC" ] || continue
  port=$(closed_port "$name")
  if [ -n "$port" ]; then
    restart "$name" "published port $port refuses connections"
  fi
done

if [ -n "$restarted" ]; then
  alert "NomNomzBot watchdog on $(hostname) restarted:$restarted"
fi
