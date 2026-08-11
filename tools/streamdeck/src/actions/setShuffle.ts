// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";

interface SetShuffleSettings extends JsonObject {
  enabled?: boolean;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-set-shuffle" })
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
