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
import type { NowPlayingPayload } from "@nomnomzbot/streamdeck-shared";

// Same proof style as tests/musicInstantiatedActions.test.ts (batch 1) and
// tests/musicVolumeAndRepeatActions.test.ts (batch 2) — S-STREAMDECK-ACTION-TESTS batch 3, the
// remaining "multi-field/picker" music-tray actions: setVolume, seek, setRepeat, setShuffle,
// transferDevice, addToPlaylist, removeFromPlaylist, nowPlaying. Each gets a real instantiated
// @action(...)-decorated class run through onKeyDown with realistic settings, asserting the actual
// resolveParams-derived invoke payload — cross-checked against each action's own source, not
// copy-pasted. seek/setRepeat/setShuffle also override isBlockedByProvider against live
// nowPlayingState (the real singleton, not a mock) — real per-key disallow logic, not the default
// "never blocked" every other action in this batch inherits unmodified.
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

interface ActionLike {
  onKeyDown: (ev: KeyDownEvent<JsonObject>) => Promise<void>;
}

interface BlockableActionLike extends ActionLike {
  isBlockedByProvider: (settings: JsonObject) => boolean;
}

/** Minimal but fully-shaped NowPlayingPayload — every required field filled with an inert value so
 * only the field under test (a `can*` disallow flag) needs to be passed as an override. */
function fakeNowPlayingPayload(overrides: Partial<NowPlayingPayload> = {}): NowPlayingPayload {
  return {
    title: "Track",
    artist: "Artist",
    durationMs: 200_000,
    positionMs: 10_000,
    isPlaying: true,
    shuffleEnabled: false,
    repeatMode: "off",
    isSaved: false,
    serverTime: new Date().toISOString(),
    volumePercent: 50,
    albumArtUrl: null,
    ...overrides,
  };
}

describe("SetVolumeAction (src/actions/setVolume.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no volume setting invokes music_set_volume with the default of 50", async () => {
    const { SetVolumeAction } = await import("../src/actions/setVolume.js");
    await (new SetVolumeAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_set_volume",
      variables: { volume: "50" },
    });
  });

  it("onKeyDown with an explicit volume setting invokes music_set_volume with that value, not the default", async () => {
    const { SetVolumeAction } = await import("../src/actions/setVolume.js");
    await (new SetVolumeAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ volume: 72 }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_set_volume",
      variables: { volume: "72" },
    });
  });
});

describe("SeekAction (src/actions/seek.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no positionSeconds setting invokes music_seek with the default of 0", async () => {
    const { SeekAction } = await import("../src/actions/seek.js");
    await (new SeekAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_seek",
      variables: { positionSeconds: "0" },
    });
  });

  it("onKeyDown with an explicit positionSeconds setting invokes music_seek with that value, not the default", async () => {
    const { SeekAction } = await import("../src/actions/seek.js");
    await (new SeekAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ positionSeconds: 137 }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_seek",
      variables: { positionSeconds: "137" },
    });
  });

  it("isBlockedByProvider reflects the real nowPlayingState singleton's canSeek flag, not a mock", async () => {
    const { SeekAction } = await import("../src/actions/seek.js");
    const { nowPlayingState } = await import("../src/nowPlaying/state.js");
    const action = new SeekAction() as unknown as BlockableActionLike;

    nowPlayingState.apply(fakeNowPlayingPayload({ canSeek: true }));
    expect(action.isBlockedByProvider({})).toBe(false);

    nowPlayingState.apply(fakeNowPlayingPayload({ canSeek: false }));
    expect(action.isBlockedByProvider({})).toBe(true);
  });
});

describe("SetRepeatAction (src/actions/setRepeat.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no mode setting invokes music_set_repeat with the default of \"off\"", async () => {
    const { SetRepeatAction } = await import("../src/actions/setRepeat.js");
    await (new SetRepeatAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_set_repeat",
      variables: { mode: "off" },
    });
  });

  it("onKeyDown with an explicit mode setting invokes music_set_repeat with that mode, not the default", async () => {
    const { SetRepeatAction } = await import("../src/actions/setRepeat.js");
    await (new SetRepeatAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ mode: "track" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_set_repeat",
      variables: { mode: "track" },
    });
  });

  it("isBlockedByProvider reflects the real nowPlayingState singleton's canSetRepeat flag, not a mock", async () => {
    const { SetRepeatAction } = await import("../src/actions/setRepeat.js");
    const { nowPlayingState } = await import("../src/nowPlaying/state.js");
    const action = new SetRepeatAction() as unknown as BlockableActionLike;

    nowPlayingState.apply(fakeNowPlayingPayload({ canSetRepeat: true }));
    expect(action.isBlockedByProvider({})).toBe(false);

    nowPlayingState.apply(fakeNowPlayingPayload({ canSetRepeat: false }));
    expect(action.isBlockedByProvider({})).toBe(true);
  });
});

describe("SetShuffleAction (src/actions/setShuffle.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no enabled setting invokes music_set_shuffle with the default of true", async () => {
    const { SetShuffleAction } = await import("../src/actions/setShuffle.js");
    await (new SetShuffleAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_set_shuffle",
      variables: { enabled: "true" },
    });
  });

  it("onKeyDown with an explicit enabled setting invokes music_set_shuffle with that value, not the default", async () => {
    const { SetShuffleAction } = await import("../src/actions/setShuffle.js");
    await (new SetShuffleAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ enabled: false }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_set_shuffle",
      variables: { enabled: "false" },
    });
  });

  it("isBlockedByProvider reflects the real nowPlayingState singleton's canSetShuffle flag, not a mock", async () => {
    const { SetShuffleAction } = await import("../src/actions/setShuffle.js");
    const { nowPlayingState } = await import("../src/nowPlaying/state.js");
    const action = new SetShuffleAction() as unknown as BlockableActionLike;

    nowPlayingState.apply(fakeNowPlayingPayload({ canSetShuffle: true }));
    expect(action.isBlockedByProvider({})).toBe(false);

    nowPlayingState.apply(fakeNowPlayingPayload({ canSetShuffle: false }));
    expect(action.isBlockedByProvider({})).toBe(true);
  });
});

describe("TransferDeviceAction (src/actions/transferDevice.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no deviceId setting invokes music_transfer_device with an empty deviceId", async () => {
    const { TransferDeviceAction } = await import("../src/actions/transferDevice.js");
    await (new TransferDeviceAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_transfer_device",
      variables: { deviceId: "" },
    });
  });

  it("onKeyDown with a deviceId chosen from the property-inspector's device picker invokes music_transfer_device with that id", async () => {
    const { TransferDeviceAction } = await import("../src/actions/transferDevice.js");
    await (new TransferDeviceAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ deviceId: "device-abc123" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_transfer_device",
      variables: { deviceId: "device-abc123" },
    });
  });
});

describe("AddToPlaylistAction (src/actions/addToPlaylist.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no playlistId setting invokes music_add_to_playlist with an empty playlistId", async () => {
    const { AddToPlaylistAction } = await import("../src/actions/addToPlaylist.js");
    await (new AddToPlaylistAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_add_to_playlist",
      variables: { playlistId: "" },
    });
  });

  it("onKeyDown with a playlistId chosen from the property-inspector's playlist picker invokes music_add_to_playlist with that id", async () => {
    const { AddToPlaylistAction } = await import("../src/actions/addToPlaylist.js");
    await (new AddToPlaylistAction() as ActionLike).onKeyDown(fakeKeyDownEvent({ playlistId: "playlist-987" }));

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_add_to_playlist",
      variables: { playlistId: "playlist-987" },
    });
  });
});

describe("RemoveFromPlaylistAction (src/actions/removeFromPlaylist.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown with no playlistId setting invokes music_remove_from_playlist with an empty playlistId", async () => {
    const { RemoveFromPlaylistAction } = await import("../src/actions/removeFromPlaylist.js");
    await (new RemoveFromPlaylistAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_remove_from_playlist",
      variables: { playlistId: "" },
    });
  });

  it("onKeyDown with a playlistId chosen from the property-inspector's playlist picker invokes music_remove_from_playlist with that id", async () => {
    const { RemoveFromPlaylistAction } = await import("../src/actions/removeFromPlaylist.js");
    await (new RemoveFromPlaylistAction() as ActionLike).onKeyDown(
      fakeKeyDownEvent({ playlistId: "playlist-123" }),
    );

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.body).toEqual({
      pipelineName: "music_remove_from_playlist",
      variables: { playlistId: "playlist-123" },
    });
  });
});

describe("NowPlayingAction (src/actions/nowPlaying.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_play_pause with no params — same actionType as PlayPauseAction, MusicAction's default resolveParams (the live cover-art marquee is onWillAppear-only, not part of the key press)", async () => {
    const { NowPlayingAction } = await import("../src/actions/nowPlaying.js");
    const action = new NowPlayingAction() as ActionLike;
    await action.onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_play_pause", variables: {} });
  });
});
