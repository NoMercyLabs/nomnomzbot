// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi, beforeAll, afterAll, afterEach } from "vitest";
import http from "node:http";
import { WebSocketServer } from "ws";

let fakeGlobalSettings: Record<string, unknown> = {};
vi.mock("@elgato/streamdeck", () => ({
  default: {
    logger: { info: vi.fn(), warn: vi.fn(), setLevel: vi.fn() },
    settings: {
      getGlobalSettings: vi.fn(() => Promise.resolve({ ...fakeGlobalSettings })),
      setGlobalSettings: vi.fn((next: Record<string, unknown>) => {
        fakeGlobalSettings = next;
        return Promise.resolve();
      }),
    },
  },
}));

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

let backendPort: number;
let httpServer: http.Server;
let wss: WebSocketServer;
let nowPlayingGetCount = 0;

beforeAll(async () => {
  httpServer = http.createServer((req, res) => {
    if (req.url === "/automation/v1/music/now-playing" && req.method === "GET") {
      nowPlayingGetCount++;
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok", data: nowPlaying({ isPlaying: nowPlayingGetCount % 2 === 1 }) }));
      return;
    }
    res.writeHead(404);
    res.end();
  });
  wss = new WebSocketServer({ server: httpServer, path: "/automation/v1/stream" });
  wss.on("connection", () => {
    /* the plugin only needs a socket that accepts connections for this test — no server-pushed
     * song.changed frames here, since we're proving the HTTP-fallback path, not the WS-push path */
  });
  await new Promise<void>((resolve) => httpServer.listen(0, "127.0.0.1", resolve));
  backendPort = (httpServer.address() as { port: number }).port;
});

afterAll(() => {
  wss.close();
  httpServer.close();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("automation client now-playing resync (no WS event needed to reflect real state)", () => {
  it("seeds now-playing state with a real GET the moment the stream connects", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");
    const { setPairingState } = await import("../src/connection/tokenStore.js");
    const { nowPlayingState } = await import("../src/nowPlaying/state.js");

    await setPairingState({
      backendUrl: `http://127.0.0.1:${backendPort}`,
      token: "nnzb_ak_test",
      tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
      deviceKind: "streamdeck",
    });

    expect(nowPlayingState.current).toBeNull();

    // Mirrors plugin.ts's own wiring (automationClient.onNowPlaying -> nowPlayingState.apply) — the
    // client only emits payloads, applying them to the shared state is the plugin's job.
    automationClient.onNowPlaying((payload) => nowPlayingState.apply(payload));

    await automationClient.connectStream();
    await new Promise((resolve) => setTimeout(resolve, 300));

    expect(nowPlayingState.current).not.toBeNull();
    expect(nowPlayingGetCount).toBeGreaterThanOrEqual(1);
  });
});

describe("automation client WS push (real server frame shape)", () => {
  it("applies a real {op:'event', type:'song.changed', data} push, not just the fallback resync", async () => {
    // A fresh module graph: the automationClient singleton from the previous test already has an
    // open `ws`, and connectStream() is intentionally idempotent (no-op) while one exists.
    vi.resetModules();
    const { automationClient } = await import("../src/connection/automationClient.js");
    const { setPairingState } = await import("../src/connection/tokenStore.js");
    const { nowPlayingState } = await import("../src/nowPlaying/state.js");

    let connectedSocket: import("ws").WebSocket | null = null;
    wss.once("connection", (socket) => {
      connectedSocket = socket;
    });

    await setPairingState({
      backendUrl: `http://127.0.0.1:${backendPort}`,
      token: "nnzb_ak_test2",
      tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
      deviceKind: "streamdeck",
    });
    automationClient.onNowPlaying((payload) => nowPlayingState.apply(payload));
    await automationClient.connectStream();
    await new Promise((resolve) => setTimeout(resolve, 200));

    expect(connectedSocket).not.toBeNull();
    // automation-api.md §4.2's real wire shape for a pushed event — {event, payload} (the shape this
    // client used to check) would never match anything a real server actually sends.
    connectedSocket!.send(
      JSON.stringify({
        op: "event",
        type: "song.changed",
        broadcasterId: "b1",
        occurredAt: new Date().toISOString(),
        data: nowPlaying({ title: "Pushed Live", positionMs: 77_000 }),
      }),
    );
    await new Promise((resolve) => setTimeout(resolve, 200));

    expect(nowPlayingState.current?.title).toBe("Pushed Live");
    expect(nowPlayingState.current?.positionMs).toBe(77_000);
  });
});
