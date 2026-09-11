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
import type { SendToPluginEvent } from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";

// Proves the OTHER half of S-STREAMDECK-OBS-REMAINDER's dropdown picker (obsActions.test.ts /
// obsInstantiatedActions*.test.ts already cover onKeyDown's invoke payload): ObsAction.onSendToPlugin's
// listScenes/listInputs relay actually calls the real automationClient against a real fake HTTP server
// and forwards the real GET /automation/v1/obs/scenes|inputs response to the property inspector via
// streamDeck.ui.sendToPropertyInspector — the same wire-proof style automationClient.test.ts and
// musicPickerActions.test.ts already use for the analogous listDevices/listPlaylists relay.
let fakeGlobalSettings: Record<string, unknown> = {};
const sendToPropertyInspector = vi.fn(() => Promise.resolve());
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
      ui: { sendToPropertyInspector },
    },
  };
});

let backendPort: number;
let httpServer: http.Server;
let scenesResponse: unknown = [];
let inputsResponse: unknown = [];
let requestedPaths: string[] = [];

beforeAll(async () => {
  httpServer = http.createServer((req: IncomingMessage, res) => {
    requestedPaths.push(req.url!);
    if (req.url === "/automation/v1/obs/scenes" && req.method === "GET") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok", data: scenesResponse }));
      return;
    }
    if (req.url === "/automation/v1/obs/inputs" && req.method === "GET") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok", data: inputsResponse }));
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
  requestedPaths = [];
  scenesResponse = [];
  inputsResponse = [];
  fakeGlobalSettings = {};
  sendToPropertyInspector.mockClear();
  const { setPairingState } = await import("@nomnomzbot/streamdeck-shared");
  await setPairingState({
    backendUrl: `http://127.0.0.1:${backendPort}`,
    token: "nnzb_ak_test",
    tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
    deviceKind: "streamdeck",
  });
});

function fakeSendToPluginEvent(payload: JsonObject): SendToPluginEvent<JsonValue, JsonObject> {
  return { payload } as unknown as SendToPluginEvent<JsonValue, JsonObject>;
}

interface PiRelayLike {
  onSendToPlugin: (ev: SendToPluginEvent<JsonValue, JsonObject>) => Promise<void>;
}

describe("SwitchSceneAction property inspector — src/actions/obsAction.ts onSendToPlugin listScenes", () => {
  it("fetches GET /automation/v1/obs/scenes and forwards the scene list to the property inspector", async () => {
    scenesResponse = [
      { name: "Starting Soon", isCurrent: false },
      { name: "Game Scene", isCurrent: true },
    ];
    const { SwitchSceneAction } = await import("../src/actions/obsSwitchScene.js");
    const action = new SwitchSceneAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listScenes" }));

    expect(requestedPaths).toContain("/automation/v1/obs/scenes");
    expect(sendToPropertyInspector).toHaveBeenCalledWith({
      type: "scenes",
      scenes: scenesResponse,
    });
  });

  it("forwards an empty scene list rather than throwing when the backend call fails (e.g. OBS not connected)", async () => {
    const { SwitchSceneAction } = await import("../src/actions/obsSwitchScene.js");
    const action = new SwitchSceneAction() as unknown as PiRelayLike;
    const { clearPairingState } = await import("@nomnomzbot/streamdeck-shared");
    await clearPairingState();

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listScenes" }));

    expect(sendToPropertyInspector).toHaveBeenCalledWith({ type: "scenes", scenes: [] });
  });
});

describe("ToggleMuteAction property inspector — src/actions/obsAction.ts onSendToPlugin listInputs", () => {
  it("fetches GET /automation/v1/obs/inputs and forwards the input list to the property inspector", async () => {
    inputsResponse = [{ name: "Mic/Aux", kind: "wasapi_input_capture", muted: false, volumeDb: -6 }];
    const { ToggleMuteAction } = await import("../src/actions/obsToggleMute.js");
    const action = new ToggleMuteAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listInputs" }));

    expect(requestedPaths).toContain("/automation/v1/obs/inputs");
    expect(sendToPropertyInspector).toHaveBeenCalledWith({
      type: "inputs",
      inputs: inputsResponse,
    });
  });

  it("forwards an empty input list rather than throwing when the backend call fails", async () => {
    const { ToggleMuteAction } = await import("../src/actions/obsToggleMute.js");
    const action = new ToggleMuteAction() as unknown as PiRelayLike;
    const { clearPairingState } = await import("@nomnomzbot/streamdeck-shared");
    await clearPairingState();

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listInputs" }));

    expect(sendToPropertyInspector).toHaveBeenCalledWith({ type: "inputs", inputs: [] });
  });

  it("ignores an unrelated request type (getHost) without touching the OBS endpoints", async () => {
    const { ToggleMuteAction } = await import("../src/actions/obsToggleMute.js");
    const action = new ToggleMuteAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "getHost" }));

    expect(requestedPaths).not.toContain("/automation/v1/obs/scenes");
    expect(requestedPaths).not.toContain("/automation/v1/obs/inputs");
  });
});
