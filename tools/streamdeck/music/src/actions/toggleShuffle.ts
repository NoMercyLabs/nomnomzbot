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

/** Live key: shuffle-off/shuffle-on icon, flipped on every song.changed (streamdeck-plugin.md P4);
 * dims when Spotify's own actions.disallows currently blocks toggling shuffle (ad break, restricted
 * market, non-Premium account) — the base class redraws it on every live update automatically. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-toggle-shuffle" })
export class ToggleShuffleAction extends MusicAction {
  protected readonly actionType = "music_toggle_shuffle";
  protected readonly iconName = "shuffle-off";

  protected override getIconName(_settings: JsonObject): string {
    return nowPlayingState.current?.shuffleEnabled ? "shuffle-on" : "shuffle-off";
  }

  protected override isBlockedByProvider(_settings: JsonObject): boolean {
    return nowPlayingState.current?.canSetShuffle === false;
  }
}
