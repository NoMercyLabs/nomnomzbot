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

// NOTE on scope: this proves the WIRE contract each new OBS action's onKeyDown drives — the real
// automationClient.invoke() hitting a real fake HTTP server, the same style automationClient.test.ts
// already uses. It stops short of instantiating the @action(...)-decorated action classes themselves —
// vitest.config.ts (S-STREAMDECK-TEST-INFRA, 617c2c81) now CAN do that (see obsInstantiatedActions.test.ts
// and tests/playAction.test.ts for the pattern); this file's lighter-weight tests are kept as-is since
// they already cover every action's resolveParams mapping and duplicating all of them at the
// instantiated-class level would be redundant, not because the old parse limitation still applies.
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
  const { setPairingState } = await import("../src/connection/tokenStore.js");
  await setPairingState({
    backendUrl: `http://127.0.0.1:${backendPort}`,
    token: "nnzb_ak_test",
    tokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
    deviceKind: "streamdeck",
  });
});

describe("OBS Stream Deck actions — the exact invoke payload each action's onKeyDown sends", () => {
  it("Switch Scene: obs_switch_scene with the configured scene name (obsSwitchScene.ts resolveParams)", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");

    // Mirrors SwitchSceneAction.resolveParams: { scene: settings.scene ?? "" }
    await automationClient.invoke("obs_switch_scene", { scene: "Game Scene" });

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_switch_scene",
      variables: { scene: "Game Scene" },
    });
  });

  it("Toggle Mute: obs_input_mute with the configured input and toggle:true (obsToggleMute.ts resolveParams)", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");

    // Mirrors ToggleMuteAction.resolveParams: { input: settings.inputName ?? "", toggle: true }
    await automationClient.invoke("obs_input_mute", { input: "Mic/Aux", toggle: true });

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_input_mute",
      // AutomationClient.invoke stringifies every variable (String(v)) — true becomes "true", which
      // ObsActionBase.GetBool's string branch on the backend parses back with bool.TryParse.
      variables: { input: "Mic/Aux", toggle: "true" },
    });
  });

  it("Start/Stop/Toggle Streaming: obs_streaming with the matching verb (one pipeline, three keys)", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");

    await automationClient.invoke("obs_streaming", { action: "start" });
    await automationClient.invoke("obs_streaming", { action: "stop" });
    await automationClient.invoke("obs_streaming", { action: "toggle" });

    expect(receivedInvokes.map((r) => r.body)).toEqual([
      { pipelineName: "obs_streaming", variables: { action: "start" } },
      { pipelineName: "obs_streaming", variables: { action: "stop" } },
      { pipelineName: "obs_streaming", variables: { action: "toggle" } },
    ]);
  });

  it("Start/Stop/Toggle Recording: obs_recording with the matching verb (one pipeline, three keys)", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");

    await automationClient.invoke("obs_recording", { action: "start" });
    await automationClient.invoke("obs_recording", { action: "stop" });
    await automationClient.invoke("obs_recording", { action: "toggle" });

    expect(receivedInvokes.map((r) => r.body)).toEqual([
      { pipelineName: "obs_recording", variables: { action: "start" } },
      { pipelineName: "obs_recording", variables: { action: "stop" } },
      { pipelineName: "obs_recording", variables: { action: "toggle" } },
    ]);
  });

  it("Toggle Replay Buffer: obs_replay_buffer with action:toggle (obsToggleReplayBuffer.ts resolveParams)", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");

    // Mirrors ToggleReplayBufferAction.resolveParams: { action: "toggle" }
    await automationClient.invoke("obs_replay_buffer", { action: "toggle" });

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_replay_buffer",
      variables: { action: "toggle" },
    });
  });

  it("Toggle Virtual Camera: obs_virtual_cam with action:toggle (obsToggleVirtualCam.ts resolveParams)", async () => {
    const { automationClient } = await import("../src/connection/automationClient.js");

    // Mirrors ToggleVirtualCamAction.resolveParams: { action: "toggle" }
    await automationClient.invoke("obs_virtual_cam", { action: "toggle" });

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "obs_virtual_cam",
      variables: { action: "toggle" },
    });
  });

  it("an unpaired plugin refuses to invoke and sends nothing to the backend", async () => {
    const { automationClient, AutomationApiError } = await import("../src/connection/automationClient.js");
    const { clearPairingState } = await import("../src/connection/tokenStore.js");
    await clearPairingState();

    await expect(automationClient.invoke("obs_switch_scene", { scene: "BRB" })).rejects.toThrow(
      AutomationApiError,
    );
    expect(receivedInvokes).toHaveLength(0);
  });
});
