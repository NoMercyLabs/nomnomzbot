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
# stack-watchdog.test.sh - runs stack-watchdog.sh against a fake `docker` and a fake /proc, and
# asserts exactly which containers it restarts.   bash scripts/stack-watchdog.test.sh

set -u
here=$(cd "$(dirname "$0")" && pwd)
fix=$(mktemp -d)
trap 'rm -rf "$fix"' EXIT
mkdir -p "$fix/bin" "$fix/proc"

cat > "$fix/bin/docker" <<'STUB'
#!/usr/bin/env bash
name="${!#}"
case "$1" in
  ps) cat "$FIX/ps" ;;
  inspect) cat "$FIX/$name.inspect" ;;
  port) cat "$FIX/$name.port" 2>/dev/null ;;
  restart) echo "$name" >> "$FIX/restarts" ;;
esac
STUB
chmod +x "$fix/bin/docker"

old="2026-01-01T00:00:00.000000000Z"
young=$(date -u +%Y-%m-%dT%H:%M:%S.000000000Z)

# add_container <name> <id> <pid> <startedAt> <process alive: yes|no> [published host port]
add_container() {
  echo "$1" >> "$fix/ps"
  echo "$2 $3 $4" > "$fix/$1.inspect"
  if [ "$5" = "yes" ]; then
    mkdir -p "$fix/proc/$3"
    echo "0::/system.slice/docker-$2.scope" > "$fix/proc/$3/cgroup"
  fi
  if [ -n "${6:-}" ]; then echo "80/tcp -> 0.0.0.0:$6" > "$fix/$1.port"; fi
}

add_container nomnomzbot-postgres     aaa111 101 "$old"   yes
add_container nomnomzbot-redis        bbb222 102 "$old"   no
add_container nomnomzbot-caddy        ccc333 103 "$old"   yes 1
add_container nomnomzbot-api-green    ddd444 104 "$young" yes 1
# The pid exists again, but it belongs to another container: the old process is gone.
add_container nomnomzbot-cloudflared  eee555 101 "$old"   yes
echo "0::/system.slice/docker-aaa111.scope" > "$fix/proc/101/cgroup"

# A fake `curl` that records each call (URL, auth header, body) and answers like Discord.
cat > "$fix/bin/curl" <<'STUB'
#!/usr/bin/env bash
url="" header="" body=""
while [ $# -gt 0 ]; do
  case "$1" in
    -H) case "$2" in @*) header=$(cat "${2#@}") ;; esac; shift ;;
    -d) body="$2"; shift ;;
    http*) url="$1" ;;
  esac
  shift
done
echo "$url | $header | $body" >> "$FIX/curl.log"
case "$url" in */users/@me/channels) echo '{"id":"777","type":1,"recipients":[{"id":"4242"}]}' ;; esac
STUB
chmod +x "$fix/bin/curl"

run_watchdog() { # <alert config file>
  rm -f "$fix/restarts" "$fix/curl.log"
  FIX="$fix" PROC_ROOT="$fix/proc" WATCHDOG_ENV="$1" PATH="$fix/bin:$PATH" bash "$here/stack-watchdog.sh" > "$fix/out" 2>&1
}

fail() {
  echo "FAIL: $1"
  echo "restarts:";        cat "$fix/restarts" 2>/dev/null
  echo "curl calls:";      cat "$fix/curl.log" 2>/dev/null
  echo "watchdog output:"; cat "$fix/out"
  exit 1
}

# Run 1: no alert config. The watchdog restarts, and it sends nothing.
run_watchdog "$fix/missing.env"
expected=$'nomnomzbot-redis\nnomnomzbot-caddy\nnomnomzbot-cloudflared'
[ "$(cat "$fix/restarts" 2>/dev/null)" = "$expected" ] || fail "wrong restarts without an alert config"
[ ! -e "$fix/curl.log" ] || fail "sent a Discord call without an alert config"

# Run 2: with an alert config. Same restarts, plus one Discord DM that names all three.
printf 'DISCORD_BOT_TOKEN=test-token\nDISCORD_ALERT_USER_ID=4242\n' > "$fix/alert.env"
run_watchdog "$fix/alert.env"
[ "$(cat "$fix/restarts" 2>/dev/null)" = "$expected" ] || fail "wrong restarts with an alert config"
[ "$(wc -l < "$fix/curl.log")" -eq 2 ] || fail "expected 2 Discord calls (open the DM, send the message)"
open=$(sed -n 1p "$fix/curl.log")
send=$(sed -n 2p "$fix/curl.log")
case "$open" in
  "https://discord.com/api/v10/users/@me/channels | Authorization: Bot test-token | "*'"recipient_id":"4242"'*) ;;
  *) fail "the first call does not open a DM with user 4242 as the bot" ;;
esac
case "$send" in
  "https://discord.com/api/v10/channels/777/messages | Authorization: Bot test-token | "*) ;;
  *) fail "the second call does not post to DM channel 777 as the bot" ;;
esac
for name in nomnomzbot-redis nomnomzbot-caddy nomnomzbot-cloudflared; do
  case "$send" in *"$name"*) ;; *) fail "the DM does not name $name" ;; esac
done
case "$send" in *nomnomzbot-postgres*|*nomnomzbot-api-green*) fail "the DM names a container that was not restarted" ;; esac

echo "PASS: restarted the dead process, the lost port and the reused pid; left the healthy and the starting container alone; one Discord DM names the three restarts, and no config sends nothing"
