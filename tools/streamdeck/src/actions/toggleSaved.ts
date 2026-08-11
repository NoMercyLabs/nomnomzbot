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

/** Live key: favorite-outline/favorite-filled icon, flipped on every song.changed's isSaved field —
 * the base class redraws it on every live update automatically. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-toggle-saved" })
export class ToggleSavedAction extends MusicAction {
  protected readonly actionType = "music_toggle_saved";
  protected readonly iconName = "favorite-outline";

  protected override getIconName(_settings: JsonObject): string {
    return nowPlayingState.current?.isSaved ? "favorite-filled" : "favorite-outline";
  }
}
