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
import { nowPlayingState } from "../nowPlaying/state.js";

@action({ UUID: "bot.nomnomzbot.streamdeck.music-previous" })
export class PreviousAction extends MusicAction {
  protected readonly actionType = "music_previous";
  protected readonly iconName = "previous";

  protected override isBlockedByProvider(_settings: JsonObject): boolean {
    return nowPlayingState.current?.canSkipPrevious === false;
  }
}
