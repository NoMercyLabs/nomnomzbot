// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action, type WillAppearEvent, type DidReceiveSettingsEvent, type KeyAction } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { nowPlayingState } from "../nowPlaying/state.js";
import { renderNowPlayingKey, DEFAULT_BACKGROUND } from "../nowPlaying/keyRenderer.js";
import { getCoverArtDataUri } from "../nowPlaying/coverArt.js";

/** Faster than the plain Play/Pause key's 1s tick — the marquee needs enough steps per second to read
 * as scrolling motion rather than a series of jumps. */
const TICK_MS = 500;

/**
 * Live key: real cover art (fetched once per album-art URL, cached — see coverArt.ts) with a bottom
 * gradient and a scrolling title/artist marquee; a tap toggles playback exactly like Play/Pause.
 * Cast to KeyAction: this manifest entry has no Encoder controller, so it's never a DialAction at runtime.
 */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-now-playing" })
export class NowPlayingAction extends MusicAction {
  protected readonly actionType = "music_play_pause";
  protected readonly iconName = "play-pause";
  private timer: ReturnType<typeof setInterval> | null = null;
  private tick = 0;

  override async onWillAppear(ev: WillAppearEvent<JsonObject>): Promise<void> {
    const key = ev.action as KeyAction<JsonObject>;
    const redraw = () => void this.renderLive(key, ev.payload.settings);
    this.stopTicking();
    this.timer = setInterval(() => {
      this.tick++;
      redraw();
    }, TICK_MS);
    nowPlayingState.onChange(redraw);
    await this.renderLive(key, ev.payload.settings);
  }

  override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<JsonObject>): Promise<void> {
    await this.renderLive(ev.action as KeyAction<JsonObject>, ev.payload.settings);
  }

  private async renderLive(key: KeyAction<JsonObject>, settings: JsonObject): Promise<void> {
    const backgroundColor =
      typeof settings.backgroundColor === "string" && settings.backgroundColor.length > 0
        ? settings.backgroundColor
        : DEFAULT_BACKGROUND;
    const coverArtDataUri = await getCoverArtDataUri(nowPlayingState.current?.albumArtUrl ?? null);
    await key.setImage(renderNowPlayingKey(nowPlayingState, coverArtDataUri, this.tick, backgroundColor));
  }

  override async onWillDisappear(): Promise<void> {
    this.stopTicking();
  }

  private stopTicking(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
