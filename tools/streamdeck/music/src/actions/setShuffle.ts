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

import { action} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";

interface SetShuffleSettings extends JsonObject {
  enabled?: boolean;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-set-shuffle" })
export class SetShuffleAction extends MusicAction<SetShuffleSettings> {
  protected readonly actionType = "music_set_shuffle";
  protected readonly iconName = "shuffle";

  protected override resolveParams(settings: SetShuffleSettings): Record<string, unknown> {
    return { enabled: settings.enabled ?? true };
  }

  protected override isBlockedByProvider(_settings: SetShuffleSettings): boolean {
    return nowPlayingState.current?.canSetShuffle === false;
  }
}
