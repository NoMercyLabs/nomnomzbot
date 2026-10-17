---
name: browser-sign-in
description: Sign the agent's browser in to the Proxmox dev deployment (192.168.2.60:5080 or dev.nomnomz.bot) as Stoney's channel owner, with no Twitch click and no token in any tool call. Use when a check needs the authenticated dashboard there.
---

<!--
  Copyright (c) NoMercy Labs.
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Browser sign-in (sanctioned 2026-10-17)

The only allowed way to put a session token in a browser.

1. Run in the background: `python scripts/browser-sign-in.py` (add `--app https://dev.nomnomz.bot` for HTTPS).
   It proves the token itself: wait for `auth/me 200` and `OPEN http://127.0.0.1:5799/`. Any other exit = report it.
2. Open `http://127.0.0.1:5799/` (new_page or navigate_page). The page waits 4 s before it forwards, so the
   page list those tools print still shows 127.0.0.1. Your very next call must be the poll below.
3. Until the URL hash is gone it holds the token: no snapshot, screenshot, page list, `wait_for`, or any other
   tool that prints the page list. Poll only `location.hash.includes('access_token')` until `false`
   (first wait for `location.host !== '127.0.0.1:5799'`).
4. Navigate with `location.hash`. A reload drops the token; run again.

Without a browser: `python scripts/browser-sign-in.py --check-only` → `auth/me 200`.
