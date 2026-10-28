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
import { ObsAction } from "./obsAction.js";

/** Invokes the dashboard's `obs_save_replay` pipeline (obs-control.md §5) — saves the replay
 * buffer as a clip. No config: pure trigger, same shape as ToggleReplayBufferAction. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-save-replay" })
export class SaveReplayAction extends ObsAction {
  protected readonly pipelineName = "obs_save_replay";
  protected readonly iconName = "record-start";
}
