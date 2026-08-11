// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";

interface SetVolumeSettings extends JsonObject {
  volume?: number;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-set-volume" })
export class SetVolumeAction extends MusicAction<SetVolumeSettings> {
  protected readonly actionType = "music_set_volume";

  protected override resolveParams(settings: SetVolumeSettings): Record<string, unknown> {
    return { volume: settings.volume ?? 50 };
  }
}
