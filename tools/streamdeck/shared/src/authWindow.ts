// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import http, { type Server } from "node:http";
import streamDeck from "@elgato/streamdeck";
import open from "open";
import { DEFAULT_HOST, getHost, getPairingState, setHost, setPairingState } from "./tokenStore.js";
import { automationClient } from "./automationClient.js";
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
/** The music and OBS plugins each serve a page; the second one to start takes the next port. */
const PORT_TRIES = 4;
let server: Server | null = null;
let servedPort: number | null = null;

type FlowState =
  | { phase: "idle" }
  | { phase: "error"; message: string }
  | { phase: "waiting"; userCode: string; verificationUri: string }
  | { phase: "paired" };

let flow: FlowState = { phase: "idle" };

export async function openAuthWindow(onPaired: () => void): Promise<void> {
  const url = await serveAuthWindow(onPaired);
  if (!url) return;
  streamDeck.logger.info(`Auth window: opening ${url}`);
  await open(url).catch((error: unknown) => {
    streamDeck.logger.warn(
      `Auth window: couldn't open a browser automatically (${error instanceof Error ? error.message : "unknown error"}) — open ${url} manually.`,
    );
  });
}

/**
 * Serves the page without opening a browser. A paired plugin keeps it running too, so the bot address can
 * change (Stoney, 2026-11-17: move the plugin off the dev server) — Stream Deck shows no plugin settings page.
 */
export async function serveAuthWindow(onPaired: () => void): Promise<string | null> {
  if (!server) await startServer(onPaired);

  return authWindowUrl();
}

/** The page's address, or null while no port could be taken. */
export function authWindowUrl(): string | null {
  return servedPort === null ? null : `http://127.0.0.1:${servedPort}/`;
}

async function startServer(onPaired: () => void): Promise<void> {
  for (let port = PORT; port < PORT + PORT_TRIES; port++) {
    const candidate = http.createServer((req, res) => {
      void handleRequest(req, res, onPaired);
    });
    if (await listens(candidate, port)) {
      server = candidate;
      servedPort = port;
      streamDeck.logger.info(`Auth window: local server listening on 127.0.0.1:${port}`);
      return;
    }
  }
  streamDeck.logger.warn(`Auth window: ports ${PORT}-${PORT + PORT_TRIES - 1} are all taken; no pairing page.`);
}

function listens(candidate: Server, port: number): Promise<boolean> {
  return new Promise((resolve) => {
    candidate.once("error", () => resolve(false));
    candidate.listen(port, "127.0.0.1", () => resolve(true));
  });
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
    res.end(renderPage(host, await pairedChannel()));
    return;
  }

  if (url.pathname === "/unpair" && req.method === "POST") {
    await automationClient.unpair();
    flow = { phase: "idle" };
    setDeviceFlowStatus({ paired: false, verificationUri: null, tokenExpiresAt: null, lastError: null });
    streamDeck.logger.info("Auth window: unpaired.");
    respondJson(res, 200, { ok: true });
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

interface Pairing {
  channel: string;
  backendUrl: string;
}

/** The current pairing, named by its channel; null while unpaired. */
async function pairedChannel(): Promise<Pairing | null> {
  const state = await getPairingState();
  if (!state) return null;
  const info = await automationClient.getInfo().catch(() => null);

  return { channel: info?.channelName ?? "a channel the bot did not name", backendUrl: state.backendUrl };
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

function renderPairing(pairing: Pairing | null): string {
  if (!pairing) return `<p class="sub">Enter your bot's URL and authorize to pair.</p>`;

  return `<section class="paired">
    <p>Paired to <b>${escapeHtml(pairing.channel)}</b></p>
    <p class="sub">on ${escapeHtml(pairing.backendUrl)}</p>
    <button id="unpair" class="secondary">Unpair</button>
  </section>
  <p class="sub">To pair with another bot, enter its URL and authorize.</p>`;
}

function renderPage(host: string, pairing: Pairing | null): string {
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
  section.paired { background: #1a1a1a; border-radius: 6px; padding: 12px; margin-bottom: 20px; }
  section.paired p { margin: 0; font-size: 14px; }
  section.paired p.sub { margin: 2px 0 12px; }
  button.secondary { width: auto; padding: 8px 12px; background: transparent; border: 1px solid #444;
                     color: #ff8080; font-weight: 500; }
</style>
</head>
<body>
<main>
  <h1>Connect your Stream Deck</h1>
  ${renderPairing(pairing)}
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

  document.getElementById("unpair")?.addEventListener("click", async () => {
    const res = await fetch("/unpair", { method: "POST" }).then((r) => r.json()).catch(() => null);
    if (res && res.ok) location.reload();
  });

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
