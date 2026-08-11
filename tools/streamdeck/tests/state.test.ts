// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi, afterEach } from "vitest";
import { NowPlayingState } from "../src/nowPlaying/state.js";
import type { NowPlayingPayload } from "../src/connection/automationClient.js";

function payload(overrides: Partial<NowPlayingPayload> = {}): NowPlayingPayload {
  return {
    title: "Song",
    artist: "Artist",
    durationMs: 200_000,
    positionMs: 10_000,
    isPlaying: true,
    shuffleEnabled: false,
    repeatMode: "off",
    isSaved: null,
    serverTime: new Date().toISOString(),
    ...overrides,
  };
}

describe("NowPlayingState", () => {
  afterEach(() => vi.useRealTimers());

  it("extrapolates elapsed position forward while playing", () => {
    vi.useFakeTimers();
    const now = new Date("2026-08-11T12:00:00Z");
    vi.setSystemTime(now);
    const state = new NowPlayingState();

    state.apply(payload({ positionMs: 10_000, isPlaying: true }));
    vi.setSystemTime(new Date(now.getTime() + 5_000));

    expect(state.elapsedMs()).toBe(15_000);
  });

  it("freezes the elapsed position while paused", () => {
    vi.useFakeTimers();
    const now = new Date("2026-08-11T12:00:00Z");
    vi.setSystemTime(now);
    const state = new NowPlayingState();

    state.apply(payload({ positionMs: 10_000, isPlaying: false }));
    vi.setSystemTime(new Date(now.getTime() + 5_000));

    expect(state.elapsedMs()).toBe(10_000);
  });

  it("formats elapsed time as m:ss", () => {
    const state = new NowPlayingState();
    state.apply(payload({ positionMs: 65_000, isPlaying: false }));

    expect(state.formattedElapsed()).toBe("1:05");
  });

  it("notifies listeners on every apply", () => {
    const state = new NowPlayingState();
    const listener = vi.fn();
    state.onChange(listener);

    state.apply(payload());
    state.apply(payload({ isPlaying: false }));

    expect(listener).toHaveBeenCalledTimes(2);
  });
});
