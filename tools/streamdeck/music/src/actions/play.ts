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
import { nowPlayingState } from "../nowPlaying/state.js";

@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-play" })
export class PlayAction extends MusicAction {
  protected readonly actionType = "music_play";
  protected readonly iconName = "play";

  protected override isBlockedByProvider(_settings: JsonObject): boolean {
    return nowPlayingState.current?.canResume === false;
  }
}
