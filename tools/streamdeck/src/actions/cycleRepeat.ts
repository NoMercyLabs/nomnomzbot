// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action, type WillAppearEvent} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";

const TITLES: Record<string, string> = { off: "Repeat\nOff", track: "Repeat\nTrack", context: "Repeat\nAll" };

/** Live key: title shows the current repeat mode, updated on every song.changed. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-cycle-repeat" })
export class CycleRepeatAction extends MusicAction {
  protected readonly actionType = "music_cycle_repeat";

  override async onWillAppear(ev: WillAppearEvent<JsonObject>): Promise<void> {
    await super.onWillAppear(ev);
    const paint = () => void ev.action.setTitle(TITLES[nowPlayingState.current?.repeatMode ?? "off"] ?? "Repeat");
    nowPlayingState.onChange(paint);
    paint();
  }
}
