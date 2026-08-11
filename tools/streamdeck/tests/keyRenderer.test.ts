// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect } from "vitest";
import { NowPlayingState } from "../src/nowPlaying/state.js";
import { renderPlayPauseKey } from "../src/nowPlaying/keyRenderer.js";
import type { NowPlayingPayload } from "../src/connection/automationClient.js";

function decode(dataUri: string): string {
  const base64 = dataUri.replace("data:image/svg+xml;base64,", "");
  return Buffer.from(base64, "base64").toString("utf-8");
}

function payload(overrides: Partial<NowPlayingPayload> = {}): NowPlayingPayload {
  return {
    title: "Song",
    artist: "Artist",
    durationMs: 200_000,
    positionMs: 65_000,
    isPlaying: true,
    shuffleEnabled: false,
    repeatMode: "off",
    isSaved: null,
    serverTime: new Date().toISOString(),
    ...overrides,
  };
}

describe("renderPlayPauseKey", () => {
  it("draws the pause glyph and the elapsed time while playing", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: true, positionMs: 65_000 }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain("<rect"); // pause bars
    expect(svg).toContain("1:05");
  });

  it("draws the play glyph while paused", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: false, positionMs: 5_000 }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain("<polygon"); // play triangle
    expect(svg).toContain("0:05");
  });

  it("shows 0:00 before any state has been applied", () => {
    const state = new NowPlayingState();

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain("0:00");
  });
});
