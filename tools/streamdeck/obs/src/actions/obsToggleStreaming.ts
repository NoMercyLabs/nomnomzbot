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

/** Invokes the dashboard's `obs_streaming` pipeline (obs-control.md §5) with `action: "toggle"` — one
 * key that starts or stops streaming depending on OBS's current state.
 *
 * S-STREAMDECK-OBS-REMAINDER: the icon itself swaps between `stream-start`/`stream-stop` to show what
 * pressing the key will DO next (start while idle, stop while live), fed by the `obs.streaming.changed`
 * live push — a static icon can't express that for a single toggle key. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-toggle-streaming" })
export class ToggleStreamingAction extends ObsLiveIconAction {
  protected readonly pipelineName = "obs_streaming";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "toggle" };
  }

  protected override liveIcon(): { iconName: string; dimmed: boolean } {
    return { iconName: obsLiveState.isStreaming === true ? "stream-stop" : "stream-start", dimmed: false };
  }
}
