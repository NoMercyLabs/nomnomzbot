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

FIX="$fix" PROC_ROOT="$fix/proc" PATH="$fix/bin:$PATH" bash "$here/stack-watchdog.sh" > "$fix/out" 2>&1

expected=$'nomnomzbot-redis\nnomnomzbot-caddy\nnomnomzbot-cloudflared'
actual=$(cat "$fix/restarts" 2>/dev/null)
if [ "$actual" = "$expected" ]; then
  echo "PASS: restarted the dead process, the lost port and the reused pid; left the healthy and the starting container alone"
  exit 0
fi
echo "FAIL"
echo "expected restarts:"; echo "$expected"
echo "actual restarts:";   echo "$actual"
echo "watchdog output:";   cat "$fix/out"
exit 1
