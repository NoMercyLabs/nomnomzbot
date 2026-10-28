// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { action } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { ObsLiveIconAction } from "./obsLiveIconAction.js";
import { obsLiveState } from "../state.js";

/** Invokes the dashboard's `obs_recording` pipeline (obs-control.md §5) with `action: "toggle"` — one
 * key that starts or stops recording depending on OBS's current state.
 *
 * S-STREAMDECK-OBS-REMAINDER: the icon swaps between `record-start`/`record-stop` to show what
 * pressing the key will DO next, fed by the `obs.recording.changed` live push. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-toggle-recording" })
export class ToggleRecordingAction extends ObsLiveIconAction {
  protected readonly pipelineName = "obs_recording";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "toggle" };
  }

  protected override liveIcon(): { iconName: string; dimmed: boolean } {
    return { iconName: obsLiveState.isRecording === true ? "record-stop" : "record-start", dimmed: false };
  }
}
