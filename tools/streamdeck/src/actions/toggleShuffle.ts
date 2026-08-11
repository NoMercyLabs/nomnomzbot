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

/** Live key: shuffle-off/shuffle-on icon, flipped on every song.changed (streamdeck-plugin.md P4);
 * dims when Spotify's own actions.disallows currently blocks toggling shuffle (ad break, restricted
 * market, non-Premium account) — the base class redraws it on every live update automatically. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-toggle-shuffle" })
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
