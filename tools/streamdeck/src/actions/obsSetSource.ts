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

export interface SetSourceSettings extends JsonObject {
  scene?: string;
  source?: string;
  visible?: boolean;
}

/** Invokes the dashboard's `obs_set_source` pipeline (obs-control.md §5) — shows/hides one source
 * inside one scene. Configured from two dependent dropdowns (ui/scene-source-picker.html,
 * S-STREAMDECK-OBS-REMAINDER): scene first (`GET /automation/v1/obs/scenes`), then the source
 * placed in that scene (`GET /automation/v1/obs/scene-items?sceneName=`), re-fetched whenever the
 * scene changes. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs-set-source" })
export class SetSourceAction extends ObsAction<SetSourceSettings> {
  protected readonly pipelineName = "obs_set_source";
  protected readonly iconName = "device";

  protected override resolveParams(settings: SetSourceSettings): Record<string, unknown> {
    return {
      scene: settings.scene ?? "",
      source: settings.source ?? "",
      visible: settings.visible ?? true,
    };
  }
}
