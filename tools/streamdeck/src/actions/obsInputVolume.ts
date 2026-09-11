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

export interface InputVolumeSettings extends JsonObject {
  inputName?: string;
  volumeDb?: number;
}

/** Invokes the dashboard's `obs_input_volume` pipeline (obs-control.md §5) — sets an input's
 * volume in dB (the more common unit for a fixed-level key, vs. the multiplier form the backend
 * also accepts). Configured from ui/input-volume-picker.html (S-STREAMDECK-OBS-REMAINDER): the
 * input dropdown (`GET /automation/v1/obs/inputs`) plus a dB number field. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs-input-volume" })
export class InputVolumeAction extends ObsAction<InputVolumeSettings> {
  protected readonly pipelineName = "obs_input_volume";
  protected readonly iconName = "volume";

  protected override resolveParams(settings: InputVolumeSettings): Record<string, unknown> {
    return { input: settings.inputName ?? "", volume_db: settings.volumeDb ?? 0 };
  }
}
