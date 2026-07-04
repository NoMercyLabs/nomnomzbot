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
#
# NomNomzBot deploy (Linux / macOS) — one script, three scenarios. Full guide: DEPLOY.md
#
#   ./deploy.sh desktop        single-file bot on this machine — no Docker, SQLite
#   ./deploy.sh docker         full stack in Docker — Postgres + Redis + API (+ Adminer)
#   ./deploy.sh saas           the Docker stack in multi-tenant SaaS mode
#   ./deploy.sh <any> --app    ALSO build the standalone desktop dashboard installer
#
# Idempotent: re-run any time. The web dashboard is bundled into every backend
# artifact automatically — after any scenario, open the API URL in a browser.

set -euo pipefail

cd "$(dirname "$0")"

# --- helpers ------------------------------------------------------------------

die() { echo "ERROR: $*" >&2; exit 1; }

guide() {
  cat <<'EOF'
NomNomzBot deploy — pick a scenario (full guide: DEPLOY.md)

  ./deploy.sh desktop   Run the bot on THIS machine as one single file — no Docker,
                        SQLite, zero dependencies. Best for: one streamer, a PC/NUC.
  ./deploy.sh docker    Full stack in Docker: Postgres + Redis + API (+ Adminer).
                        Best for: a home server, database durability, room to grow.
  ./deploy.sh saas      The same Docker stack in multi-tenant SaaS mode, behind
                        YOUR HTTPS reverse proxy.
                        RESTRICTED: hosting NomNomzBot as a service for others is
                        against the project license (reserved to NoMercy Labs).
                        Self-hosting your own bot is always free and unrestricted.

Dashboard (both work in every scenario):
  web app     nothing to build — the bot serves it; open the API URL in a browser.
  --app       also build the standalone desktop dashboard installer for THIS OS.

Example: ./deploy.sh desktop --app
EOF
}

# Read KEY from .env (empty when absent).
env_get() { grep -E "^$1=" .env 2>/dev/null | head -n 1 | cut -d= -f2- || true; }

# Replace or append KEY=VALUE in .env.
env_set() {
  local key="$1" value="$2"
  if grep -qE "^${key}=" .env; then
    awk -v k="$key" -v v="$value" 'index($0, k"=") == 1 {print k"="v; next} {print}' .env > .env.tmp \
      && mv .env.tmp .env
  else
    printf '%s=%s\n' "$key" "$value" >> .env
  fi
}

rand_base64() {
  if command -v openssl >/dev/null 2>&1; then openssl rand -base64 32; else head -c 32 /dev/urandom | base64; fi
}

rand_hex() {
  if command -v openssl >/dev/null 2>&1; then openssl rand -hex 24; else od -An -tx1 -N24 /dev/urandom | tr -d ' \n'; fi
}

# Create .env from the template with real generated secrets; prompt for Twitch
# credentials when a terminal is attached (Enter = skip, use the dashboard wizard).
ensure_env() {
  [ -f .env ] && return 0

  echo "No .env found — creating one from .env.example with freshly generated secrets."
  cp .env.example .env
  env_set JWT_SECRET "$(rand_base64)"
  env_set ENCRYPTION_KEY "$(rand_base64)"
  env_set POSTGRES_PASSWORD "$(rand_hex)"

  if [ -t 0 ]; then
    echo
    echo "Twitch app credentials (https://dev.twitch.tv/console/apps)."
    echo "Press Enter to skip any value — you can also enter them later in the dashboard's setup wizard."
    read -r -p "  TWITCH_CLIENT_ID     : " tw_id || true
    read -r -p "  TWITCH_CLIENT_SECRET : " tw_secret || true
    read -r -p "  TWITCH_BOT_USERNAME  : " tw_bot || true
    [ -n "${tw_id:-}" ]     && env_set TWITCH_CLIENT_ID "$tw_id"
    [ -n "${tw_secret:-}" ] && env_set TWITCH_CLIENT_SECRET "$tw_secret"
    [ -n "${tw_bot:-}" ]    && env_set TWITCH_BOT_USERNAME "$tw_bot"
  else
    echo
    echo "  >> .env created (secrets generated). Edit it to set TWITCH_CLIENT_ID,"
    echo "     TWITCH_CLIENT_SECRET and TWITCH_BOT_USERNAME — or leave them blank and"
    echo "     use the dashboard's setup wizard — then re-run this script."
    exit 0
  fi
}

# Bring the compose stack up (pull the published image when configured, build otherwise),
# then block until /health/ready goes green.
compose_up_and_wait() {
  local api_image port base_url
  api_image="$(env_get API_IMAGE)"
  port="$(env_get API_HTTP_PORT)"; port="${port:-5080}"
  base_url="$(env_get API_BASE_URL)"; base_url="${base_url:-http://localhost:${port}}"

  if [ -n "$api_image" ] && [ "$api_image" != "nomnomzbot-api:local" ]; then
    echo "Pulling the published image ($api_image)..."
    docker compose pull api
    docker compose up -d --no-build
  else
    echo "Building the image locally (includes the web dashboard) and starting the stack..."
    docker compose up -d --build
  fi

  printf 'Waiting for the API to report ready (this includes first-run migrations)'
  local i
  for i in $(seq 1 60); do
    if curl -fsS "http://localhost:${port}/health/ready" >/dev/null 2>&1; then
      echo " ready."
      echo
      echo "Stack is up:"
      echo "  Dashboard (web) : ${base_url}"
      echo "  Health          : ${base_url}/health"
      echo "  Adminer (DB)    : http://localhost:$(env_get ADMINER_PORT | grep . || echo 8082)"
      echo
      echo "Point the desktop dashboard app at ${base_url}, or just use the web dashboard."
      return 0
    fi
    printf '.'
    sleep 3
  done

  echo
  die "the API did not become ready within 3 minutes — inspect it with: docker compose logs -f api"
}

# --- scenario: desktop (self_host_lite — single-file binary) -------------------

scenario_desktop() {
  command -v dotnet >/dev/null 2>&1 \
    || die "the .NET SDK is required to build the desktop binary. Install .NET 10 from https://dot.net"

  local os arch rid out
  case "$(uname -s)" in
    Linux)  os="linux" ;;
    Darwin) os="osx" ;;
    *) die "unsupported OS for the desktop binary: $(uname -s)" ;;
  esac
  case "$(uname -m)" in
    x86_64|amd64)  arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) die "unsupported architecture for the desktop binary: $(uname -m)" ;;
  esac
  rid="${os}-${arch}"

  echo "Publishing the single-file bot (self_host_lite) for ${rid} — the web dashboard is bundled in..."
  dotnet publish server/src/NomNomzBot.Api -c Release -r "$rid" --self-contained true

  local data_dir
  case "$os" in
    linux) data_dir="~/.local/share/NomNomzBot" ;;
    osx)   data_dir="~/Library/Application Support/NomNomzBot" ;;
  esac

  out="server/src/NomNomzBot.Api/bin/Release/net10.0/${rid}/publish/nomnomz"
  echo
  echo "Done. Your single-file bot is at:"
  echo "  ${out}"
  echo "Run it from any folder:"
  echo "  cp \"${out}\" ./nomnomz && ./nomnomz"
  echo "Its data (SQLite DB, keys, logs) lives in ${data_dir} — override with NOMNOMZ_DATA_DIR."
  echo "Then open the web dashboard at http://localhost:5080 — or use the desktop app (--app)."
}

# --- scenario: docker (self_host_full — compose stack) --------------------------

scenario_docker() {
  command -v docker >/dev/null 2>&1 \
    || die "Docker is required for this scenario. Install it from https://docs.docker.com/get-docker/ (or run './deploy.sh desktop' for the no-Docker single-file bot)."
  ensure_env

  local mode
  mode="$(env_get DEPLOYMENT_MODE)"
  if [ -n "$mode" ] && [ "$mode" != "self_host_full" ]; then
    echo "Note: .env has DEPLOYMENT_MODE=${mode} — resetting it to self_host_full for this scenario."
    env_set DEPLOYMENT_MODE self_host_full
  fi

  compose_up_and_wait
}

# --- scenario: saas (multi-tenant fleet mode) -----------------------------------

scenario_saas() {
  command -v docker >/dev/null 2>&1 \
    || die "Docker is required for this scenario. Install it from https://docs.docker.com/get-docker/"
  ensure_env

  # Fail-closed guards — SaaS is public and multi-tenant, so weak or local values are refused.
  local base_url jwt enc
  base_url="$(env_get API_BASE_URL)"
  case "$base_url" in
    https://*) : ;;
    *) die "SaaS requires API_BASE_URL in .env to be your public HTTPS origin (behind your reverse proxy) — currently '${base_url:-unset}'. See DEPLOY.md (SaaS)." ;;
  esac
  case "$base_url" in
    *localhost*|*127.0.0.1*) die "SaaS requires a public API_BASE_URL — '${base_url}' points at this machine. See DEPLOY.md (SaaS)." ;;
  esac
  jwt="$(env_get JWT_SECRET)"
  [ "$jwt" = 'dev-secret-key-at-least-32-characters-long!!' ] \
    && die "SaaS refuses the dev JWT_SECRET. Set a strong one in .env: openssl rand -base64 32"
  enc="$(env_get ENCRYPTION_KEY)"
  [ "$enc" = 'ZGV2LWVuY3J5cHRpb24ta2V5LWZvci1sb2NhbC1kZXY=' ] \
    && die "SaaS refuses the dev ENCRYPTION_KEY. Set a strong one in .env: openssl rand -base64 32 (changing it later invalidates all stored OAuth tokens)"

  if [ "$(env_get DEPLOYMENT_MODE)" != "saas" ]; then
    echo "Setting DEPLOYMENT_MODE=saas in .env."
    env_set DEPLOYMENT_MODE saas
  fi

  echo "RESTRICTED OPTION: operating NomNomzBot as a hosted service for third parties is against"
  echo "the project license — that right is reserved to NoMercy Labs. Self-hosting your own bot"
  echo "(desktop/docker) is always free and unrestricted. See DEPLOY.md (SaaS)."
  echo
  echo "SaaS mode: TLS terminates at YOUR reverse proxy; set TRUSTED_PROXY_NETWORKS in .env if the"
  echo "proxy reaches the API over a docker network (e.g. 172.16.0.0/12). Scale-out guidance: DEPLOY.md."
  compose_up_and_wait
}

# --- standalone desktop dashboard app (optional, any scenario) ------------------

build_desktop_app() {
  command -v java >/dev/null 2>&1 \
    || die "a JDK (21 recommended) is required to build the desktop dashboard app — https://adoptium.net"

  echo
  echo "Building the standalone desktop dashboard installer for this OS..."
  (cd app && ./gradlew :composeApp:packageDistributionForCurrentOS --console=plain)

  echo
  echo "Desktop app installer(s) at:"
  echo "  app/composeApp/build/compose/binaries/main/"
  echo "Install it, launch it, and point it at your bot's URL (it also finds LAN bots automatically)."
}

# --- argument parsing -----------------------------------------------------------

SCENARIO=""
BUILD_APP=0
for arg in "$@"; do
  case "$arg" in
    desktop|docker|saas)
      [ -n "$SCENARIO" ] && die "only one scenario at a time (got '$SCENARIO' and '$arg')"
      SCENARIO="$arg"
      ;;
    --lite) SCENARIO="desktop" ;;   # legacy alias
    --app)  BUILD_APP=1 ;;
    -h|--help) guide; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; echo >&2; guide >&2; exit 2 ;;
  esac
done

if [ -z "$SCENARIO" ]; then
  guide
  exit 0
fi

case "$SCENARIO" in
  desktop) scenario_desktop ;;
  docker)  scenario_docker ;;
  saas)    scenario_saas ;;
esac

[ "$BUILD_APP" -eq 1 ] && build_desktop_app

exit 0
