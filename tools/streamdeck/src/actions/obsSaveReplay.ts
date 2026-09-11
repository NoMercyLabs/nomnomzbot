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
import { ObsAction } from "./obsAction.js";

/** Invokes the dashboard's `obs_save_replay` pipeline (obs-control.md §5) — saves the replay
 * buffer as a clip. No config: pure trigger, same shape as ToggleReplayBufferAction. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs-save-replay" })
export class SaveReplayAction extends ObsAction {
  protected readonly pipelineName = "obs_save_replay";
  protected readonly iconName = "record-start";
}
