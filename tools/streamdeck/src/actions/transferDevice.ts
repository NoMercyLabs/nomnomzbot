// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";

interface TransferDeviceSettings extends JsonObject {
  deviceId?: string;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-transfer-device" })
export class TransferDeviceAction extends MusicAction<TransferDeviceSettings> {
  protected readonly actionType = "music_transfer_device";

  protected override resolveParams(settings: TransferDeviceSettings): Record<string, unknown> {
    return { deviceId: settings.deviceId ?? "" };
  }
}
