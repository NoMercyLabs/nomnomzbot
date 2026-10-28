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
