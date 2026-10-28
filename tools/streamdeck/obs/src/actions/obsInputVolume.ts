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
import { ObsAction } from "./obsAction.js";

export interface InputVolumeSettings extends JsonObject {
  inputName?: string;
  volumeDb?: number;
}

/** Invokes the dashboard's `obs_input_volume` pipeline (obs-control.md §5) — sets an input's
 * volume in dB (the more common unit for a fixed-level key, vs. the multiplier form the backend
 * also accepts). Configured from ui/input-volume-picker.html (S-STREAMDECK-OBS-REMAINDER): the
 * input dropdown (`GET /automation/v1/obs/inputs`) plus a dB number field. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-input-volume" })
export class InputVolumeAction extends ObsAction<InputVolumeSettings> {
  protected readonly pipelineName = "obs_input_volume";
  protected readonly iconName = "volume";

  protected override resolveParams(settings: InputVolumeSettings): Record<string, unknown> {
    return { input: settings.inputName ?? "", volume_db: settings.volumeDb ?? 0 };
  }
}
