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
# NomNomzBot quickstart (Linux / macOS). Two ways to run:
#
#   ./deploy.sh          self_host_full — Docker stack (Postgres + Redis + API + Adminer)
#   ./deploy.sh --lite   self_host_lite — single-file binary, no Docker, no Postgres/Redis
#
# Idempotent: re-run any time. The full path copies .env.example -> .env on first run and
# brings the stack up; the lite path publishes the self-contained ./nomnomz binary.

set -euo pipefail

cd "$(dirname "$0")"

LITE=0
for arg in "$@"; do
  case "$arg" in
    --lite) LITE=1 ;;
    -h|--help)
      echo "Usage: ./deploy.sh [--lite]"
      echo "  (no flag)  Docker full stack — Postgres + Redis + API + Adminer"
      echo "  --lite     Publish the single-file binary (no Docker)"
      exit 0
      ;;
    *) echo "Unknown option: $arg (try --help)" >&2; exit 2 ;;
  esac
done

# --- lite: publish the single self-contained binary --------------------------
if [ "$LITE" -eq 1 ]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: the .NET SDK is required to build the lite binary. Install .NET 10 from https://dot.net" >&2
    exit 1
  fi

  # Map the host OS/arch to a .NET runtime identifier.
  case "$(uname -s)" in
    Linux)  os="linux" ;;
    Darwin) os="osx" ;;
    *) echo "ERROR: unsupported OS for the lite binary: $(uname -s)" >&2; exit 1 ;;
  esac
  case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) echo "ERROR: unsupported architecture for the lite binary: $(uname -m)" >&2; exit 1 ;;
  esac
  rid="${os}-${arch}"

  echo "Publishing the self_host_lite single-file binary for ${rid}..."
  dotnet publish server/src/NomNomzBot.Api -c Release -r "$rid" --self-contained true

  out="server/src/NomNomzBot.Api/bin/Release/net10.0/${rid}/publish/nomnomz"
  echo
  echo "Done. Your single-file bot is at:"
  echo "  ${out}"
  echo "Run it from any folder (it creates ./nomnomz.db beside itself on first start):"
  echo "  cp \"${out}\" ./nomnomz && ./nomnomz"
  exit 0
fi

# --- full: the Docker stack --------------------------------------------------
if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: Docker is required for the full stack. Install it from https://docs.docker.com/get-docker/" >&2
  echo "       (or run './deploy.sh --lite' for the no-Docker single-file binary.)" >&2
  exit 1
fi

if [ ! -f .env ]; then
  echo "No .env found — creating one from .env.example."
  cp .env.example .env
  echo
  echo "  >> Edit .env and set TWITCH_CLIENT_ID, TWITCH_CLIENT_SECRET and TWITCH_BOT_USERNAME,"
  echo "     then re-run ./deploy.sh. Generate secrets with: openssl rand -base64 32"
  exit 0
fi

echo "Building and starting the NomNomzBot stack..."
docker compose up -d --build

echo
echo "Stack is up. Once the API reports ready:"
echo "  Dashboard / API : ${API_BASE_URL:-http://localhost:5080}"
echo "  API docs        : ${API_BASE_URL:-http://localhost:5080}/scalar"
echo "  Health          : ${API_BASE_URL:-http://localhost:5080}/health"
echo "  Adminer (DB)    : http://localhost:${ADMINER_PORT:-8082}"
echo
echo "Follow startup with: docker compose logs -f api"
