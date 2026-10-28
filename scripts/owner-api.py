#!/usr/bin/env python3
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
"""Calls the deployed API as the channel owner, for one request, and prints the answer.

It signs in the way browser-sign-in.py does (the token is minted here and never printed, never
passed as an argument and never written to a file). Only the hosts that script allows are used.

Usage:
  python scripts/owner-api.py GET /api/v1/channels/<channelId>/shoutout-overrides
  python scripts/owner-api.py PUT /api/v1/users/<userId>/profile --json '{"pronounId":0}'
"""
import argparse
import importlib.util
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

_spec = importlib.util.spec_from_file_location(
    "browser_sign_in", Path(__file__).with_name("browser-sign-in.py")
)
sign_in = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sign_in)


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("method", choices=["GET", "PUT", "POST", "DELETE"])
    parser.add_argument("path")
    parser.add_argument("--json", dest="body")
    parser.add_argument("--app", default="http://192.168.2.60:5080")
    parser.add_argument("--channel", default="stoney_eagle")
    args = parser.parse_args()

    app = args.app.rstrip("/")
    if urllib.parse.urlsplit(app).hostname not in sign_in.ALLOWED_APPS:
        sys.exit(f"owner-api: {app} is not an allowed host")
    if not args.path.startswith("/api/"):
        sys.exit("owner-api: the path starts with /api/")

    sub, tenant, sid = sign_in.owner_ids(args.channel)
    token = sign_in.mint(sign_in.live_secret(), sub, tenant, sid, args.channel, 10)

    data = args.body.encode() if args.body else None
    request = urllib.request.Request(
        f"{app}{args.path}",
        data=data,
        method=args.method,
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "User-Agent": "nomnomzbot-owner-api",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            print(response.status, response.read().decode()[:400000])
    except urllib.error.HTTPError as error:
        print(error.code, error.read().decode()[:2000])
        sys.exit(1)


if __name__ == "__main__":
    main()
