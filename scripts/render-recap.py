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
"""The only way to make a recap image, and it refuses to write one that breaks the spec.

Steps: read the stored recap from the box, render the committed raid_recap.vue with exactly that payload in
headless Chrome, check the rendered page against .claude/docs/design/spec/raid-recap.md section 4.1, and write
the PNG only when every check passes. Otherwise it prints each failed check and exits 1.

  python scripts/render-recap.py --out recap.png                    newest stored recap
  python scripts/render-recap.py --stream <streamId> --out recap.png
  python scripts/render-recap.py --stream <streamId> --rebuild --out recap.png    POST a rebuild first
  python scripts/render-recap.py --payload-file recap.json --out recap.png        offline (used by the tests)

The owner token is minted inside this process (ssh reads the signing secret from the box, mint-jwt.py signs it).
It never reaches output, a command line or a file. Only the dev box host is allowed.
Needs: ssh key ~/.ssh/docker_proxmox, node, Google Chrome, and dashboard/node_modules (vite, plugin-vue, playwright).
"""

import argparse
import contextlib
import importlib.util
import io
import json
import os
import pathlib
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from zoneinfo import ZoneInfo

ROOT = pathlib.Path(__file__).resolve().parent.parent
DASHBOARD = ROOT / "dashboard"
WIDGET = ROOT / "server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue"
SAMPLES = ROOT / "server/src/NomNomzBot.Api/Hubs/Broadcasters/WidgetTestSamples.cs"
FIXTURES = DASHBOARD / "src/__tests__/fixtures"

HOST = "192.168.2.60"
BOX = f"http://{HOST}:5080"
CHANNEL = "019f146e-8303-71ef-b698-18d1098d7d7e"
OWNER = "019f146e-81c7-7d3c-8b63-c6e6beae0283"
SSH_KEY = pathlib.Path.home() / ".ssh" / "docker_proxmox"
CHROME = os.environ.get("CHROME_PATH", "C:/Program Files/Google/Chrome/Application/chrome.exe")
ZONE = ZoneInfo("Europe/Amsterdam")
VIEWPORT = (1920, 1080)

# Node side: serves the committed widget with vite, opens it in headless Chrome, measures the page, takes the shot.
DRIVER = r"""
import fs from 'node:fs'
import net from 'node:net'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'

const [, , dashboard, chrome, dir, outPng, width, height] = process.argv
const load = (rel) => import(pathToFileURL(path.join(dashboard, 'node_modules', rel)).href)
const { createServer } = await load('vite/dist/node/index.js')
const vue = (await load('@vitejs/plugin-vue/dist/index.mjs')).default
const { chromium } = createRequire(path.join(dashboard, 'package.json'))('playwright')

const free = await new Promise((resolve) => {
  const probe = net.createServer().listen(0, '127.0.0.1', () => {
    const { port } = probe.address()
    probe.close(() => resolve(port))
  })
})
const server = await createServer({
  root: dir,
  configFile: false,
  logLevel: 'error',
  plugins: [vue()],
  resolve: { alias: { vue: path.join(dashboard, 'node_modules/vue/dist/vue.runtime.esm-bundler.js') } },
  server: { host: '127.0.0.1', port: free, strictPort: true, fs: { strict: false } },
})
await server.listen()

const browser = await chromium.launch({ executablePath: chrome })
const errors = []
try {
  const page = await browser.newPage({ viewport: { width: Number(width), height: Number(height) } })
  page.on('pageerror', (e) => errors.push(String(e.message || e)))
  await page.goto(`http://127.0.0.1:${free}/`, { waitUntil: 'load' })
  await page.waitForSelector('.recap[data-visible="true"] [data-block]', { timeout: 30000 })
  await page.evaluate(async () => {
    await document.fonts.ready
    const pending = Array.from(document.images).filter((i) => !i.complete)
    await Promise.race([
      Promise.all(pending.map((i) => new Promise((r) => { i.onload = r; i.onerror = r }))),
      new Promise((r) => setTimeout(r, 15000)),
    ])
  })
  await page.waitForTimeout(1500)
  const facts = await page.evaluate(() => {
    const box = (el) => {
      const r = el.getBoundingClientRect()
      return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, width: r.width, height: r.height }
    }
    const baseEl = document.querySelector('[style*="--base"]')
    const mark = document.querySelector('img.watermark')
    const markStyle = mark ? getComputedStyle(mark) : null
    const markBox = mark ? box(mark) : null
    const clipped = []
    document.querySelectorAll('[data-block]').forEach((card) => {
      const key = card.getAttribute('data-block')
      const scroll = card.querySelector('.scroll')
      const cardBox = box(card)
      const areaBox = scroll ? box(scroll) : cardBox
      card.querySelectorAll('.row, .sect').forEach((row) => {
        const r = box(row)
        const out = r.bottom > areaBox.bottom + 1 || r.top < areaBox.top - 1 || r.bottom > cardBox.bottom + 1
        if (out) clipped.push({ block: key, text: row.textContent.trim().slice(0, 40), bottom: r.bottom, limit: areaBox.bottom })
      })
    })
    const own = (el) => Array.from(el.childNodes).filter((n) => n.nodeType === 3).map((n) => n.textContent).join('').trim()
    const bigs = Array.from(document.querySelectorAll('.block .big')).map((el) => ({
      block: el.closest('[data-block]').getAttribute('data-block'),
      big: own(el),
      unit: (el.querySelector('small')?.textContent || '').trim(),
    }))
    const thanksEl = document.querySelector('.thanks')
    const emptyLabels = Array.from(document.querySelectorAll('h1, .label')).filter((el) => !el.textContent.trim())
      .map((el) => (el.closest('[data-block]')?.getAttribute('data-block') || el.tagName.toLowerCase()) + ':' + el.className)
    const numbers = Array.from(document.querySelectorAll('.num, .big, .v, .note')).map((el) => el.textContent.trim())
    return {
      bigs,
      thanks: thanksEl ? thanksEl.textContent.trim() : null,
      emptyLabels,
      numbers,
      base: baseEl ? getComputedStyle(baseEl).getPropertyValue('--base').trim() : null,
      watermark: mark
        ? {
            naturalWidth: mark.naturalWidth,
            opacity: Number(markStyle.opacity),
            display: markStyle.display,
            visibility: markStyle.visibility,
            inViewport: markBox.right > 0 && markBox.bottom > 0 && markBox.left < innerWidth && markBox.top < innerHeight,
            width: markBox.width,
          }
        : null,
      blocks: document.querySelectorAll('[data-block]').length,
      clipped,
      text: document.body.innerText,
    }
  })
  facts.pageErrors = errors
  await page.screenshot({ path: outPng, type: 'png' })
  fs.writeFileSync(path.join(dir, 'facts.json'), JSON.stringify(facts))
} finally {
  await browser.close()
  await server.close()
}
"""

PAGE_HTML = """<!doctype html>
<html><head><meta charset="utf-8"><style>html,body{margin:0;background:#000;overflow:hidden}#app{position:fixed;inset:0}</style></head>
<body><div id="app"></div><script type="module" src="./main.ts"></script></body></html>
"""

# The widget reads its numbers through NomNomz.data.streamStats; this hands it the stored payload and nothing else.
MAIN_TS = """import { createApp } from 'vue'
import Recap from '__WIDGET__'
import payload from './payload.json'

const handlers: Record<string, (d: unknown) => void> = {}
let settingsCb: (s: unknown) => void = () => {}

;(window as unknown as Record<string, unknown>).NomNomz = {
  onSettings: (cb: (s: unknown) => void) => { settingsCb = cb },
  on: (t: string, cb: (d: unknown) => void) => { handlers[t] = cb },
  off: () => {},
  data: { streamStats: async () => payload.stats },
}

createApp(Recap).mount('#app')
settingsCb({ labels: payload.labels, holdSeconds: 600 })
handlers['raid_recap.show']({ stream: { id: payload.stats.stream.id } })
"""


def fail(check: str, detail: str) -> str:
    return f"FAIL {check}: {detail}"


# ---------------------------------------------------------------- box access (token stays in this process)


def require_box_host(url: str) -> None:
    if urllib.parse.urlparse(url).hostname != HOST:
        raise SystemExit(f"refused: only the dev box {HOST} is allowed")


def mint_token() -> str:
    secret = subprocess.run(
        [
            "ssh", "-i", str(SSH_KEY), "-o", "BatchMode=yes", f"root@{HOST}",
            "docker exec $(docker ps --format '{{.Names}}' | grep -E 'api-(blue|green)' | head -1) printenv Jwt__Secret",
        ],
        capture_output=True, text=True, timeout=60,
    )
    if secret.returncode != 0 or not secret.stdout.strip():
        raise SystemExit("could not read the signing secret from the box (ssh failed)")
    spec = importlib.util.spec_from_file_location("mint_jwt", ROOT / "scripts/mint-jwt.py")
    mint = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mint)
    args = ["mint-jwt", "--secret", secret.stdout.strip(), "--sub", OWNER, "--tenant", CHANNEL,
            "--username", "Stoney_Eagle", "--roles", "user", "--minutes", "10"]
    out = io.StringIO()
    saved = sys.argv
    sys.argv = args
    try:
        with contextlib.redirect_stdout(out):
            mint.main()
    finally:
        sys.argv = saved
    return out.getvalue().strip()


def call(token: str, method: str, path: str) -> dict:
    url = BOX + path
    require_box_host(url)
    request = urllib.request.Request(url, method=method, data=b"" if method == "POST" else None)
    request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        raise SystemExit(f"{method} {path} -> HTTP {error.code}")


def fetch_recap(stream: str | None, rebuild: bool) -> tuple[str, dict, dict]:
    """Returns (requested stream id, the stored recap dto, the labels of the installed raid recap widget)."""
    token = mint_token()
    base = f"/api/v1/dashboard/{CHANNEL}/stream-recaps"
    if stream is None:
        listed = call(token, "GET", base + "?Page=1&PageSize=1").get("data") or []
        if not listed:
            raise SystemExit("no stored recap on the box")
        stream = listed[0]["streamId"]
    if rebuild:
        call(token, "POST", f"{base}/{stream}")
    dto = call(token, "GET", f"{base}/{stream}").get("data") or {}
    return stream, dto, installed_labels(token)


def installed_labels(token: str) -> dict:
    """The words on the screen are the channel's own raid recap widget settings."""
    widgets = call(token, "GET", f"/api/v1/channels/{CHANNEL}/widgets?Page=1&PageSize=100").get("data") or []
    for widget in widgets:
        if "raid_recap.show" in (widget.get("eventSubscriptions") or []):
            labels = (widget.get("settings") or {}).get("labels") or {}
            return json.loads(labels) if isinstance(labels, str) else labels
    return {}


# ---------------------------------------------------------------- the checks


def sample_literals() -> list[str]:
    """Every string the gallery preview sample (WidgetTestSamples.RaidRecap) puts on screen."""
    text = SAMPLES.read_text(encoding="utf-8")
    start = text.index("private static StreamStatsDto RaidRecap")
    body = text[start:text.index("};", start)]
    return sorted(set(re.findall(r'"([^"]+)"', body)))


def strings_in(value) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, dict):
        return [s for v in value.values() for s in strings_in(v)]
    if isinstance(value, list):
        return [s for v in value for s in strings_in(v)]
    return []


def expected_hue(started_at: str) -> float:
    day = datetime.fromisoformat(started_at.replace("Z", "+00:00")).astimezone(ZONE).day
    return (day - 1) * 360 / 31


def payload_checks(requested: str, dto: dict, offline: bool) -> list[str]:
    failures: list[str] = []
    stats = dto.get("stats") or {}
    stream = stats.get("stream") or {}
    channel = stats.get("channel") or {}
    if dto.get("streamId") != requested or stream.get("id") != requested:
        failures.append(fail("stream id", f"requested {requested}, recap is {dto.get('streamId')} / stats {stream.get('id')}"))
    if not str(channel.get("name") or "").strip():
        failures.append(fail("channel name", "the payload has no channel.name"))
    avatar = str(channel.get("avatarUrl") or "")
    allowed = ("http://", "https://", "data:image/") if offline else ("https://",)
    if not avatar.startswith(allowed):
        failures.append(fail("channel avatar", "the payload has no usable channel.avatarUrl"))
    if not stream.get("startedAt"):
        failures.append(fail("stream start", "the payload has no stream.startedAt, so the day hue is unknown"))
    literals = set(sample_literals())
    leaked = sorted(s for s in strings_in(stats) if s in literals)
    if leaked:
        failures.append(fail("sample data", f"payload carries gallery preview sample values: {leaked[:5]}"))
    for fixture in sorted(FIXTURES.glob("streamRecap*.json")):
        other = json.loads(fixture.read_text(encoding="utf-8")).get("stream") or {}
        if other.get("id") != stream.get("id") and other.get("title") and other.get("title") == stream.get("title"):
            failures.append(fail("test fixture", f"payload repeats the title of {fixture.name}, which is another stream"))
    return failures


# A count that stands for a thing carries its unit word; the raid card carries two ("1 raid · 11 viewers").
UNIT_BLOCKS = {"raids": r"^\S+ · \S+ \S+$", "chatters": r"^\S+$", "songs": r"^\S+$", "redemptions": r"^\S+$"}
NOT_EN_US_GROUPING = re.compile(r"\d[.  ]\d{3}(?!\d)")


def word_checks(stats: dict, facts: dict) -> list[str]:
    """Words and numbers on the screen: unit words, en-US grouping, the thank-you line, no empty label."""
    failures: list[str] = []
    for item in facts.get("bigs", []):
        pattern = UNIT_BLOCKS.get(item["block"])
        if pattern and not re.match(pattern, item["unit"]):
            failures.append(fail("unit word", f"{item['block']}: count '{item['big']}' has unit '{item['unit']}' (a count needs its unit word)"))
    for text in facts.get("numbers", []):
        if NOT_EN_US_GROUPING.search(text):
            failures.append(fail("number format", f"'{text}' is not en-US grouping (1,107)"))
    messages = (stats.get("chat") or {}).get("messages")
    if isinstance(messages, int) and messages >= 1000 and f"{messages:,}" not in facts.get("numbers", []):
        failures.append(fail("number format", f"chat messages {messages} should read '{messages:,}'"))
    duration = stats.get("duration")
    thanks = facts.get("thanks")
    if isinstance(duration, (int, float)) and not thanks:
        failures.append(fail("thank-you line", "the payload has a duration but the header shows no thank-you line"))
    for text in [thanks or ""] + facts.get("numbers", []):
        if re.search(r"\{\w+\}", text):
            failures.append(fail("label placeholder", f"'{text}' still has an unfilled {{placeholder}}"))
    for empty in facts.get("emptyLabels", []):
        failures.append(fail("empty label", f"a setting label renders empty: {empty}"))
    return failures


def dom_checks(dto: dict, facts: dict) -> list[str]:
    failures: list[str] = []
    stats = dto.get("stats") or {}
    started = (stats.get("stream") or {}).get("startedAt")
    if facts.get("pageErrors"):
        failures.append(fail("page errors", "; ".join(facts["pageErrors"])[:200]))
    mark = facts.get("watermark")
    if not mark:
        failures.append(fail("watermark", "no avatar watermark image in the rendered page"))
    else:
        if mark["naturalWidth"] <= 0:
            failures.append(fail("avatar loaded", "the avatar image did not load (naturalWidth 0)"))
        hidden = mark["opacity"] <= 0 or mark["display"] == "none" or mark["visibility"] == "hidden" or not mark["inViewport"]
        if hidden:
            failures.append(fail("watermark visible", f"hidden or off screen: {mark}"))
    base = facts.get("base")
    if base is None or started is None:
        failures.append(fail("day hue", "no --base on the page or no stream start to compare"))
    else:
        got = float(base.replace("deg", ""))
        want = expected_hue(started)
        if abs(got - want) > 0.01:
            failures.append(fail("day hue", f"--base is {got:.2f}deg, the spec says {want:.2f}deg for {started} (Amsterdam day)"))
    if facts.get("blocks", 0) == 0:
        failures.append(fail("blocks", "no card was rendered"))
    for row in facts.get("clipped", []):
        failures.append(fail("row clipped", f"{row['block']}: '{row['text']}' ends at {row['bottom']:.0f}px, card area ends at {row['limit']:.0f}px"))
    failures += word_checks(stats, facts)
    shown = [s for s in sample_literals() if len(s) >= 5 and s in facts.get("text", "")]
    if shown:
        failures.append(fail("sample data", f"the page shows gallery preview sample text: {shown[:5]}"))
    return failures


# ---------------------------------------------------------------- the render


def render(dto: dict, labels: dict, png: pathlib.Path) -> dict:
    for needed in (CHROME, DASHBOARD / "node_modules/vite", WIDGET):
        if not pathlib.Path(needed).exists():
            raise SystemExit(f"missing: {needed}")
    work = pathlib.Path(tempfile.mkdtemp(prefix="render-recap-"))
    try:
        (work / "index.html").write_text(PAGE_HTML, encoding="utf-8")
        (work / "main.ts").write_text(MAIN_TS.replace("__WIDGET__", "/@fs/" + WIDGET.as_posix()), encoding="utf-8")
        (work / "payload.json").write_text(json.dumps({"stats": dto["stats"], "labels": labels}), encoding="utf-8")
        (work / "driver.mjs").write_text(DRIVER, encoding="utf-8")
        shot = work / "shot.png"
        run = subprocess.run(
            ["node", str(work / "driver.mjs"), str(DASHBOARD), CHROME, str(work), str(shot), str(VIEWPORT[0]), str(VIEWPORT[1])],
            capture_output=True, text=True, timeout=240,
        )
        if run.returncode != 0:
            raise SystemExit("render failed: " + (run.stderr or run.stdout)[-300:])
        facts = json.loads((work / "facts.json").read_text(encoding="utf-8"))
        facts["png"] = shot.read_bytes()
        return facts
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--stream", help="stream id (default: the newest stored recap)")
    parser.add_argument("--out", required=True, help="PNG to write (only written when every check passes)")
    parser.add_argument("--rebuild", action="store_true", help="POST /stream-recaps/{streamId} before reading")
    parser.add_argument("--payload-file", help="offline: a recap dto json (streamId, stats, optional labels) instead of the box")
    args = parser.parse_args(argv)

    if args.payload_file:
        dto = json.loads(pathlib.Path(args.payload_file).read_text(encoding="utf-8"))
        requested = args.stream or dto.get("streamId") or ""
        labels = dto.get("labels") or {}
    else:
        requested, dto, labels = fetch_recap(args.stream, args.rebuild)

    failures = payload_checks(requested, dto, offline=bool(args.payload_file))
    if "stats" not in dto:
        failures.append(fail("payload", "the recap has no stats"))
        print("\n".join(failures))
        return 1
    facts = render(dto, labels, pathlib.Path(args.out))
    failures += dom_checks(dto, facts)
    if failures:
        print("\n".join(failures))
        print(f"refused: {len(failures)} check(s) failed, no image written")
        return 1
    pathlib.Path(args.out).write_bytes(facts["png"])
    print(f"ok: all checks passed, wrote {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
