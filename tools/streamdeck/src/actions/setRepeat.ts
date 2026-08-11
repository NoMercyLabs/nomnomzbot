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

interface SetRepeatSettings extends JsonObject {
  mode?: "off" | "track" | "context";
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-set-repeat" })
export class SetRepeatAction extends MusicAction<SetRepeatSettings> {
  protected readonly actionType = "music_set_repeat";
  protected readonly iconName = "repeat";

  protected override resolveParams(settings: SetRepeatSettings): Record<string, unknown> {
    return { mode: settings.mode ?? "off" };
  }

  protected override isBlockedByProvider(_settings: SetRepeatSettings): boolean {
    return nowPlayingState.current?.canSetRepeat === false;
  }
}
