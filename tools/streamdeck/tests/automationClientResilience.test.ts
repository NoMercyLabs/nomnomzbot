// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi } from "vitest";
import http from "node:http";
import { WebSocketServer } from "ws";

vi.mock("@elgato/streamdeck", () => ({
  default: {
    logger: { info: vi.fn(), warn: vi.fn(), setLevel: vi.fn() },
    settings: {
      getGlobalSettings: vi.fn(() => Promise.resolve({})),
      setGlobalSettings: vi.fn(() => Promise.resolve()),
    },
  },
}));

async function waitUntil(predicate: () => boolean, timeoutMs: number, stepMs = 10): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!predicate()) {
    if (Date.now() > deadline) throw new Error(`waitUntil: condition not met within ${timeoutMs}ms`);
    await new Promise((resolve) => setTimeout(resolve, stepMs));
  }
}

function nowPlaying(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    title: "Track",
    artist: "Artist",
    durationMs: 200000,
    positionMs: 1000,
    isPlaying: true,
    shuffleEnabled: false,
    repeatMode: "off",
    isSaved: false,
    serverTime: new Date().toISOString(),
    ...overrides,
  };
}

/**
 * Reproduces the real defect: a plugin process that has to stay connected for hours during a
 * stream, over a network path that can silently die (laptop sleep, VPN toggle, a NAT/firewall
 * dropping an idle keep-alive with no RST) without either side's socket ever firing `close`/`error`.
 * Before the watchdog existed, `connectStream()`'s reconnect-with-backoff was correct but USELESS
 * against this failure mode — nothing ever told it the connection was dead, so it never ran. The
 * independent REST resync was the intended safety net, but it used a bare `fetch()` with no
 * timeout, so a hang on THAT path never resolved or rejected either. Both self-healing paths could
 * hang forever at once, which is exactly "stuck on stale state until the plugin is manually
 * restarted, with nothing that explains why or prevents it happening again."
 */
describe("a WebSocket that goes silent without closing is declared dead and replaced", () => {
  it("terminates the zombie socket, reconnects, and resumes real WS pushes", async () => {
    const httpServer = http.createServer((req, res) => {
      if (req.url === "/automation/v1/music/now-playing" && req.method === "GET") {
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ status: "ok", data: nowPlaying() }));
        return;
      }
      res.writeHead(404);
      res.end();
    });
    const wss = new WebSocketServer({ server: httpServer, path: "/automation/v1/stream" });

    let connectionCount = 0;
    const connectedSockets: import("ws").WebSocket[] = [];
    wss.on("connection", (socket) => {
      connectionCount++;
      connectedSockets.push(socket);
      // Deliberately silent — no message, no ping, nothing. This is the zombie-connection case:
      // a real server would normally still auto-ping, but the point of the watchdog is to survive
      // even the case where the network path drops everything, pings included.
    });

    await new Promise<void>((resolve) => httpServer.listen(0, "127.0.0.1", resolve));
    const backendPort = (httpServer.address() as { port: number }).port;

    try {
      const { AutomationClient } = await import("../src/connection/automationClient.js");
      const { setPairingState } = await import("../src/connection/tokenStore.js");

      // Real timers, deliberately tiny windows — proves the mechanism fires, not that production's
      // 45s window is correct (that's a judgment call, not something a test can validate).
      const client = new AutomationClient({
        wsIdleTimeoutMs: 100,
        wsWatchdogIntervalMs: 20,
      });

      await setPairingState({
        backendUrl: `http://127.0.0.1:${backendPort}`,
        token: "nnzb_ak_resilience_test",
        tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
        deviceKind: "streamdeck",
      });

      const received: unknown[] = [];
      client.onNowPlaying((payload) => received.push(payload));

      await client.connectStream();
      await waitUntil(() => connectionCount >= 1, 1000);
      expect(connectionCount).toBe(1);

      // Past wsIdleTimeoutMs with zero inbound frames on connection #1 — the watchdog must have
      // terminated it, and connectStream()'s existing reconnect-with-backoff must have opened a
      // second one, all without any test code calling connectStream() again. The reconnect itself
      // carries production's real 1000ms initial backoff (untouched by the injected watchdog
      // timing), so the wait budget here has to clear THAT delay on top of the watchdog window —
      // polled rather than one flat sleep so the test moves on the instant it's true instead of
      // also eating however many EXTRA idle-timeout cycles a fixed sleep would let elapse (this
      // server never responds to anything, so every new connection is itself a fresh zombie clock).
      await waitUntil(() => connectionCount >= 2, 3000);

      // Prove the NEW connection is actually live, not just that a socket object exists: push a
      // real song.changed frame down it immediately (this connection is ALSO on the same short
      // zombie clock, so there's no slack to spare) and confirm the client applies it.
      const latest = connectedSockets[connectedSockets.length - 1];
      latest.send(
        JSON.stringify({
          op: "event",
          type: "song.changed",
          data: nowPlaying({ title: "Alive After Reconnect" }),
        }),
      );
      await waitUntil(
        () => received.some((p) => (p as { title: string }).title === "Alive After Reconnect"),
        500,
      );
    } finally {
      wss.close();
      httpServer.close();
    }
  });
});

describe("a REST call that never responds does not block recovery forever", () => {
  it("times out a hung now-playing fetch instead of hanging, so the next resync still lands", async () => {
    let requestCount = 0;
    const httpServer = http.createServer((req, res) => {
      if (req.url === "/automation/v1/music/now-playing" && req.method === "GET") {
        requestCount++;
        if (requestCount === 1) {
          // Accept the connection, never respond — the exact shape of a half-dead network path:
          // the request is "in flight" forever from the client's point of view.
          return;
        }
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ status: "ok", data: nowPlaying({ title: "Recovered" }) }));
        return;
      }
      res.writeHead(404);
      res.end();
    });
    const wss = new WebSocketServer({ server: httpServer, path: "/automation/v1/stream" });
    wss.on("connection", () => {
      /* WS side isn't under test here */
    });

    await new Promise<void>((resolve) => httpServer.listen(0, "127.0.0.1", resolve));
    const backendPort = (httpServer.address() as { port: number }).port;

    try {
      const { AutomationClient } = await import("../src/connection/automationClient.js");
      const { setPairingState } = await import("../src/connection/tokenStore.js");

      const client = new AutomationClient({ requestTimeoutMs: 100 });

      await setPairingState({
        backendUrl: `http://127.0.0.1:${backendPort}`,
        token: "nnzb_ak_timeout_test",
        tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
        deviceKind: "streamdeck",
      });

      // The FIRST call hangs server-side forever. Without a client-side timeout this promise would
      // never settle. It must reject (caught by the caller) within the injected timeout window.
      const firstCall = client.getNowPlaying();
      const outcome = await Promise.race([
        firstCall.then(() => "resolved" as const).catch(() => "rejected" as const),
        new Promise<"never-settled">((resolve) => setTimeout(() => resolve("never-settled"), 2000)),
      ]);
      expect(outcome).toBe("rejected");

      // The SECOND call, against the now-healthy handler, proves the client itself is still usable
      // afterward — a timeout on one call must not poison the client for subsequent ones.
      const second = await client.getNowPlaying();
      expect(second.title).toBe("Recovered");
    } finally {
      wss.close();
      httpServer.close();
    }
  });
});
