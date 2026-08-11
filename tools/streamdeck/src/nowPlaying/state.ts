// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { NowPlayingPayload } from "../connection/automationClient.js";

/** Position-anchor extrapolation (widget-sdk.md §9, reused client-side per streamdeck-plugin.md P4). */
export class NowPlayingState {
  private anchor: NowPlayingPayload | null = null;
  private receivedAtMs = 0;
  private listeners = new Set<() => void>();

  apply(payload: NowPlayingPayload): void {
    this.anchor = payload;
    this.receivedAtMs = Date.now();
    for (const listener of this.listeners) listener();
  }

  onChange(listener: () => void): void {
    this.listeners.add(listener);
  }

  get current(): NowPlayingPayload | null {
    return this.anchor;
  }

  /** Extrapolated elapsed position right now — frozen while paused, drifting forward while playing. */
  elapsedMs(): number {
    if (!this.anchor) return 0;
    if (!this.anchor.isPlaying) return this.anchor.positionMs;
    return this.anchor.positionMs + (Date.now() - this.receivedAtMs);
  }

  formattedElapsed(): string {
    const totalSeconds = Math.max(0, Math.floor(this.elapsedMs() / 1000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  }
}

export const nowPlayingState = new NowPlayingState();
