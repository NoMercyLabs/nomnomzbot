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

// Same proof as tests/playAction.test.ts and tests/obsInstantiatedActions.test.ts
// (S-STREAMDECK-ACTION-TESTS remainder, batch 1): the ten simplest music-tray actions — every one
// whose resolveParams is the MusicAction base's default (returns {}), i.e. no settings-derived
// payload at all — are real, unmocked, @action(...)-decorated classes. This instantiates each and
// drives its actual onKeyDown, so a regression in the decorator/SingletonAction lifecycle machinery,
// or in an action's own actionType string, fails here rather than only in a mapping-level test.
// Single-setting actions (volumeUp/volumeDown/volumeMute, cycleRepeat) and multi-field/picker actions
// (setVolume, seek, setRepeat, setShuffle, transferDevice, addToPlaylist, removeFromPlaylist,
// nowPlaying) are left for a later batch — see SHORTCOMINGS-EXECUTION-PLAN.md.
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

function fakeKeyDownEvent(): KeyDownEvent<JsonObject> {
  const showAlert = (): Promise<void> => Promise.resolve();
  return {
    action: { showAlert },
    payload: { settings: {} },
  } as unknown as KeyDownEvent<JsonObject>;
}

interface ActionLike {
  onKeyDown: (ev: KeyDownEvent<JsonObject>) => Promise<void>;
}

describe("PauseAction (src/actions/pause.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_pause with no params — MusicAction's default resolveParams", async () => {
    const { PauseAction } = await import("../src/actions/pause.js");
    await (new PauseAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_pause", variables: {} });
  });
});

describe("PlayPauseAction (src/actions/playPause.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_play_pause with no params — MusicAction's default resolveParams", async () => {
    const { PlayPauseAction } = await import("../src/actions/playPause.js");
    await (new PlayPauseAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_play_pause", variables: {} });
  });
});

describe("NextAction (src/actions/next.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_next with no params — MusicAction's default resolveParams", async () => {
    const { NextAction } = await import("../src/actions/next.js");
    await (new NextAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_next", variables: {} });
  });
});

describe("PreviousAction (src/actions/previous.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_previous with no params — MusicAction's default resolveParams", async () => {
    const { PreviousAction } = await import("../src/actions/previous.js");
    await (new PreviousAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_previous", variables: {} });
  });
});

describe("ToggleShuffleAction (src/actions/toggleShuffle.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_toggle_shuffle with no params — MusicAction's default resolveParams", async () => {
    const { ToggleShuffleAction } = await import("../src/actions/toggleShuffle.js");
    await (new ToggleShuffleAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_toggle_shuffle", variables: {} });
  });
});

describe("ToggleSavedAction (src/actions/toggleSaved.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_toggle_saved with no params — MusicAction's default resolveParams", async () => {
    const { ToggleSavedAction } = await import("../src/actions/toggleSaved.js");
    await (new ToggleSavedAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_toggle_saved", variables: {} });
  });
});

describe("FollowArtistAction (src/actions/followArtist.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_follow_artist with no params — MusicAction's default resolveParams", async () => {
    const { FollowArtistAction } = await import("../src/actions/followArtist.js");
    await (new FollowArtistAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_follow_artist", variables: {} });
  });
});

describe("UnfollowArtistAction (src/actions/unfollowArtist.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_unfollow_artist with no params — MusicAction's default resolveParams", async () => {
    const { UnfollowArtistAction } = await import("../src/actions/unfollowArtist.js");
    await (new UnfollowArtistAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_unfollow_artist", variables: {} });
  });
});

describe("SaveTrackAction (src/actions/saveTrack.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_save_track with no params — MusicAction's default resolveParams", async () => {
    const { SaveTrackAction } = await import("../src/actions/saveTrack.js");
    await (new SaveTrackAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_save_track", variables: {} });
  });
});

describe("UnsaveTrackAction (src/actions/unsaveTrack.ts) — real @action(...)-decorated class", () => {
  it("onKeyDown invokes music_unsave_track with no params — MusicAction's default resolveParams", async () => {
    const { UnsaveTrackAction } = await import("../src/actions/unsaveTrack.js");
    await (new UnsaveTrackAction() as ActionLike).onKeyDown(fakeKeyDownEvent());

    expect(receivedInvokes).toHaveLength(1);
    expect(receivedInvokes[0]!.path).toBe("/automation/v1/invoke");
    expect(receivedInvokes[0]!.body).toEqual({ pipelineName: "music_unsave_track", variables: {} });
  });
});
