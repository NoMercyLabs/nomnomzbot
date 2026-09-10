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

// Same proof style as tests/musicInstantiatedActions.test.ts (S-STREAMDECK-ACTION-TESTS batch 1),
// covering the "single-setting" music-tray actions that batch left open: volumeUp/volumeDown
// derive a `step` from settings (default 10), volumeMute derives an `unmuteVolume` (default 50) —
// each gets a real instantiated-class run with the SDK default AND an explicit non-default value,
// so the resolveParams mapping itself is proven, not just that some call fired. cycleRepeat is
// included too: despite the plan file's "single-setting" grouping, its resolveParams is the
// MusicAction base's untouched default (no settings feed its invoke payload at all — the repeat
// mode is server-driven and only reflected back into the key's title/icon) — proven here as a
// genuine no-arg action alongside the settings-derived ones, not assumed from the plan wording.
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
  const { setPairingState } = await import("../src/connection/tokenStore.js");
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

interface ActionLike {
  onKeyDown: (ev: KeyDownEvent<JsonObject>) => Promise<void>;
}

describe("VolumeUpAction (src/actions/volumeUp.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no step setting invokes music_volume_up with the default step of 10", async () => {
    const { VolumeUpAction } = await import("../src/actions/volumeUp.js");
    await (new VolumeUpAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_volume_up",
      variables: { step: "10" },
    });
  });

  it("onKeyDown with an explicit step setting invokes music_volume_up with that step, not the default", async () => {
    const { VolumeUpAction } = await import("../src/actions/volumeUp.js");
    await (new VolumeUpAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ step: 25 }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_volume_up",
      variables: { step: "25" },
    });
  });
});

describe("VolumeDownAction (src/actions/volumeDown.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no step setting invokes music_volume_down with the default step of 10", async () => {
    const { VolumeDownAction } = await import("../src/actions/volumeDown.js");
    await (new VolumeDownAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_volume_down",
      variables: { step: "10" },
    });
  });

  it("onKeyDown with an explicit step setting invokes music_volume_down with that step, not the default", async () => {
    const { VolumeDownAction } = await import("../src/actions/volumeDown.js");
    await (new VolumeDownAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ step: 5 }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_volume_down",
      variables: { step: "5" },
    });
  });
});

describe("VolumeMuteAction (src/actions/volumeMute.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no unmuteVolume setting invokes music_volume_mute with the default of 50", async () => {
    const { VolumeMuteAction } = await import("../src/actions/volumeMute.js");
    await (new VolumeMuteAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_volume_mute",
      variables: { unmuteVolume: "50" },
    });
  });

  it("onKeyDown with an explicit unmuteVolume setting invokes music_volume_mute with that value, not the default", async () => {
    const { VolumeMuteAction } = await import("../src/actions/volumeMute.js");
    await (new VolumeMuteAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ unmuteVolume: 35 }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_volume_mute",
      variables: { unmuteVolume: "35" },
    });
  });
});

describe("CycleRepeatAction (src/actions/cycleRepeat.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_cycle_repeat with no params — MusicAction's default resolveParams (repeat mode is server-driven, not settings-derived)", async () => {
    const { CycleRepeatAction } = await import("../src/actions/cycleRepeat.js");
    await (new CycleRepeatAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_cycle_repeat", variables: {} });
  });
});
