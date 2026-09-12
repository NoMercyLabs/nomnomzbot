// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi } from "vitest";
import { ObsLiveState } from "../src/state.js";

describe("ObsLiveState", () => {
  it("starts with every state unknown (null), not a false default", () => {
    const state = new ObsLiveState();

    expect(state.isStreaming).toBeNull();
    expect(state.isRecording).toBeNull();
    expect(state.isInputMuted("Mic/Aux")).toBeUndefined();
  });

  it("applyStreaming updates isStreaming and notifies listeners", () => {
    const state = new ObsLiveState();
    const listener = vi.fn();
    state.onChange(listener);

    state.applyStreaming({ active: true });

    expect(state.isStreaming).toBe(true);
    expect(listener).toHaveBeenCalledTimes(1);

    state.applyStreaming({ active: false });

    expect(state.isStreaming).toBe(false);
    expect(listener).toHaveBeenCalledTimes(2);
  });

  it("applyRecording updates both active and paused independently", () => {
    const state = new ObsLiveState();

    state.applyRecording({ active: true, paused: false });
    expect(state.isRecording).toBe(true);
    expect(state.isRecordingPaused).toBe(false);

    state.applyRecording({ active: true, paused: true });
    expect(state.isRecording).toBe(true);
    expect(state.isRecordingPaused).toBe(true);

    state.applyRecording({ active: false, paused: false });
    expect(state.isRecording).toBe(false);
  });

  it("applyMute tracks mute state per input name, independently of other inputs", () => {
    const state = new ObsLiveState();

    state.applyMute({ inputName: "Mic/Aux", muted: true });
    state.applyMute({ inputName: "Desktop Audio", muted: false });

    expect(state.isInputMuted("Mic/Aux")).toBe(true);
    expect(state.isInputMuted("Desktop Audio")).toBe(false);
    expect(state.isInputMuted("Unknown Input")).toBeUndefined();

    state.applyMute({ inputName: "Mic/Aux", muted: false });
    expect(state.isInputMuted("Mic/Aux")).toBe(false);
    // Unaffected by the update to a different input.
    expect(state.isInputMuted("Desktop Audio")).toBe(false);
  });

  it("notifies listeners on every apply, regardless of which state changed", () => {
    const state = new ObsLiveState();
    const listener = vi.fn();
    state.onChange(listener);

    state.applyStreaming({ active: true });
    state.applyRecording({ active: true, paused: false });
    state.applyMute({ inputName: "Mic/Aux", muted: true });

    expect(listener).toHaveBeenCalledTimes(3);
  });
});
