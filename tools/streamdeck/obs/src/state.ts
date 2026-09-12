// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type {
  ObsStreamingStatePayload,
  ObsRecordingStatePayload,
  ObsMuteStatePayload,
} from "@nomnomzbot/streamdeck-shared";

/**
 * Live OBS state for the tray's icon-state tiles (S-STREAMDECK-OBS-REMAINDER) — mirrors
 * {@link "../../music/src/nowPlaying/state.js".NowPlayingState}'s role for the music plugin: one
 * shared, WS-fed store every key instance reads from instead of tracking its own copy. Unlike
 * now-playing there's no position to extrapolate — streaming/recording/mute are plain booleans, so
 * `apply*` + `onChange` is the whole shape.
 *
 * Mute state is per-input (an OBS channel typically has several audio inputs — mic, desktop audio,
 * …), so it's keyed by input name rather than a single flag.
 */
export class ObsLiveState {
  private streaming: ObsStreamingStatePayload | null = null;
  private recording: ObsRecordingStatePayload | null = null;
  private muteByInput = new Map<string, boolean>();
  private listeners = new Set<() => void>();

  applyStreaming(payload: ObsStreamingStatePayload): void {
    this.streaming = payload;
    this.notify();
  }

  applyRecording(payload: ObsRecordingStatePayload): void {
    this.recording = payload;
    this.notify();
  }

  applyMute(payload: ObsMuteStatePayload): void {
    this.muteByInput.set(payload.inputName, payload.muted);
    this.notify();
  }

  onChange(listener: () => void): void {
    this.listeners.add(listener);
  }

  /** Whether OBS is currently streaming. `null` means "unknown yet" (no seed read has landed) —
   * callers treat that the same as "not streaming" for rendering, but it's kept distinct so a key
   * that appears before the first seed doesn't briefly claim to know OBS is idle. */
  get isStreaming(): boolean | null {
    return this.streaming?.active ?? null;
  }

  get isRecording(): boolean | null {
    return this.recording?.active ?? null;
  }

  get isRecordingPaused(): boolean {
    return this.recording?.paused ?? false;
  }

  /** `undefined` when this input's mute state has never been observed (not yet seeded, or the input
   * name doesn't match any input OBS actually has). */
  isInputMuted(inputName: string): boolean | undefined {
    return this.muteByInput.get(inputName);
  }

  private notify(): void {
    for (const listener of this.listeners) listener();
  }
}

export const obsLiveState = new ObsLiveState();
