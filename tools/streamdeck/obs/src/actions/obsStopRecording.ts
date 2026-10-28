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

/** Invokes the dashboard's `obs_recording` pipeline (obs-control.md §5) with `action: "stop"`.
 *
 * S-STREAMDECK-OBS-REMAINDER: dims once OBS is confirmed NOT recording — pressing it would be a
 * no-op. Stays undimmed while the state is still unknown (no seed read has landed yet). */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-stop-recording" })
export class StopRecordingAction extends ObsLiveIconAction {
  protected readonly pipelineName = "obs_recording";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "stop" };
  }

  protected override liveIcon(): { iconName: string; dimmed: boolean } {
    return { iconName: "record-stop", dimmed: obsLiveState.isRecording === false };
  }
}
