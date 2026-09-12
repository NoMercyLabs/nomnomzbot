// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { ObsLiveIconAction } from "./obsLiveIconAction.js";
import { obsLiveState } from "../state.js";

export interface ToggleMuteSettings extends JsonObject {
  inputName?: string;
}

/** Invokes the dashboard's `obs_input_mute` pipeline (obs-control.md §5) with `toggle: true` — flips
 * whichever audio input this key names (mic, desktop audio, …), chosen from a live dropdown
 * (ui/input-picker.html, S-STREAMDECK-OBS-REMAINDER) fed by `GET /automation/v1/obs/inputs`, rather
 * than setting an absolute muted/unmuted state, matching a single-key toggle press.
 *
 * S-STREAMDECK-OBS-REMAINDER: the icon reflects whether the named input is ACTUALLY muted right now
 * (fed by the `obs.mute.changed` live push) — full brightness while muted, dimmed while unmuted or
 * unknown, rather than a static icon that never changes. There is no separate "unmuted" icon asset
 * yet (only `mic-mute.svg` exists), so the on/off distinction is opacity, matching how every other
 * OBS key already signals an unavailable/inactive control (`renderIconKey`'s `dimmed` flag). */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-toggle-mute" })
export class ToggleMuteAction extends ObsLiveIconAction<ToggleMuteSettings> {
  protected readonly pipelineName = "obs_input_mute";

  protected override resolveParams(settings: ToggleMuteSettings): Record<string, unknown> {
    return { input: settings.inputName ?? "", toggle: true };
  }

  protected override liveIcon(settings: ToggleMuteSettings): { iconName: string; dimmed: boolean } {
    const muted = obsLiveState.isInputMuted(settings.inputName ?? "");
    return { iconName: "mic-mute", dimmed: muted !== true };
  }
}
