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

// Final batch of S-STREAMDECK-ACTION-TESTS: same proof as obsInstantiatedActions.test.ts (which covers
// ToggleReplayBufferAction/ToggleVirtualCamAction), extended to the remaining 8 obs*.ts actions that were
// previously only exercised at the resolveParams-mapping level in obsActions.test.ts. Instantiating the
// real @action(...)-decorated classes and driving their actual onKeyDown catches a regression in the
// decorator/SingletonAction lifecycle machinery itself, not just the params mapping.
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

describe("SwitchSceneAction (src/actions/obsSwitchScene.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_switch_scene with the configured scene name", async () => {
    const { SwitchSceneAction } = await import("../src/actions/obsSwitchScene.js");

    await new SwitchSceneAction().onKeyDown(fakeKeyDownEvent({ scene: "Game Scene" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_switch_scene",
      variables: { scene: "Game Scene" },
    });
  });

  it("onKeyDown falls back to an empty scene when unconfigured", async () => {
    const { SwitchSceneAction } = await import("../src/actions/obsSwitchScene.js");

    await new SwitchSceneAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_switch_scene",
      variables: { scene: "" },
    });
  });
});

describe("ToggleMuteAction (src/actions/obsToggleMute.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_input_mute with the configured input and toggle:true", async () => {
    const { ToggleMuteAction } = await import("../src/actions/obsToggleMute.js");

    await new ToggleMuteAction().onKeyDown(fakeKeyDownEvent({ inputName: "Mic/Aux" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    // AutomationClient.invoke stringifies every variable (String(v)) — true becomes "true", which
    // ObsActionBase.GetBool's string branch on the backend parses back with bool.TryParse.
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_input_mute",
      variables: { input: "Mic/Aux", toggle: "true" },
    });
  });
});

describe("StartStreamingAction (src/actions/obsStartStreaming.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_streaming with action: start", async () => {
    const { StartStreamingAction } = await import("../src/actions/obsStartStreaming.js");

    await new StartStreamingAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_streaming",
      variables: { action: "start" },
    });
  });
});

describe("StopStreamingAction (src/actions/obsStopStreaming.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_streaming with action: stop", async () => {
    const { StopStreamingAction } = await import("../src/actions/obsStopStreaming.js");

    await new StopStreamingAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_streaming",
      variables: { action: "stop" },
    });
  });
});

describe("ToggleStreamingAction (src/actions/obsToggleStreaming.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_streaming with action: toggle", async () => {
    const { ToggleStreamingAction } = await import("../src/actions/obsToggleStreaming.js");

    await new ToggleStreamingAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_streaming",
      variables: { action: "toggle" },
    });
  });
});

describe("StartRecordingAction (src/actions/obsStartRecording.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_recording with action: start", async () => {
    const { StartRecordingAction } = await import("../src/actions/obsStartRecording.js");

    await new StartRecordingAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_recording",
      variables: { action: "start" },
    });
  });
});

describe("StopRecordingAction (src/actions/obsStopRecording.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_recording with action: stop", async () => {
    const { StopRecordingAction } = await import("../src/actions/obsStopRecording.js");

    await new StopRecordingAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_recording",
      variables: { action: "stop" },
    });
  });
});

describe("ToggleRecordingAction (src/actions/obsToggleRecording.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes obs_recording with action: toggle", async () => {
    const { ToggleRecordingAction } = await import("../src/actions/obsToggleRecording.js");

    await new ToggleRecordingAction().onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_recording",
      variables: { action: "toggle" },
    });
  });
});
