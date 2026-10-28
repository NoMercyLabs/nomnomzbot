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

interface SeekSettings extends JsonObject {
  positionSeconds?: number;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-seek" })
export class SeekAction extends MusicAction<SeekSettings> {
  protected readonly actionType = "music_seek";
  protected readonly iconName = "seek";

  protected override resolveParams(settings: SeekSettings): Record<string, unknown> {
    return { positionSeconds: settings.positionSeconds ?? 0 };
  }

  protected override isBlockedByProvider(_settings: SeekSettings): boolean {
    return nowPlayingState.current?.canSeek === false;
  }
}
