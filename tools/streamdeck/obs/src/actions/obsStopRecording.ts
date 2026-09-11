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
import { ObsAction } from "./obsAction.js";

/** Invokes the dashboard's `obs_recording` pipeline (obs-control.md §5) with `action: "stop"`. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-stop-recording" })
export class StopRecordingAction extends ObsAction {
  protected readonly pipelineName = "obs_recording";
  protected readonly iconName = "record-stop";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "stop" };
  }
}
