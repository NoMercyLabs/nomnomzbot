// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi, beforeAll, afterAll } from "vitest";
import http from "node:http";

const openedUrls: string[] = [];
vi.mock("open", () => ({
  default: vi.fn((url: string) => {
    openedUrls.push(url);
    return Promise.resolve();
  }),
}));

// authWindow.ts pulls in tokenStore.ts, which reads/writes global settings via the real SDK —
// that requires Stream Deck's own CLI handshake args, which don't exist outside the app. Fake the
// minimal surface the flow actually touches.
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

let fakeBackend: http.Server;
let backendPort: number;

beforeAll(async () => {
  fakeBackend = http.createServer((req, res) => {
    if (req.url === "/automation/v1/pair/device/init" && req.method === "POST") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(
        JSON.stringify({
          status: "ok",
          data: {
            deviceCode: "dc-1",
            userCode: "ABCD-1234",
            verificationUri: "http://fake-backend/approve?code=ABCD-1234",
            expiresAt: new Date(Date.now() + 60000).toISOString(),
            pollIntervalSeconds: 1,
          },
        }),
      );
      return;
    }
    res.writeHead(404);
    res.end();
  });
  await new Promise<void>((resolve) => fakeBackend.listen(0, "127.0.0.1", resolve));
  backendPort = (fakeBackend.address() as { port: number }).port;
});

afterAll(() => {
  fakeBackend.close();
});

describe("auth window (spawned by the plugin itself, stream-deck.md D9 revised)", () => {
  it("serves a page with a host field and an Authorize button, and starts pairing when clicked", async () => {
    const { openAuthWindow } = await import("../src/connection/authWindow.js");
    const onPaired = vi.fn();
    await openAuthWindow(onPaired);

    // A real browser window was opened, pointed at the plugin's own local page.
    expect(openedUrls.some((u) => u.includes("127.0.0.1:21617"))).toBe(true);

    const page = await fetch("http://127.0.0.1:21617/").then((r) => r.text());
    expect(page).toContain("Authorize");
    expect(page).toContain("localhost:5080"); // default host pre-filled

    const authorize = await fetch("http://127.0.0.1:21617/authorize", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ host: `http://127.0.0.1:${backendPort}` }),
    }).then((r) => r.json());

    expect(authorize.ok).toBe(true);
    expect(authorize.userCode).toBe("ABCD-1234");
    expect(authorize.verificationUri).toContain("approve?code=ABCD-1234");

    // Clicking Authorize opens the backend's own approval page as the second tab.
    expect(openedUrls).toContain("http://fake-backend/approve?code=ABCD-1234");

    const status = await fetch("http://127.0.0.1:21617/status").then((r) => r.json());
    expect(status.phase).toBe("waiting");
  });
});
