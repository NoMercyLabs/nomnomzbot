// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action, type WillAppearEvent, type KeyAction } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";

/** Native 2-state key: shuffle-off/shuffle-on images, flipped on every song.changed (streamdeck-plugin.md P4).
 * Cast to KeyAction: this manifest entry has no Encoder controller, so it's never a DialAction at runtime. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-toggle-shuffle" })
export class ToggleShuffleAction extends MusicAction {
  protected readonly actionType = "music_toggle_shuffle";

  override async onWillAppear(ev: WillAppearEvent<JsonObject>): Promise<void> {
    await super.onWillAppear(ev);
    const key = ev.action as KeyAction<JsonObject>;
    nowPlayingState.onChange(() => void key.setState(nowPlayingState.current?.shuffleEnabled ? 1 : 0));
    await key.setState(nowPlayingState.current?.shuffleEnabled ? 1 : 0);
  }
}
