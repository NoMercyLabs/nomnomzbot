// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import http, { type Server } from "node:http";
import streamDeck from "@elgato/streamdeck";
import open from "open";
import { DEFAULT_HOST, getHost, setHost, setPairingState } from "./tokenStore.js";
import { initDevicePairing, pollDevicePairing, getLastDeviceFlowError } from "./deviceFlow.js";
import { setDeviceFlowStatus } from "./deviceFlowState.js";

/**
 * The plugin-initiated auth window (stream-deck.md D9, revised): rather than pairing silently in the
 * background, the plugin spawns this page itself in the operator's browser the moment it's unpaired.
 * It is the ONLY manual step — a host field (default {@link DEFAULT_HOST}) and an "Authorize" button.
 * Clicking it runs the device-init call, opens the backend's own approval page in a second tab, and
 * polls here until approved, all served from a tiny local HTTP server (no dashboard/app code needed).
 */
const PORT = 21617;
let server: Server | null = null;

type FlowState =
  | { phase: "idle" }
  | { phase: "error"; message: string }
  | { phase: "waiting"; userCode: string; verificationUri: string }
  | { phase: "paired" };

let flow: FlowState = { phase: "idle" };

export async function openAuthWindow(onPaired: () => void): Promise<void> {
  if (!server) startServer(onPaired);
  const url = `http://127.0.0.1:${PORT}/`;
  streamDeck.logger.info(`Auth window: opening ${url}`);
  await open(url).catch((error: unknown) => {
    streamDeck.logger.warn(
      `Auth window: couldn't open a browser automatically (${error instanceof Error ? error.message : "unknown error"}) — open ${url} manually.`,
    );
  });
}

function startServer(onPaired: () => void): void {
  server = http.createServer((req, res) => {
    void handleRequest(req, res, onPaired);
  });
  server.listen(PORT, "127.0.0.1");
  streamDeck.logger.info(`Auth window: local server listening on 127.0.0.1:${PORT}`);
}

async function handleRequest(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  onPaired: () => void,
): Promise<void> {
  const url = new URL(req.url ?? "/", "http://127.0.0.1");

  if (url.pathname === "/" && req.method === "GET") {
    const host = await getHost();
    res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    res.end(renderPage(host));
    return;
  }

  if (url.pathname === "/authorize" && req.method === "POST") {
    const body = await readBody(req);
    const host = (JSON.parse(body || "{}").host as string | undefined)?.trim() || DEFAULT_HOST;
    await setHost(host);
    streamDeck.logger.info(`Auth window: Authorize clicked for ${host}.`);

    const init = await initDevicePairing(host);
    if (!init) {
      const message = getLastDeviceFlowError() ?? "Could not reach the backend.";
      flow = { phase: "error", message };
      setDeviceFlowStatus({ paired: false, verificationUri: null, tokenExpiresAt: null, lastError: message });
      respondJson(res, 200, { ok: false, message });
      return;
    }

    flow = { phase: "waiting", userCode: init.userCode, verificationUri: init.verificationUri };
    setDeviceFlowStatus({ paired: false, verificationUri: init.verificationUri, tokenExpiresAt: null, lastError: null });
    // Opened server-side (not via the page's own window.open) so a popup blocker on the auth window's
    // tab can never swallow it — the approval page is the actual login/authorize step the user asked for.
    await open(init.verificationUri).catch(() => {
      /* best-effort — the link is still shown on the page either way */
    });
    respondJson(res, 200, { ok: true, userCode: init.userCode, verificationUri: init.verificationUri });

    void pollUntilPaired(host, init.deviceCode, init.expiresAt, init.pollIntervalMs, onPaired);
    return;
  }

  if (url.pathname === "/status" && req.method === "GET") {
    respondJson(res, 200, flow);
    return;
  }

  res.writeHead(404);
  res.end();
}

async function pollUntilPaired(
  host: string,
  deviceCode: string,
  expiresAt: Date,
  pollIntervalMs: number,
  onPaired: () => void,
): Promise<void> {
  while (Date.now() < expiresAt.getTime()) {
    await sleep(pollIntervalMs);
    const result = await pollDevicePairing(host, deviceCode);
    if (result === "pending") continue;
    if (result === null) {
      const message = getLastDeviceFlowError() ?? "The pairing request expired — click Authorize to try again.";
      flow = { phase: "error", message };
      setDeviceFlowStatus({ paired: false, verificationUri: null, tokenExpiresAt: null, lastError: message });
      return;
    }
    flow = { phase: "paired" };
    streamDeck.logger.info("Auth window: approved — paired.");
    await setPairingState(result);
    setDeviceFlowStatus({ paired: true, verificationUri: null, tokenExpiresAt: result.tokenExpiresAt, lastError: null });
    onPaired();
    closeServer();
    return;
  }
  flow = { phase: "error", message: "The pairing code expired — click Authorize to try again." };
}

function closeServer(): void {
  server?.close();
  server = null;
}

function readBody(req: http.IncomingMessage): Promise<string> {
  return new Promise((resolve) => {
    let data = "";
    req.on("data", (chunk: Buffer) => (data += chunk.toString()));
    req.on("end", () => resolve(data));
  });
}

function respondJson(res: http.ServerResponse, status: number, body: unknown): void {
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(body));
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function renderPage(host: string): string {
  return `<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<title>NomNomzBot — Connect Stream Deck</title>
<style>
  body { font-family: -apple-system, Segoe UI, sans-serif; background: #1a1a1a; color: #eee; display: flex;
         align-items: center; justify-content: center; height: 100vh; margin: 0; }
  main { width: 360px; background: #242424; border-radius: 12px; padding: 28px; }
  h1 { font-size: 18px; margin: 0 0 4px; }
  p.sub { color: #999; font-size: 13px; margin: 0 0 20px; }
  label { display: block; font-size: 13px; margin-bottom: 6px; color: #ccc; }
  input { width: 100%; box-sizing: border-box; padding: 10px; border-radius: 6px; border: 1px solid #444;
          background: #1a1a1a; color: #eee; font-size: 14px; margin-bottom: 16px; }
  button { width: 100%; padding: 12px; border-radius: 6px; border: none; background: #7c5cff; color: #fff;
           font-size: 14px; font-weight: 600; cursor: pointer; }
  button:disabled { opacity: 0.6; cursor: default; }
  #status { margin-top: 16px; font-size: 13px; color: #ccc; min-height: 20px; }
  #status.error { color: #ff8080; }
  #status.success { color: #7cff9c; }
  a { color: #7c5cff; }
</style>
</head>
<body>
<main>
  <h1>Connect your Stream Deck</h1>
  <p class="sub">Enter your bot's URL and authorize to pair.</p>
  <label for="host">Bot URL</label>
  <input id="host" value="${escapeHtml(host)}" />
  <button id="authorize">Authorize</button>
  <div id="status"></div>
</main>
<script>
  const statusEl = document.getElementById("status");
  const button = document.getElementById("authorize");
  const hostInput = document.getElementById("host");
  let polling = null;

  button.addEventListener("click", async () => {
    button.disabled = true;
    statusEl.className = "";
    statusEl.textContent = "Requesting a pairing code…";
    const res = await fetch("/authorize", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ host: hostInput.value }),
    }).then((r) => r.json()).catch(() => null);

    if (!res || !res.ok) {
      statusEl.className = "error";
      statusEl.textContent = res?.message || "Could not reach that bot.";
      button.disabled = false;
      return;
    }

    statusEl.innerHTML = 'Code <b>' + res.userCode + '</b> — approve it in the tab that just opened, or <a href="' + res.verificationUri + '" target="_blank">click here</a>.';
    poll();
  });

  function poll() {
    if (polling) clearInterval(polling);
    polling = setInterval(async () => {
      const state = await fetch("/status").then((r) => r.json()).catch(() => null);
      if (!state) return;
      if (state.phase === "paired") {
        clearInterval(polling);
        statusEl.className = "success";
        statusEl.textContent = "Paired! You can close this tab.";
      } else if (state.phase === "error") {
        clearInterval(polling);
        statusEl.className = "error";
        statusEl.textContent = state.message;
        button.disabled = false;
      }
    }, 1500);
  }
</script>
</body>
</html>`;
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}
