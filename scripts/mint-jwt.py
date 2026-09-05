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
"""Mints an HS256 access JWT matching JwtTokenService.GenerateAccessToken's claim
shape, for driving a deployed instance as an authenticated human/E2E check without
going through the real Twitch device-code flow. stdlib only (hashlib/hmac/base64),
so it runs anywhere Python 3 does without installing PyJWT.

WHERE THE SECRET ACTUALLY COMES FROM — this costs an hour every time it is rediscovered:
a self-host instance does NOT sign with the appsettings.json placeholder. Program.cs treats a weak or
default Jwt:Secret as absent and calls SelfHostSecretStore.LoadOrCreateJwtSecret(), which generates a
per-install value and persists it at <data-dir>/keys/jwt-secret.bin. Read the secret from THAT file,
not from appsettings — on Linux the file is plaintext, on Windows it is DPAPI-sealed. The data dir is
NOMNOMZ_DATA_DIR when set, otherwise the machine app-data dir. A mismatch shows up as
401 "The signature key was not found", never as a clearer message.

Usage:
  python3 scripts/mint-jwt.py --secret <base64 Jwt__Secret> --sub <userId guid>
      [--tenant <broadcasterId guid>] [--issuer nomnomzbot] [--audience nomnomzbot]
      [--username someone] [--minutes 60]
"""

import argparse
import base64
import hashlib
import hmac
import json
import time
import uuid


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--secret",
        required=True,
        help="Jwt__Secret, used as its raw UTF-8 bytes — JwtTokenService.BuildSymmetric does "
        "Encoding.UTF8.GetBytes(secret), NOT base64-decode, even though the value itself is "
        "usually base64-shaped (openssl rand -base64 32) — that's just how it was generated.",
    )
    parser.add_argument("--sub", required=True, help="userId GUID -> ClaimTypes.NameIdentifier")
    parser.add_argument("--tenant", help="broadcasterId GUID -> the 'tenant' claim")
    parser.add_argument("--issuer", default="nomnomzbot")
    parser.add_argument("--audience", default="nomnomzbot")
    parser.add_argument("--username", default="mint-jwt")
    parser.add_argument("--minutes", type=int, default=60)
    parser.add_argument(
        "--sid",
        help="AuthSessions.Id to put in the 'sid' claim. Defaults to a random GUID, which is fine for "
        "endpoints that only read claims but is rejected by anything validating the session still "
        "exists and is unrevoked — use a real row's id to exercise those.",
    )
    parser.add_argument(
        "--roles",
        default="",
        help="Comma-separated roles -> ClaimTypes.Role claims. SessionService.RolesFor issues "
        "'user,admin' for a platform principal and 'user' otherwise, and several endpoints gate on "
        "User.IsInRole(\"admin\") (e.g. the bot device flow once setup is complete), so a token "
        "without this cannot exercise those paths.",
    )
    args = parser.parse_args()

    # JwtTokenService builds the token from System.Security.Claims.ClaimTypes constants directly
    # (JwtSecurityToken's ctor writes Claim.Type verbatim as the JSON key — no short-name mapping),
    # so the wire claim keys are these long XML-identity URIs, not "sub"/"name"/"role". ClaimTypes.Role
    # is the ONE exception to the xmlsoap.org family below — it resolves to a microsoft.com URI, and a
    # token minted with the xmlsoap.org value there passes JWT validation but User.IsInRole(...) never
    # matches it, so every admin-gated policy silently 403s (confirmed against JwtTokenService.cs:117,
    # which signs roles with ClaimTypes.Role verbatim).
    NAME_IDENTIFIER = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    NAME = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"
    ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"

    now = int(time.time())
    header = {"alg": "HS256", "typ": "JWT"}
    claims = {
        NAME_IDENTIFIER: args.sub,
        NAME: args.username,
        "sid": args.sid or str(uuid.uuid4()),
        "jti": str(uuid.uuid4()),
        "iat": now,
        "iss": args.issuer,
        "aud": args.audience,
        "exp": now + args.minutes * 60,
    }
    if args.tenant:
        claims["tenant"] = args.tenant

    roles = [r.strip() for r in args.roles.split(",") if r.strip()]
    if roles:
        # A single role must be a bare string, not a one-element list: JwtSecurityToken writes one claim
        # per value and ASP.NET reads either shape, but a list of one serialises differently enough to
        # trip role checks on some stacks. Match what the real issuer emits.
        claims[ROLE] = roles[0] if len(roles) == 1 else roles

    signing_input = f"{b64url(json.dumps(header, separators=(',', ':')).encode())}.{b64url(json.dumps(claims, separators=(',', ':')).encode())}"
    key = args.secret.encode("utf-8")
    signature = hmac.new(key, signing_input.encode("ascii"), hashlib.sha256).digest()
    print(f"{signing_input}.{b64url(signature)}")


if __name__ == "__main__":
    main()
