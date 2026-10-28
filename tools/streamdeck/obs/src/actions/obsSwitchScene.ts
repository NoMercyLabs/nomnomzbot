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

export interface SwitchSceneSettings extends JsonObject {
  scene?: string;
}

/** Invokes the dashboard's `obs_switch_scene` pipeline (obs-control.md §5) with this key's configured
 * scene name — chosen from a live dropdown (ui/scene-picker.html, S-STREAMDECK-OBS-REMAINDER) fed by
 * `GET /automation/v1/obs/scenes`. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-switch-scene" })
export class SwitchSceneAction extends ObsAction<SwitchSceneSettings> {
  protected readonly pipelineName = "obs_switch_scene";
  protected readonly iconName = "scene";

  protected override resolveParams(settings: SwitchSceneSettings): Record<string, unknown> {
    return { scene: settings.scene ?? "" };
  }
}
