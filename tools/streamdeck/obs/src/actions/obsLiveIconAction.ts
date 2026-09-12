// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { WillAppearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { ObsAction, backgroundColorOf } from "./obsAction.js";
import { renderIconKey } from "../keyRenderer.js";
import { obsLiveState } from "../state.js";

/**
 * Shared base for the OBS tray actions whose icon reflects LIVE state (S-STREAMDECK-OBS-REMAINDER) —
 * mute/streaming/recording tiles that used to show one static placeholder icon regardless of what OBS
 * was actually doing. Mirrors the music plugin's `PlayPauseAction` re-render-on-change shape: redraw
 * whenever {@link obsLiveState} changes, not just on appear/settings-change like the plain
 * {@link ObsAction} base.
 *
 * Subclasses implement {@link liveIcon} only — everything else (subscribing, redrawing, background
 * color) is common across mute/stream/record and lives here once, per Rule of Three (the same
 * subscribe-and-redraw shape was about to be repeated across 7 action classes).
 */
export abstract class ObsLiveIconAction<
  TSettings extends JsonObject = JsonObject,
> extends ObsAction<TSettings> {
  /** {@link ObsAction.iconName} is unused here — every live-icon action overrides {@link render}
   * instead, since its icon depends on live state rather than being fixed per action. Implemented
   * once here so the 7 concrete subclasses (mute/stream/record) don't each need a throwaway value. */
  protected override readonly iconName: string = "";

  /** This key's icon name + whether it should render dimmed, for the CURRENT live state (e.g. Toggle
   * Streaming shows "stream-stop" once OBS is live; Start Streaming dims once already streaming, since
   * pressing it again would be a no-op). */
  protected abstract liveIcon(settings: TSettings): { iconName: string; dimmed: boolean };

  protected override async render(
    action: WillAppearEvent<TSettings>["action"],
    settings: TSettings,
  ): Promise<void> {
    const { iconName, dimmed } = this.liveIcon(settings);
    await action.setImage(renderIconKey(iconName, backgroundColorOf(settings), dimmed));
  }

  override async onWillAppear(ev: WillAppearEvent<TSettings>): Promise<void> {
    await super.onWillAppear(ev);
    // Same simplification the music plugin's PlayPauseAction already accepts (nowPlaying/state.ts):
    // ObsLiveState exposes no `off`, so a listener is registered per appearance rather than torn down
    // on willDisappear — an accepted, pre-existing tradeoff in this codebase, not new here.
    obsLiveState.onChange(() => void this.render(ev.action, ev.payload.settings));
  }
}
