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

interface SeekSettings extends JsonObject {
  positionSeconds?: number;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-seek" })
export class SeekAction extends MusicAction<SeekSettings> {
  protected readonly actionType = "music_seek";

  protected override resolveParams(settings: SeekSettings): Record<string, unknown> {
    return { positionSeconds: settings.positionSeconds ?? 0 };
  }
}
