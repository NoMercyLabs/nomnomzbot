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

import { action, type WillAppearEvent} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";

const TITLES: Record<string, string> = { off: "Repeat\nOff", track: "Repeat\nTrack", context: "Repeat\nAll" };

/** Live key: title shows the current repeat mode, updated on every song.changed. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-cycle-repeat" })
export class CycleRepeatAction extends MusicAction {
  protected readonly actionType = "music_cycle_repeat";
  protected readonly iconName = "repeat";

  protected override isBlockedByProvider(_settings: JsonObject): boolean {
    return nowPlayingState.current?.canSetRepeat === false;
  }

  override async onWillAppear(ev: WillAppearEvent<JsonObject>): Promise<void> {
    await super.onWillAppear(ev);
    const paint = () => void ev.action.setTitle(TITLES[nowPlayingState.current?.repeatMode ?? "off"] ?? "Repeat");
    nowPlayingState.onChange(paint);
    paint();
  }
}
