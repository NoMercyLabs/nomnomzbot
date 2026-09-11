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

export interface TransitionSettings extends JsonObject {
  transition?: string;
  studio?: boolean;
  durationMs?: number;
}

/** Invokes the dashboard's `obs_transition` pipeline (obs-control.md §5) — sets the current
 * transition and/or fires a studio-mode transition. Configured from ui/transition-picker.html
 * (S-STREAMDECK-OBS-REMAINDER): a transition dropdown fed by
 * `GET /automation/v1/obs/scene-transitions` (optional — leave unset to only fire the studio
 * transition with whatever transition is already active), a "trigger studio transition" toggle,
 * and an optional duration override in milliseconds. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-transition" })
export class TransitionAction extends ObsAction<TransitionSettings> {
  protected readonly pipelineName = "obs_transition";
  protected readonly iconName = "shuffle";

  protected override resolveParams(settings: TransitionSettings): Record<string, unknown> {
    const params: Record<string, unknown> = { studio: settings.studio ?? false };
    if (settings.transition) params.transition = settings.transition;
    if (settings.durationMs) params.duration_ms = settings.durationMs;
    return params;
  }
}
