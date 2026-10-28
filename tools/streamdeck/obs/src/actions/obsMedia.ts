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

export interface MediaSettings extends JsonObject {
  inputName?: string;
  mediaAction?: string;
}

/** Invokes the dashboard's `obs_media` pipeline (obs-control.md §5) — drives a media source
 * (play/pause/stop/restart/next/previous). Configured from ui/media-picker.html
 * (S-STREAMDECK-OBS-REMAINDER): the input dropdown (`GET /automation/v1/obs/inputs`) plus a
 * hardcoded verb dropdown — the verb set is fixed and small enough not to need a backend list. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-media" })
export class MediaAction extends ObsAction<MediaSettings> {
  protected readonly pipelineName = "obs_media";
  protected readonly iconName = "play-pause";

  protected override resolveParams(settings: MediaSettings): Record<string, unknown> {
    return { input: settings.inputName ?? "", action: settings.mediaAction ?? "play" };
  }
}
