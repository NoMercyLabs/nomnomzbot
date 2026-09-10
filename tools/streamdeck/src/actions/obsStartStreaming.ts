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

/** Invokes the dashboard's `obs_streaming` pipeline (obs-control.md §5) with `action: "start"`. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs-start-streaming" })
export class StartStreamingAction extends ObsAction {
  protected readonly pipelineName = "obs_streaming";
  protected readonly iconName = "stream-start";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "start" };
  }
}
