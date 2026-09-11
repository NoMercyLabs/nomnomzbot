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

// S-STREAMDECK-OBS-REMAINDER: same proof as obsInstantiatedActions.test.ts / obsInstantiatedActions2.test.ts
// (which cover the original 6 obs_* action types already wired to the plugin), extended to the 12 newly
// added obs_* pipeline action types (of the 13 the backend implements — obs_request_batch is deferred,
// see the plugin.ts / manifest.json history for why: its `requests` array field can't survive the
// auto-provisioned pipeline's whole-value `{field}` template substitution, which always re-wraps a
// resolved value as a JSON string, never an array). Instantiates the real @action(...)-decorated classes
// and drives their actual onKeyDown against a real fake HTTP server, asserting the exact
// POST /automation/v1/invoke body — proving both the field-name mapping AND that resolveParams's optional
// fields are only sent when actually configured.
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

function fakeKeyDownEvent(settings: JsonObject = {}): KeyDownEvent<JsonObject> {
  const showAlert = (): Promise<void> => Promise.resolve();
  return {
    action: { showAlert },
    payload: { settings },
  } as unknown as KeyDownEvent<JsonObject>;
}

describe("SetPreviewSceneAction (src/actions/obsSetPreviewScene.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_set_preview_scene with the configured scene name", async () => {
    const { SetPreviewSceneAction } = await import("../src/actions/obsSetPreviewScene.js");

    await new SetPreviewSceneAction().onKeyDown(fakeKeyDownEvent({ scene: "Starting Soon" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_set_preview_scene",
      variables: { scene: "Starting Soon" },
    });
  });

  it("onKeyDown falls back to an empty scene when unconfigured", async () => {
    const { SetPreviewSceneAction } = await import("../src/actions/obsSetPreviewScene.js");

    await new SetPreviewSceneAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_set_preview_scene",
      variables: { scene: "" },
    });
  });
});

describe("SetSourceAction (src/actions/obsSetSource.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_set_source with scene, source and visible", async () => {
    const { SetSourceAction } = await import("../src/actions/obsSetSource.js");

    await new SetSourceAction().onKeyDown(
      fakeKeyDownEvent({ scene: "Game Scene", source: "Webcam", visible: false }),
    );

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_set_source",
      variables: { scene: "Game Scene", source: "Webcam", visible: "false" },
    });
  });

  it("onKeyDown defaults visible to true when unconfigured", async () => {
    const { SetSourceAction } = await import("../src/actions/obsSetSource.js");

    await new SetSourceAction().onKeyDown(fakeKeyDownEvent({ scene: "Game Scene", source: "Webcam" }));

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_set_source",
      variables: { scene: "Game Scene", source: "Webcam", visible: "true" },
    });
  });
});

describe("FilterAction (src/actions/obsFilter.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_filter with source, filter and enabled", async () => {
    const { FilterAction } = await import("../src/actions/obsFilter.js");

    await new FilterAction().onKeyDown(
      fakeKeyDownEvent({ source: "Webcam", filter: "Color Correction", enabled: false }),
    );

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_filter",
      variables: { source: "Webcam", filter: "Color Correction", enabled: "false" },
    });
  });
});

describe("TransitionAction (src/actions/obsTransition.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_transition with transition, studio and duration_ms when all are set", async () => {
    const { TransitionAction } = await import("../src/actions/obsTransition.js");

    await new TransitionAction().onKeyDown(
      fakeKeyDownEvent({ transition: "Fade", studio: true, durationMs: 500 }),
    );

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_transition",
      variables: { transition: "Fade", studio: "true", duration_ms: "500" },
    });
  });

  it("onKeyDown omits transition and duration_ms when unset, sending only studio", async () => {
    const { TransitionAction } = await import("../src/actions/obsTransition.js");

    await new TransitionAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_transition",
      variables: { studio: "false" },
    });
  });
});

describe("InputVolumeAction (src/actions/obsInputVolume.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_input_volume with the configured input and volume_db", async () => {
    const { InputVolumeAction } = await import("../src/actions/obsInputVolume.js");

    await new InputVolumeAction().onKeyDown(fakeKeyDownEvent({ inputName: "Mic/Aux", volumeDb: -6 }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_input_volume",
      variables: { input: "Mic/Aux", volume_db: "-6" },
    });
  });
});

describe("MediaAction (src/actions/obsMedia.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_media with the configured input and action verb", async () => {
    const { MediaAction } = await import("../src/actions/obsMedia.js");

    await new MediaAction().onKeyDown(fakeKeyDownEvent({ inputName: "Intro.mp4", mediaAction: "restart" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_media",
      variables: { input: "Intro.mp4", action: "restart" },
    });
  });

  it("onKeyDown defaults the action verb to play when unconfigured", async () => {
    const { MediaAction } = await import("../src/actions/obsMedia.js");

    await new MediaAction().onKeyDown(fakeKeyDownEvent({ inputName: "Intro.mp4" }));

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_media",
      variables: { input: "Intro.mp4", action: "play" },
    });
  });
});

describe("HotkeyAction (src/actions/obsHotkey.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_hotkey with the configured hotkey name", async () => {
    const { HotkeyAction } = await import("../src/actions/obsHotkey.js");

    await new HotkeyAction().onKeyDown(fakeKeyDownEvent({ hotkeyName: "OBSBasic.StartStreaming" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_hotkey",
      variables: { hotkey_name: "OBSBasic.StartStreaming" },
    });
  });
});

describe("RefreshBrowserAction (src/actions/obsRefreshBrowser.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_refresh_browser with the configured input", async () => {
    const { RefreshBrowserAction } = await import("../src/actions/obsRefreshBrowser.js");

    await new RefreshBrowserAction().onKeyDown(fakeKeyDownEvent({ inputName: "Chat Overlay" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_refresh_browser",
      variables: { input: "Chat Overlay" },
    });
  });
});

describe("ScreenshotAction (src/actions/obsScreenshot.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_screenshot with the configured source and format", async () => {
    const { ScreenshotAction } = await import("../src/actions/obsScreenshot.js");

    await new ScreenshotAction().onKeyDown(fakeKeyDownEvent({ inputName: "Webcam", format: "jpg" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_screenshot",
      variables: { source: "Webcam", format: "jpg" },
    });
  });

  it("onKeyDown defaults format to png when unconfigured", async () => {
    const { ScreenshotAction } = await import("../src/actions/obsScreenshot.js");

    await new ScreenshotAction().onKeyDown(fakeKeyDownEvent({ inputName: "Webcam" }));

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_screenshot",
      variables: { source: "Webcam", format: "png" },
    });
  });
});

describe("SaveReplayAction (src/actions/obsSaveReplay.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_save_replay with no variables", async () => {
    const { SaveReplayAction } = await import("../src/actions/obsSaveReplay.js");

    await new SaveReplayAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_save_replay",
      variables: {},
    });
  });
});

describe("RequestAction (src/actions/obsRequest.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_request with request_type and request_data when both are set", async () => {
    const { RequestAction } = await import("../src/actions/obsRequest.js");

    await new RequestAction().onKeyDown(
      fakeKeyDownEvent({ requestType: "SetCurrentProgramScene", requestData: '{"sceneName":"BRB"}' }),
    );

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_request",
      variables: { request_type: "SetCurrentProgramScene", request_data: '{"sceneName":"BRB"}' },
    });
  });

  it("onKeyDown omits request_data when unset", async () => {
    const { RequestAction } = await import("../src/actions/obsRequest.js");

    await new RequestAction().onKeyDown(fakeKeyDownEvent({ requestType: "SaveReplayBuffer" }));

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_request",
      variables: { request_type: "SaveReplayBuffer" },
    });
  });
});

describe("CallVendorAction (src/actions/obsCallVendor.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_call_vendor with vendor, request_type and request_data when all are set", async () => {
    const { CallVendorAction } = await import("../src/actions/obsCallVendor.js");

    await new CallVendorAction().onKeyDown(
      fakeKeyDownEvent({ vendor: "obs-shaderfilter", requestType: "Refresh", requestData: "{}" }),
    );

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_call_vendor",
      variables: { vendor: "obs-shaderfilter", request_type: "Refresh", request_data: "{}" },
    });
  });

  it("onKeyDown omits request_data when unset", async () => {
    const { CallVendorAction } = await import("../src/actions/obsCallVendor.js");

    await new CallVendorAction().onKeyDown(
      fakeKeyDownEvent({ vendor: "obs-shaderfilter", requestType: "Refresh" }),
    );

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_call_vendor",
      variables: { vendor: "obs-shaderfilter", request_type: "Refresh" },
    });
  });
});
