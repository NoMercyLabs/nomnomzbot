#!/usr/bin/env sh
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
# start.sh — one command to run the bot + dashboard from source, in DEVELOPMENT
# mode with hot reload. No Docker, no Postgres, no Redis. Starts two processes:
#
#   1. the API  (dotnet run, ASPNETCORE_ENVIRONMENT=Development) — SQLite,
#      self-contained "SelfHostLite" profile, /scalar docs enabled.
#   2. the dashboard (Kotlin/Wasm webpack dev server, --watch-fs -t) — same
#      Compose UI as the desktop app, hot-reloading in the browser as Kotlin
#      source changes. NOT the ~25-minute production bundle.
#
# For an installable build instead of a dev inner loop, see deploy.sh/DEPLOY.md.
#
# Usage:
#   ./start.sh
#   NOMNOMZ_DATA_DIR=/some/path ./start.sh   use a specific SQLite data dir
#
# Stop with Ctrl+C — both processes are stopped together, nothing is left running.

set -eu

cd "$(dirname "$0")"
REPO_ROOT="$(pwd)"

say() { printf '%s\n' "$*"; }
die() { printf '\nERROR: %s\n\n' "$*" >&2; exit 1; }

# --- 1. check what needs to be installed, and say exactly what's missing ---

# .NET SDK — the version this repo actually needs, read from server/global.json
# (never guessed) so this check can't drift from what the repo requires.
GLOBAL_JSON="server/global.json"
[ -f "$GLOBAL_JSON" ] || die "Can't find $GLOBAL_JSON — run this script from the repo root."
NEEDED_DOTNET_VERSION=$(grep -o '"version"[^,}]*' "$GLOBAL_JSON" | head -1 | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')
NEEDED_DOTNET_MAJOR=$(printf '%s' "$NEEDED_DOTNET_VERSION" | cut -d. -f1)

if ! command -v dotnet >/dev/null 2>&1; then
  die "The .NET ${NEEDED_DOTNET_MAJOR} SDK is required to run the bot.
Install it from: https://dot.net/download (get the SDK, not just the runtime)."
fi

INSTALLED_DOTNET_VERSION=$(dotnet --version 2>/dev/null || true)
INSTALLED_DOTNET_MAJOR=$(printf '%s' "$INSTALLED_DOTNET_VERSION" | cut -d. -f1)
if [ "$INSTALLED_DOTNET_MAJOR" != "$NEEDED_DOTNET_MAJOR" ]; then
  die "This repo needs .NET ${NEEDED_DOTNET_MAJOR}.x (server/global.json), but 'dotnet --version' reports ${INSTALLED_DOTNET_VERSION:-nothing installed}.
Install .NET ${NEEDED_DOTNET_MAJOR} from: https://dot.net/download"
fi
say "OK  .NET SDK ${INSTALLED_DOTNET_VERSION} (repo needs ${NEEDED_DOTNET_MAJOR}.x)"

# JDK — Gradle (the dashboard's build tool) needs one to run the webpack dev server.
if ! command -v java >/dev/null 2>&1; then
  die "A JDK is required to run the dashboard (it's built with Gradle/Kotlin).
Install a JDK 21 from: https://adoptium.net"
fi
say "OK  Java found ($(command -v java))"

# --- 2. ports ----------------------------------------------------------------
#
# The API runs on its committed, documented default — 5080. The dashboard dev
# server listens on 5173 (app/composeApp/build.gradle.kts) — that's the URL to
# open in a browser. The dashboard's own dev-only webpack proxy
# (app/composeApp/webpack.config.d/proxy.js) forwards /api + /hubs from 5173
# through to the API on 5080 by default, so the browser only ever talks to one
# origin and no env var is needed here.

DASHBOARD_PORT=5173
API_PORT=5080

# --- 3. start the API (Development, SelfHostLite/SQLite) --------------------

say "Starting the API (dotnet run, Development, SQLite)..."
(
  cd "$REPO_ROOT/server/src/NomNomzBot.Api"
  ASPNETCORE_ENVIRONMENT=Development \
  Deployment__Mode=self_host_lite \
  exec dotnet run --no-launch-profile -- --urls "http://localhost:${API_PORT}"
) &
API_PID=$!

# --- 4. start the dashboard dev server (hot reload) --------------------------

say "Starting the dashboard dev server (Kotlin/Wasm, hot reload — first start can"
say "take a few minutes while Gradle compiles)..."
GRADLEW="$REPO_ROOT/app/gradlew"
[ -x "$GRADLEW" ] || GRADLEW="sh $REPO_ROOT/app/gradlew"
(
  cd "$REPO_ROOT/app"
  exec $GRADLEW --no-daemon :composeApp:wasmJsBrowserDevelopmentRun --watch-fs -t --console=plain
) &
WEB_PID=$!

# --- 5. clean shutdown on Ctrl+C ---------------------------------------------

cleanup() {
  say ""
  say "Stopping..."
  kill "$API_PID" 2>/dev/null || true
  kill "$WEB_PID" 2>/dev/null || true
  wait "$API_PID" 2>/dev/null || true
  wait "$WEB_PID" 2>/dev/null || true
  say "Stopped."
}
trap cleanup INT TERM

# --- 6. wait for the API to report healthy, then print the useful URLs ------

printf 'Waiting for the API to come up'
API_READY=0
i=0
while [ $i -lt 120 ]; do
  if curl -fsS "http://localhost:${API_PORT}/health/ready" >/dev/null 2>&1; then
    API_READY=1
    break
  fi
  printf '.'
  i=$((i + 1))
  sleep 1
done
say ""

if [ "$API_READY" != "1" ]; then
  say "The API did not report healthy within 2 minutes. It may still be starting —"
  say "check the output above for errors. Leaving both processes running; Ctrl+C to stop."
else
  say "API is up."
fi

say ""
say "--------------------------------------------------------------------------"
say " Dashboard (open this)  : http://localhost:${DASHBOARD_PORT}"
say " API                    : http://localhost:${API_PORT}"
say " API docs (Scalar)      : http://localhost:${API_PORT}/scalar"
say " API health             : http://localhost:${API_PORT}/health"
say " Twitch redirect URL    : register this in the Twitch Developer Console if"
say "                           it isn't there already:"
say "                           http://localhost:${DASHBOARD_PORT}/api/v1/auth/twitch/callback"
say "--------------------------------------------------------------------------"
say " The dashboard needs a little longer to finish its first compile before"
say " http://localhost:${DASHBOARD_PORT} responds — refresh once it does."
say " Press Ctrl+C to stop both the API and the dashboard."
say "--------------------------------------------------------------------------"
say ""

wait "$API_PID" "$WEB_PID"
