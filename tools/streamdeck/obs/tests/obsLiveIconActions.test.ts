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
import type { WillAppearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

// S-STREAMDECK-OBS-REMAINDER: proves the live icon actually SWAPS when OBS's real state changes —
// not just that the key renders once, and not just that the invoke wire body is right (that's
// obsRemainderActions.test.ts's job). renderIconKey is mocked so the assertion reads the exact
// (iconName, dimmed) arguments each render call, rather than trying to decode base64 SVG output.
const renderIconKey = vi.fn(
  (iconName: string, _backgroundColor: string, dimmed: boolean) => `mock:${iconName}:${dimmed}`,
);
vi.mock("../src/keyRenderer.js", () => ({
  renderIconKey,
  DEFAULT_BACKGROUND: "#1a1a1a",
}));

function fakeWillAppear(settings: JsonObject = {}): {
  event: WillAppearEvent<JsonObject>;
  setImage: ReturnType<typeof vi.fn>;
} {
  const setImage = vi.fn(() => Promise.resolve());
  const event = {
    action: { setImage },
    payload: { settings },
  } as unknown as WillAppearEvent<JsonObject>;
  return { event, setImage };
}

function lastIconCall(): { iconName: string; dimmed: boolean } {
  const call = renderIconKey.mock.calls.at(-1);
  if (!call) throw new Error("renderIconKey was never called");
  return { iconName: call[0] as string, dimmed: call[2] as boolean };
}

describe("ToggleStreamingAction (src/actions/obsToggleStreaming.ts) — live icon swap", () => {
  it("shows stream-start while idle and swaps to stream-stop once OBS starts streaming", async () => {
    renderIconKey.mockClear();
    const { ToggleStreamingAction } = await import("../src/actions/obsToggleStreaming.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyStreaming({ active: false });

    const action = new ToggleStreamingAction();
    const { event, setImage } = fakeWillAppear();
    await action.onWillAppear(event);

    expect(setImage).toHaveBeenCalledTimes(1);
    expect(lastIconCall()).toEqual({ iconName: "stream-start", dimmed: false });

    // The real signal this slice adds: an `obs.streaming.changed` push (routed through
    // ObsLiveState.applyStreaming) redraws every appeared key with NO further action from the key.
    obsLiveState.applyStreaming({ active: true });
    await Promise.resolve();

    expect(setImage).toHaveBeenCalledTimes(2);
    expect(lastIconCall()).toEqual({ iconName: "stream-stop", dimmed: false });
  });
});

describe("StartStreamingAction (src/actions/obsStartStreaming.ts) — live icon swap", () => {
  it("dims once OBS is already streaming, undims once it stops", async () => {
    renderIconKey.mockClear();
    const { StartStreamingAction } = await import("../src/actions/obsStartStreaming.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyStreaming({ active: false });

    const action = new StartStreamingAction();
    const { event } = fakeWillAppear();
    await action.onWillAppear(event);

    expect(lastIconCall()).toEqual({ iconName: "stream-start", dimmed: false });

    obsLiveState.applyStreaming({ active: true });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "stream-start", dimmed: true });

    obsLiveState.applyStreaming({ active: false });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "stream-start", dimmed: false });
  });
});

describe("StopStreamingAction (src/actions/obsStopStreaming.ts) — live icon swap", () => {
  it("dims once OBS is confirmed not streaming", async () => {
    renderIconKey.mockClear();
    const { StopStreamingAction } = await import("../src/actions/obsStopStreaming.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyStreaming({ active: true });

    const action = new StopStreamingAction();
    const { event } = fakeWillAppear();
    await action.onWillAppear(event);

    expect(lastIconCall()).toEqual({ iconName: "stream-stop", dimmed: false });

    obsLiveState.applyStreaming({ active: false });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "stream-stop", dimmed: true });
  });
});

describe("ToggleRecordingAction (src/actions/obsToggleRecording.ts) — live icon swap", () => {
  it("shows record-start while idle and swaps to record-stop once OBS starts recording", async () => {
    renderIconKey.mockClear();
    const { ToggleRecordingAction } = await import("../src/actions/obsToggleRecording.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyRecording({ active: false, paused: false });

    const action = new ToggleRecordingAction();
    const { event } = fakeWillAppear();
    await action.onWillAppear(event);

    expect(lastIconCall()).toEqual({ iconName: "record-start", dimmed: false });

    obsLiveState.applyRecording({ active: true, paused: false });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "record-stop", dimmed: false });
  });
});

describe("StartRecordingAction / StopRecordingAction — live icon swap", () => {
  it("StartRecordingAction dims once already recording", async () => {
    renderIconKey.mockClear();
    const { StartRecordingAction } = await import("../src/actions/obsStartRecording.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyRecording({ active: false, paused: false });

    const action = new StartRecordingAction();
    const { event } = fakeWillAppear();
    await action.onWillAppear(event);
    expect(lastIconCall()).toEqual({ iconName: "record-start", dimmed: false });

    obsLiveState.applyRecording({ active: true, paused: false });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "record-start", dimmed: true });
  });

  it("StopRecordingAction dims once confirmed not recording", async () => {
    renderIconKey.mockClear();
    const { StopRecordingAction } = await import("../src/actions/obsStopRecording.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyRecording({ active: true, paused: false });

    const action = new StopRecordingAction();
    const { event } = fakeWillAppear();
    await action.onWillAppear(event);
    expect(lastIconCall()).toEqual({ iconName: "record-stop", dimmed: false });

    obsLiveState.applyRecording({ active: false, paused: false });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "record-stop", dimmed: true });
  });
});

describe("ToggleMuteAction (src/actions/obsToggleMute.ts) — live icon swap", () => {
  it("renders bright while the configured input is muted, dimmed once unmuted", async () => {
    renderIconKey.mockClear();
    const { ToggleMuteAction } = await import("../src/actions/obsToggleMute.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyMute({ inputName: "Mic/Aux", muted: true });

    const action = new ToggleMuteAction();
    const { event } = fakeWillAppear({ inputName: "Mic/Aux" });
    await action.onWillAppear(event);

    expect(lastIconCall()).toEqual({ iconName: "mic-mute", dimmed: false });

    obsLiveState.applyMute({ inputName: "Mic/Aux", muted: false });
    await Promise.resolve();
    expect(lastIconCall()).toEqual({ iconName: "mic-mute", dimmed: true });
  });

  it("a different key's input is unaffected by another input's mute change", async () => {
    renderIconKey.mockClear();
    const { ToggleMuteAction } = await import("../src/actions/obsToggleMute.js");
    const { obsLiveState } = await import("../src/state.js");
    obsLiveState.applyMute({ inputName: "Desktop Audio", muted: false });

    const action = new ToggleMuteAction();
    const { event } = fakeWillAppear({ inputName: "Desktop Audio" });
    await action.onWillAppear(event);
    expect(lastIconCall()).toEqual({ iconName: "mic-mute", dimmed: true });

    obsLiveState.applyMute({ inputName: "Mic/Aux", muted: true });
    await Promise.resolve();
    // Redrawn (ObsLiveState notifies on every change, not just this input's), but the icon state
    // for THIS key's input ("Desktop Audio") is unchanged.
    expect(lastIconCall()).toEqual({ iconName: "mic-mute", dimmed: true });
  });
});
