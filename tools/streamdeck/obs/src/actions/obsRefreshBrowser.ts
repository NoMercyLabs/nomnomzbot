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

export interface RefreshBrowserSettings extends JsonObject {
  inputName?: string;
}

/** Invokes the dashboard's `obs_refresh_browser` pipeline (obs-control.md §5) — reloads a browser
 * source with no cache. Reuses the input dropdown (ui/input-picker.html,
 * S-STREAMDECK-OBS-REMAINDER) fed by `GET /automation/v1/obs/inputs`, same as Toggle Mute — the
 * picker isn't filtered to browser-kind inputs specifically (the automation plane's input list
 * doesn't distinguish input kinds beyond the raw `kind` string), so any input can be chosen; firing
 * this on a non-browser input is a harmless no-op on OBS's side. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-refresh-browser" })
export class RefreshBrowserAction extends ObsAction<RefreshBrowserSettings> {
  protected readonly pipelineName = "obs_refresh_browser";
  protected readonly iconName = "previous";

  protected override resolveParams(settings: RefreshBrowserSettings): Record<string, unknown> {
    return { input: settings.inputName ?? "" };
  }
}
