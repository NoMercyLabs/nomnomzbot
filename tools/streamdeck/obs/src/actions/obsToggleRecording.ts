// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
