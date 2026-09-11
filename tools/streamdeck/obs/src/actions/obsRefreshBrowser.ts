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
