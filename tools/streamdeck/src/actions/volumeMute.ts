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

interface VolumeMuteSettings extends JsonObject {
  unmuteVolume?: number;
}

/** Live key: volume/volume-mute icon actually flips on the real muted state (volumePercent <= 0),
 * same pattern as Toggle Shuffle/Favorite Toggle — not just a static "mute" glyph with a text label
 * layered on top. The title still shows the live percentage as supplementary detail. */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-volume-mute" })
export class VolumeMuteAction extends MusicAction<VolumeMuteSettings> {
  protected readonly actionType = "music_volume_mute";
  protected readonly iconName = "volume";

  protected override resolveParams(settings: VolumeMuteSettings): Record<string, unknown> {
    return { unmuteVolume: settings.unmuteVolume ?? 50 };
  }

  protected override getIconName(_settings: VolumeMuteSettings): string {
    return isMuted(nowPlayingState.current?.volumePercent) ? "volume-mute" : "volume";
  }

  override async onWillAppear(ev: WillAppearEvent<VolumeMuteSettings>): Promise<void> {
    await super.onWillAppear(ev);
    const key = ev.action as KeyAction<VolumeMuteSettings>;
    const paint = () => {
      void this.render(key, ev.payload.settings);
      void key.setTitle(volumeTitle(nowPlayingState.current?.volumePercent));
    };
    nowPlayingState.onChange(paint);
    paint();
  }
}

function isMuted(volumePercent: number | undefined): boolean {
  return volumePercent !== undefined && volumePercent <= 0;
}

function volumeTitle(volumePercent: number | undefined): string {
  if (volumePercent === undefined) return "";
  return volumePercent <= 0 ? "Muted" : `${volumePercent}%`;
}
