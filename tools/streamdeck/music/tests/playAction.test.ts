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

// S-STREAMDECK-TEST-INFRA proof: PlayAction is a real @action(...)-decorated class
// (src/actions/play.ts extends MusicAction, both real, unmocked) — this instantiates it and drives
// its actual onKeyDown, proving vitest.config.ts's decorator-aware TS transform now produces a
// class whose `@action(...)` decoration and inherited SingletonAction/onKeyDown behavior work
// exactly as the production `tsc` build does. Same wire-assertion style as tests/obsActions.test.ts:
// a real fake HTTP server standing in for the bot's automation-invoke endpoint, asserting the exact
// JSON body received — not merely that the call didn't throw.
//
// Only `streamDeck` (the default export, used for global-settings persistence and logging) is
// faked here, exactly like obsActions.test.ts fakes it — it would otherwise try to reach a real
// Stream Deck host over stdio/websocket, which doesn't exist in a test process. `action` and
// `SingletonAction` are kept REAL (via importOriginal) since exercising those — the actual
// decorator and lifecycle machinery this slice's fix is about — is the point of this test.
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

describe("PlayAction (src/actions/play.ts) — real @action(...)-decorated class, real SingletonAction lifecycle", () => {
  it("onKeyDown invokes music_play with no params — the exact payload MusicAction.onKeyDown sends", async () => {
    const { PlayAction } = await import("../src/actions/play.js");

    const action: PlayAction = new PlayAction();
    const showAlert = (): Promise<void> => Promise.resolve();
    const fakeEvent: KeyDownEvent<JsonObject> = {
      action: { showAlert },
      payload: { settings: {} },
    } as unknown as KeyDownEvent<JsonObject>;

    await action.onKeyDown(fakeEvent);

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_play",
      variables: {},
    });
  });
});
