// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi, beforeAll, afterAll, beforeEach } from "vitest";
import http from "node:http";
import type { IncomingMessage } from "node:http";
import type { KeyDownEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

// Same proof as tests/playAction.test.ts, for the two newest OBS actions
// (S-STREAMDECK-OBS-REMAINDER, 44aa4e1e): ToggleReplayBufferAction and ToggleVirtualCamAction are real,
// unmocked, @action(...)-decorated classes — this instantiates them and drives their actual onKeyDown,
// so a regression in the decorator/SingletonAction lifecycle machinery itself (not just the
// resolveParams mapping obsActions.test.ts already covers) would fail here.
let fakeGlobalSettings: Record<string, unknown> = {};
vi.mock("@elgato/streamdeck", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@elgato/streamdeck")>();
  return {
    ...actual,
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
  };
});

let backendPort: number;
let httpServer: http.Server;
let receivedInvokes: { path: string; body: unknown }[] = [];

beforeAll(async () => {
  httpServer = http.createServer((req: IncomingMessage, res) => {
    if (req.url === "/automation/v1/invoke" && req.method === "POST") {
      let raw = "";
      req.on("data", (chunk: Buffer) => (raw += chunk.toString()));
      req.on("end", () => {
        receivedInvokes.push({ path: req.url!, body: JSON.parse(raw) });
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ status: "ok", data: { accepted: true } }));
      });
      return;
    }
    res.writeHead(404);
    res.end();
  });
  await new Promise<void>((resolve) => httpServer.listen(0, "127.0.0.1", resolve));
  backendPort = (httpServer.address() as { port: number }).port;
});

afterAll(() => {
  httpServer.close();
});

beforeEach(async () => {
  receivedInvokes = [];
  fakeGlobalSettings = {};
  const { setPairingState } = await import("@nomnomzbot/streamdeck-shared");
  await setPairingState({
    backendUrl: `http://127.0.0.1:${backendPort}`,
    token: "nnzb_ak_test",
    tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
    deviceKind: "streamdeck",
  });
});

function fakeKeyDownEvent(): KeyDownEvent<JsonObject> {
  const showAlert = (): Promise<void> => Promise.resolve();
  return {
    action: { showAlert },
    payload: { settings: {} },
  } as unknown as KeyDownEvent<JsonObject>;
}

describe("ToggleReplayBufferAction (src/actions/obsToggleReplayBuffer.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_replay_buffer with action: toggle", async () => {
    const { ToggleReplayBufferAction } = await import(
      "../src/actions/obsToggleReplayBuffer.js"
    );

    await new ToggleReplayBufferAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_replay_buffer",
      variables: { action: "toggle" },
    });
  });
});

describe("ToggleVirtualCamAction (src/actions/obsToggleVirtualCam.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_virtual_cam with action: toggle", async () => {
    const { ToggleVirtualCamAction } = await import(
      "../src/actions/obsToggleVirtualCam.js"
    );

    await new ToggleVirtualCamAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_virtual_cam",
      variables: { action: "toggle" },
    });
  });
});
