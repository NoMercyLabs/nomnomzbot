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
