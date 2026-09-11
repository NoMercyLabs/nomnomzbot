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
