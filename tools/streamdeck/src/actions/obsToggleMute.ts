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

export interface ToggleMuteSettings extends JsonObject {
  inputName?: string;
}

/** Invokes the dashboard's `obs_input_mute` pipeline (obs-control.md §5) with `toggle: true` — flips
 * whichever audio input this key names (mic, desktop audio, …), chosen from a live dropdown
 * (ui/input-picker.html, S-STREAMDECK-OBS-REMAINDER) fed by `GET /automation/v1/obs/inputs`, rather
 * than setting an absolute muted/unmuted state, matching a single-key toggle press. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs-toggle-mute" })
export class ToggleMuteAction extends ObsAction<ToggleMuteSettings> {
  protected readonly pipelineName = "obs_input_mute";
  protected readonly iconName = "mic-mute";

  protected override resolveParams(settings: ToggleMuteSettings): Record<string, unknown> {
    return { input: settings.inputName ?? "", toggle: true };
  }
}
