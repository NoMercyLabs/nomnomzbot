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
import { MusicAction } from "./musicAction.js";

interface VolumeStepSettings extends JsonObject {
  step?: number;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-volume-down" })
export class VolumeDownAction extends MusicAction<VolumeStepSettings> {
  protected readonly actionType = "music_volume_down";

  protected override resolveParams(settings: VolumeStepSettings): Record<string, unknown> {
    return { step: settings.step ?? 10 };
  }
}
