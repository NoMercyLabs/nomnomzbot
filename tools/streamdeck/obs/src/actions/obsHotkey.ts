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

export interface HotkeySettings extends JsonObject {
  hotkeyName?: string;
}

/** Invokes the dashboard's `obs_hotkey` pipeline (obs-control.md §5) — fires an OBS hotkey by its
 * registered name. Free-text field (ui/simple-param.html): OBS-WS exposes no "list hotkeys"
 * request, so there is nothing to build a picker from — confirmed against the live obs-websocket
 * protocol this session. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-hotkey" })
export class HotkeyAction extends ObsAction<HotkeySettings> {
  protected readonly pipelineName = "obs_hotkey";
  protected readonly iconName = "seek";

  protected override resolveParams(settings: HotkeySettings): Record<string, unknown> {
    return { hotkey_name: settings.hotkeyName ?? "" };
  }
}
