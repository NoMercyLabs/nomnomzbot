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
import { MusicAction } from "./musicAction.js";

interface VolumeStepSettings extends JsonObject {
  step?: number;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-volume-up" })
export class VolumeUpAction extends MusicAction<VolumeStepSettings> {
  protected readonly actionType = "music_volume_up";
  protected readonly iconName = "volume-up";

  protected override resolveParams(settings: VolumeStepSettings): Record<string, unknown> {
    return { step: settings.step ?? 10 };
  }
}
