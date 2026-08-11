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
import { renderPlayPauseKey } from "../nowPlaying/keyRenderer.js";

/** Live key: play/pause icon + elapsed time, redrawn every second while visible (streamdeck-plugin.md P4). */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-play-pause" })
export class PlayPauseAction extends MusicAction {
  protected readonly actionType = "music_play_pause";
  private timer: ReturnType<typeof setInterval> | null = null;

  override async onWillAppear(ev: WillAppearEvent<JsonObject>): Promise<void> {
    await super.onWillAppear(ev);
    this.stopTicking();
    this.timer = setInterval(() => {
      void ev.action.setImage(renderPlayPauseKey(nowPlayingState));
    }, 1000);
    nowPlayingState.onChange(() => void ev.action.setImage(renderPlayPauseKey(nowPlayingState)));
    await ev.action.setImage(renderPlayPauseKey(nowPlayingState));
  }

  override async onWillDisappear(): Promise<void> {
    this.stopTicking();
  }

  private stopTicking(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
