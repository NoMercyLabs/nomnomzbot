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

/** Invokes the dashboard's `obs_streaming` pipeline (obs-control.md §5) with `action: "start"`.
 *
 * S-STREAMDECK-OBS-REMAINDER: dims once OBS is ALREADY streaming (fed by `obs.streaming.changed`) —
 * pressing it again would be a no-op, so the key signals that rather than staying indistinguishable
 * from "ready to start". */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-start-streaming" })
export class StartStreamingAction extends ObsLiveIconAction {
  protected readonly pipelineName = "obs_streaming";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "start" };
  }

  protected override liveIcon(): { iconName: string; dimmed: boolean } {
    return { iconName: "stream-start", dimmed: obsLiveState.isStreaming === true };
  }
}
