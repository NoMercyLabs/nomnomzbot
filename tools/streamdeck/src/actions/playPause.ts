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
import { renderPlayPauseKey, DEFAULT_BACKGROUND } from "../nowPlaying/keyRenderer.js";

/**
 * Live key: play/pause icon + elapsed time, redrawn every second while visible (streamdeck-plugin.md P4).
 * Manifest declares 2 native States (play/pause) so the key shows the right icon before the plugin
 * connects or if a custom-image redraw ever fails; setState() keeps that state in sync on every change,
 * then the setImage() call layers the live elapsed-time overlay on top — setImage always runs last, so
 * the custom render is what's actually shown. Cast to KeyAction: this manifest entry has no Encoder
 * controller, so it's never a DialAction at runtime. iconName is unused (this action fully owns its own
 * render) but still required by the abstract base contract.
 */
@action({ UUID: "bot.nomnomzbot.streamdeck.music-play-pause" })
export class PlayPauseAction extends MusicAction {
  protected readonly actionType = "music_play_pause";
  protected readonly iconName = "play";
  private timer: ReturnType<typeof setInterval> | null = null;

  override async onWillAppear(ev: WillAppearEvent<JsonObject>): Promise<void> {
    const key = ev.action as KeyAction<JsonObject>;
    const redraw = () => void this.renderLive(key, ev.payload.settings);
    this.stopTicking();
    this.timer = setInterval(redraw, 1000);
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
    await key.setState(nowPlayingState.current?.isPlaying ? 1 : 0);
    await key.setImage(renderPlayPauseKey(nowPlayingState, backgroundColor));
  }

  override async onWillDisappear(): Promise<void> {
    this.stopTicking();
  }

  private stopTicking(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
