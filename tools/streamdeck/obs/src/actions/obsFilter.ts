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

export interface FilterSettings extends JsonObject {
  source?: string;
  filter?: string;
  enabled?: boolean;
}

/** Invokes the dashboard's `obs_filter` pipeline (obs-control.md §5) — enables/disables one filter
 * on one source. Configured from two dependent dropdowns (ui/source-filter-picker.html,
 * S-STREAMDECK-OBS-REMAINDER): source first (`GET /automation/v1/obs/inputs`), then the filters on
 * that source (`GET /automation/v1/obs/source-filters?sourceName=`), re-fetched whenever the source
 * changes. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-filter" })
export class FilterAction extends ObsAction<FilterSettings> {
  protected readonly pipelineName = "obs_filter";
  protected readonly iconName = "repeat";

  protected override resolveParams(settings: FilterSettings): Record<string, unknown> {
    return {
      source: settings.source ?? "",
      filter: settings.filter ?? "",
      enabled: settings.enabled ?? true,
    };
  }
}
