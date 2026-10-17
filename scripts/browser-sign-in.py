#!/usr/bin/env python3
# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
"""Signs an agent's browser in to the Proxmox dev deployment as the channel owner.

Sanctioned by Stoney on 2026-10-17 ("a sanctioned tool ... it needs to work on the docker container on
proxmox"). It reads Jwt__Secret from the live api container and the owner ids from Postgres over SSH,
mints a short token with the real session id, proves it on /auth/me, then serves a one-shot page on
127.0.0.1. The agent opens that page; it forwards to <app>/app#access_token=..., which the app consumes
and strips on boot. The secret and the token are never printed, written to disk or put in a tool call.
Only the hosts in ALLOWED_APPS are accepted.

Usage:
  python scripts/browser-sign-in.py [--app http://192.168.2.60:5080] [--channel stoney_eagle]
                                    [--minutes 120] [--port 5799] [--check-only]
"""

import argparse
import http.server
import json
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ALLOWED_APPS = {"192.168.2.60", "dev.nomnomz.bot", "vue.nomnomz.bot"}
SSH = [
    "ssh", "-i", str(Path.home() / ".ssh" / "docker_proxmox"),
    "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes", "root@192.168.2.60",
]
MINT = Path(__file__).with_name("mint-jwt.py")
FORWARD_DELAY_MS = 4000


def remote(command: str) -> str:
    result = subprocess.run([*SSH, command], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"browser-sign-in: ssh to Proxmox failed (exit {result.returncode})")
    return result.stdout.strip()


def live_secret() -> str:
    # Blue/green: whichever api container is running holds the signing secret.
    secret = remote(
        "for c in nomnomzbot-api-green nomnomzbot-api-blue; do "
        "docker exec $c printenv Jwt__Secret 2>/dev/null && break; done"
    )
    if not secret:
        sys.exit("browser-sign-in: no running api container answered with Jwt__Secret")
    return secret


def owner_ids(channel: str) -> tuple[str, str, str]:
    if not channel.replace("_", "").isalnum():
        sys.exit("browser-sign-in: a channel name is letters, digits and _ only")
    sql = (
        'select c."OwnerUserId" || chr(32) || c."Id" || chr(32) || s."Id" from "Channels" c '
        'join "AuthSessions" s on s."UserId" = c."OwnerUserId" and s."RevokedAt" is null '
        f"where lower(c.\"Name\") = '{channel.lower()}' order by s.\"CreatedAt\" desc limit 1"
    )
    row = remote(f"docker exec nomnomzbot-postgres psql -U nomnomzbot -d nomnomzbot -tAc {json.dumps(sql)}")
    parts = row.split()
    if len(parts) != 3:
        sys.exit(f"browser-sign-in: no channel {channel} with an open owner session; sign in once by hand")
    return parts[0], parts[1], parts[2]


def mint(secret: str, sub: str, tenant: str, sid: str, channel: str, minutes: int) -> str:
    result = subprocess.run(
        [sys.executable, str(MINT), "--secret", secret, "--sub", sub, "--tenant", tenant,
         "--sid", sid, "--username", channel, "--roles", "user,admin", "--minutes", str(minutes)],
        capture_output=True, text=True, check=True,
    )
    return result.stdout.strip()


def prove(app: str, token: str) -> None:
    request = urllib.request.Request(f"{app}/api/v1/auth/me", headers={
        "Authorization": f"Bearer {token}",
        # Cloudflare (vue.nomnomz.bot) answers 403 to the default Python-urllib agent.
        "User-Agent": "nomnomzbot-browser-sign-in",
    })
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            json.load(response)
    except urllib.error.HTTPError as error:
        sys.exit(f"browser-sign-in: auth/me answered {error.code}; the token is not accepted")
    except urllib.error.URLError as error:
        sys.exit(f"browser-sign-in: {app} is not reachable ({error.reason})")
    print(f"auth/me 200 at {app}")


def serve_once(port: int, target: str) -> None:
    page = (
        "<!doctype html><title>sign-in handoff</title><p>Signing in...</p>"
        # The delay lets new_page/navigate_page return (they print the page list) while the address is still
        # this harmless page; forwarding at once leaked the token URL into a transcript on 2026-10-17.
        f"<script>setTimeout(function () {{ location.replace({json.dumps(target)}); }}, {FORWARD_DELAY_MS})</script>"
    ).encode("utf-8")

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(page)))
            self.end_headers()
            self.wfile.write(page)

        def log_message(self, *args: object) -> None:
            pass

    server = http.server.HTTPServer(("127.0.0.1", port), Handler)
    print(f"OPEN http://127.0.0.1:{port}/  (one request, then this exits)", flush=True)
    server.handle_request()
    print("handoff served")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", default="http://192.168.2.60:5080")
    parser.add_argument("--channel", default="stoney_eagle")
    parser.add_argument("--minutes", type=int, default=120)
    parser.add_argument("--port", type=int, default=5799)
    parser.add_argument("--check-only", action="store_true", help="mint and prove, but serve nothing")
    args = parser.parse_args()

    app = args.app.rstrip("/")
    if urllib.parse.urlsplit(app).hostname not in ALLOWED_APPS:
        sys.exit(f"browser-sign-in: {app} is not an allowed host ({', '.join(sorted(ALLOWED_APPS))})")

    sub, tenant, sid = owner_ids(args.channel)
    token = mint(live_secret(), sub, tenant, sid, args.channel, args.minutes)
    prove(app, token)

    if args.check_only:
        return
    serve_once(args.port, f"{app}/app#access_token={token}&expires_in={args.minutes * 60}")


if __name__ == "__main__":
    main()
