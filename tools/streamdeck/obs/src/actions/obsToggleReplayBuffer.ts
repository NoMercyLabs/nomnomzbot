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

import { action } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { ObsAction } from "./obsAction.js";

/** Invokes the dashboard's `obs_replay_buffer` pipeline (obs-control.md §5) with `action: "toggle"` —
 * one key that starts or stops the replay buffer depending on OBS's current state, mirroring OBS
 * Studio's own single Start/Stop Replay Buffer button (S-STREAMDECK-OBS-REMAINDER). */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-toggle-replay-buffer" })
export class ToggleReplayBufferAction extends ObsAction {
  protected readonly pipelineName = "obs_replay_buffer";
  protected readonly iconName = "record-start";

  protected override resolveParams(_settings: JsonObject): Record<string, unknown> {
    return { action: "toggle" };
  }
}
