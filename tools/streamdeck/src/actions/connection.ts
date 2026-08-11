// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action, type KeyDownEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";
import { automationClient } from "../connection/automationClient.js";

/**
 * The ONE key that shows pairing/host setup — every other action's property inspector is
 * appearance-only.html (streamdeck-plugin.md P6). Not a music_* action: it has no backend action
 * type to invoke, it exists purely to host the Connection property inspector and give the operator
 * a physical key that reflects paired/not-paired at a glance.
 */
@action({ UUID: "bot.nomnomzbot.streamdeck.connection" })
export class ConnectionAction extends MusicAction {
  protected readonly actionType = "";
  protected readonly iconName = "connection";

  /** No backend action to invoke — pressing it just re-checks/shows the current pairing status. */
  override async onKeyDown(ev: KeyDownEvent<JsonObject>): Promise<void> {
    if (!(await automationClient.isPaired())) {
      await ev.action.showAlert();
      return;
    }
    await ev.action.showOk();
  }
}
