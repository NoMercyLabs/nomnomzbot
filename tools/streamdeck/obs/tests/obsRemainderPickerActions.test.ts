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

// S-STREAMDECK-OBS-REMAINDER: proves the three NEW dependent-picker relays added to
// src/actions/obsAction.ts's onSendToPlugin (listSceneItems, listSceneTransitions, listSourceFilters) —
// same wire-proof style as obsPickerActions.test.ts's listScenes/listInputs coverage: a real
// automationClient call against a real fake HTTP server, forwarded to the property inspector via
// streamDeck.ui.sendToPropertyInspector.
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
let sceneItemsResponse: unknown = [];
let transitionsResponse: unknown = [];
let sourceFiltersResponse: unknown = [];
let requestedUrls: string[] = [];

beforeAll(async () => {
  httpServer = http.createServer((req: IncomingMessage, res) => {
    requestedUrls.push(req.url!);
    if (req.url?.startsWith("/automation/v1/obs/scene-items") && req.method === "GET") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok", data: sceneItemsResponse }));
      return;
    }
    if (req.url === "/automation/v1/obs/scene-transitions" && req.method === "GET") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok", data: transitionsResponse }));
      return;
    }
    if (req.url?.startsWith("/automation/v1/obs/source-filters") && req.method === "GET") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok", data: sourceFiltersResponse }));
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
  requestedUrls = [];
  sceneItemsResponse = [];
  transitionsResponse = [];
  sourceFiltersResponse = [];
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

describe("SetSourceAction property inspector — onSendToPlugin listSceneItems", () => {
  it("fetches GET /automation/v1/obs/scene-items?sceneName= and forwards the item list", async () => {
    sceneItemsResponse = [
      { sceneItemId: 1, sourceName: "Webcam", enabled: true },
      { sceneItemId: 2, sourceName: "Overlay", enabled: false },
    ];
    const { SetSourceAction } = await import("../src/actions/obsSetSource.js");
    const action = new SetSourceAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listSceneItems", sceneName: "Game Scene" }));

    expect(requestedUrls).toContain("/automation/v1/obs/scene-items?sceneName=Game%20Scene");
    expect(sendToPropertyInspector).toHaveBeenCalledWith({
      type: "sceneItems",
      sceneItems: sceneItemsResponse,
    });
  });

  it("forwards an empty list rather than throwing when the backend call fails", async () => {
    const { SetSourceAction } = await import("../src/actions/obsSetSource.js");
    const action = new SetSourceAction() as unknown as PiRelayLike;
    const { clearPairingState } = await import("@nomnomzbot/streamdeck-shared");
    await clearPairingState();

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listSceneItems", sceneName: "Game Scene" }));

    expect(sendToPropertyInspector).toHaveBeenCalledWith({ type: "sceneItems", sceneItems: [] });
  });
});

describe("TransitionAction property inspector — onSendToPlugin listSceneTransitions", () => {
  it("fetches GET /automation/v1/obs/scene-transitions and forwards the transition list", async () => {
    transitionsResponse = [
      { name: "Fade", isCurrent: true },
      { name: "Cut", isCurrent: false },
    ];
    const { TransitionAction } = await import("../src/actions/obsTransition.js");
    const action = new TransitionAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listSceneTransitions" }));

    expect(requestedUrls).toContain("/automation/v1/obs/scene-transitions");
    expect(sendToPropertyInspector).toHaveBeenCalledWith({
      type: "transitions",
      transitions: transitionsResponse,
    });
  });

  it("forwards an empty list rather than throwing when the backend call fails", async () => {
    const { TransitionAction } = await import("../src/actions/obsTransition.js");
    const action = new TransitionAction() as unknown as PiRelayLike;
    const { clearPairingState } = await import("@nomnomzbot/streamdeck-shared");
    await clearPairingState();

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listSceneTransitions" }));

    expect(sendToPropertyInspector).toHaveBeenCalledWith({ type: "transitions", transitions: [] });
  });
});

describe("FilterAction property inspector — onSendToPlugin listSourceFilters", () => {
  it("fetches GET /automation/v1/obs/source-filters?sourceName= and forwards the filter list", async () => {
    sourceFiltersResponse = [{ name: "Color Correction", kind: "color_filter", enabled: true, index: 0 }];
    const { FilterAction } = await import("../src/actions/obsFilter.js");
    const action = new FilterAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listSourceFilters", sourceName: "Webcam" }));

    expect(requestedUrls).toContain("/automation/v1/obs/source-filters?sourceName=Webcam");
    expect(sendToPropertyInspector).toHaveBeenCalledWith({
      type: "sourceFilters",
      sourceFilters: sourceFiltersResponse,
    });
  });

  it("forwards an empty list rather than throwing when the backend call fails", async () => {
    const { FilterAction } = await import("../src/actions/obsFilter.js");
    const action = new FilterAction() as unknown as PiRelayLike;
    const { clearPairingState } = await import("@nomnomzbot/streamdeck-shared");
    await clearPairingState();

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "listSourceFilters", sourceName: "Webcam" }));

    expect(sendToPropertyInspector).toHaveBeenCalledWith({ type: "sourceFilters", sourceFilters: [] });
  });

  it("ignores an unrelated request type without touching the OBS endpoints", async () => {
    const { FilterAction } = await import("../src/actions/obsFilter.js");
    const action = new FilterAction() as unknown as PiRelayLike;

    await action.onSendToPlugin(fakeSendToPluginEvent({ type: "getHost" }));

    expect(requestedUrls).not.toContain("/automation/v1/obs/source-filters");
  });
});
