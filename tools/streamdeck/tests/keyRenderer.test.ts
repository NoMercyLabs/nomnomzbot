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
import { renderPlayPauseKey, renderNowPlayingKey, renderIconKey } from "../src/nowPlaying/keyRenderer.js";
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
    volumePercent: 100,
    albumArtUrl: null,
    ...overrides,
  };
}

describe("renderPlayPauseKey", () => {
  it("draws the pause glyph and the elapsed time while playing", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: true, positionMs: 65_000 }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain("M8 6V18M16 18"); // pause.svg's distinguishing path data
    expect(svg).not.toContain("M11.2941"); // not play.svg's
    expect(svg).toContain("1:05");
  });

  it("draws the play glyph while paused", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: false, positionMs: 5_000 }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain("M11.2941"); // play.svg's distinguishing path data
    expect(svg).not.toContain("M8 6V18M16 18"); // not pause.svg's
    expect(svg).toContain("0:05");
  });

  it("shows 0:00 before any state has been applied", () => {
    const state = new NowPlayingState();

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain("0:00");
  });

  it("dims the pause glyph when the provider currently blocks pausing", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: true, canPause: false }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain('opacity="0.35"');
  });

  it("does not dim the pause glyph when canResume is false while playing (irrelevant direction)", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: true, canPause: true, canResume: false }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).not.toContain('opacity="0.35"');
  });

  it("dims the play glyph when the provider currently blocks resuming", () => {
    const state = new NowPlayingState();
    state.apply(payload({ isPlaying: false, canResume: false }));

    const svg = decode(renderPlayPauseKey(state));

    expect(svg).toContain('opacity="0.35"');
  });
});

describe("renderIconKey", () => {
  it("renders at full opacity by default", () => {
    const svg = decode(renderIconKey("shuffle-off"));

    expect(svg).not.toContain('opacity="0.35"');
  });

  it("dims the icon when marked as blocked by the provider", () => {
    const svg = decode(renderIconKey("shuffle-off", "#1a1a1a", true));

    expect(svg).toContain('opacity="0.35"');
  });
});

describe("renderNowPlayingKey", () => {
  it("embeds the given cover art data URI as the key's image", () => {
    const state = new NowPlayingState();
    state.apply(payload({ title: "Song", artist: "Artist" }));
    const art = "data:image/jpeg;base64,ZmFrZQ==";

    const svg = decode(renderNowPlayingKey(state, art, 0));

    expect(svg).toContain(`href="${art}"`);
  });

  it("renders no image element when there is no cover art", () => {
    const state = new NowPlayingState();
    state.apply(payload({ title: "Song", artist: "Artist" }));

    const svg = decode(renderNowPlayingKey(state, null, 0));

    expect(svg).not.toContain("<image");
  });

  it("centers a short title/artist with no scroll offset", () => {
    const state = new NowPlayingState();
    state.apply(payload({ title: "Hi", artist: "Yo" }));

    const svg = decode(renderNowPlayingKey(state, null, 5));

    expect(svg).toContain("Hi — Yo");
    expect(svg).toContain('text-anchor="middle"');
  });

  it("scrolls a long title/artist by advancing with tick, looping the label", () => {
    const state = new NowPlayingState();
    const longLabel = "A Very Long Track Title That Cannot Possibly Fit";
    state.apply(payload({ title: longLabel, artist: "An Equally Long Artist Name" }));

    const atZero = decode(renderNowPlayingKey(state, null, 0));
    const atLater = decode(renderNowPlayingKey(state, null, 20));

    // Looped (label appears twice) and NOT centered — a scrolling marquee, not a static label.
    expect((atZero.match(new RegExp(longLabel, "g")) ?? []).length).toBe(2);
    expect(atZero).not.toContain('text-anchor="middle"');
    // Different ticks produce different x offsets — it's actually moving.
    const xOf = (svg: string) => svg.match(/<text x="(-?[\d.]+)"/)?.[1];
    expect(xOf(atZero)).not.toBe(xOf(atLater));
  });

  it("shows a placeholder when nothing is playing", () => {
    const state = new NowPlayingState();

    const svg = decode(renderNowPlayingKey(state, null, 0));

    expect(svg).toContain("Nothing playing");
  });
});
