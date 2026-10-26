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

# Writes <channel>.env once: new random database, Redis, JWT and encryption secrets for this channel alone, the
# channel's public address, and the platform app keys copied from the main stack's .env (one directory up). It
# refuses to overwrite an existing file: a new ENCRYPTION_KEY would make every stored token unreadable. No value is
# printed.
#
#   ./init-channel.sh stable
set -euo pipefail

channel="${1:-}"
case "$channel" in
  stable) host="https://nomnomz.bot" ;;
  *) echo "usage: $0 stable  (one hosted bot only: a second stack on the same Twitch app doubles the bot in chat)" >&2; exit 2 ;;
esac

cd "$(dirname "$0")"
out="${channel}.env"
main="../.env"

[ -e "$out" ] && { echo "${out} exists; keeping it" >&2; exit 0; }
[ -f "$main" ] || { echo "${main} is missing" >&2; exit 1; }

copy() { grep -E "^$1=" "$main" | head -1 || true; }
secret() { openssl rand -hex "$1"; }

umask 077
{
  echo "CHANNEL=${channel}"
  echo "API_BASE_URL=${host}"
  echo "DEPLOYMENT_MODE=self_host_full"
  echo "POSTGRES_PASSWORD=$(secret 24)"
  echo "REDIS_PASSWORD=$(secret 24)"
  echo "JWT_SECRET=$(secret 48)"
  echo "ENCRYPTION_KEY=$(openssl rand -base64 32)"
  for key in TWITCH_CLIENT_ID TWITCH_CLIENT_SECRET TWITCH_BOT_USERNAME SPOTIFY_CLIENT_ID SPOTIFY_CLIENT_SECRET \
    DISCORD_CLIENT_ID DISCORD_CLIENT_SECRET INITIAL_ADMIN_TWITCH_ID; do
    copy "$key"
  done
} > "$out"

echo "${out} written ($(grep -c = "$out") keys)"
