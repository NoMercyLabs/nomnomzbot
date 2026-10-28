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

export interface ScreenshotSettings extends JsonObject {
  inputName?: string;
  format?: string;
}

/** Invokes the dashboard's `obs_screenshot` pipeline (obs-control.md §5) — captures a source; the
 * result lands in the pipeline variable `obs.screenshot`, not on the key itself, so this is a
 * fire-and-forget trigger (like a save/clip action) rather than something the key can preview.
 * Configured from ui/screenshot-picker.html (S-STREAMDECK-OBS-REMAINDER): the source dropdown
 * (`GET /automation/v1/obs/inputs`) plus a hardcoded format dropdown. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-screenshot" })
export class ScreenshotAction extends ObsAction<ScreenshotSettings> {
  protected readonly pipelineName = "obs_screenshot";
  protected readonly iconName = "record-stop";

  protected override resolveParams(settings: ScreenshotSettings): Record<string, unknown> {
    return { source: settings.inputName ?? "", format: settings.format ?? "png" };
  }
}
