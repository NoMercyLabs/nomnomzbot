// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action, type WillAppearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";

interface VolumeMuteSettings extends JsonObject {
  unmuteVolume?: number;
}

/** Live key: the title shows the real current volume (or "Muted" at 0%), read from the same
 * position-anchor now-playing state every other live key uses — never a locally-guessed toggle. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-volume-mute" })
export class VolumeMuteAction extends MusicAction<VolumeMuteSettings> {
  protected readonly actionType = "music_volume_mute";
  protected readonly iconName = "volume-mute";

  protected override resolveParams(settings: VolumeMuteSettings): Record<string, unknown> {
    return { unmuteVolume: settings.unmuteVolume ?? 50 };
  }

  override async onWillAppear(ev: WillAppearEvent<VolumeMuteSettings>): Promise<void> {
    await super.onWillAppear(ev);
    const paint = () => void ev.action.setTitle(volumeTitle(nowPlayingState.current?.volumePercent));
    nowPlayingState.onChange(paint);
    paint();
  }
}

function volumeTitle(volumePercent: number | undefined): string {
  if (volumePercent === undefined) return "Mute";
  return volumePercent <= 0 ? "Muted" : `Mute\n${volumePercent}%`;
}
