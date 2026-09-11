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
import { ObsAction } from "./obsAction.js";

export interface CallVendorSettings extends JsonObject {
  vendor?: string;
  requestType?: string;
  requestData?: string;
}

/** Invokes the dashboard's `obs_call_vendor` pipeline (obs-control.md §5) — calls into an
 * OBS-WS vendor (third-party plugin, e.g. obs-websocket-vendor extensions). Free-text fields
 * (ui/simple-param.html): vendor name, request type, and an optional JSON object of request data —
 * there is no backend enumeration of installed vendors/requests to build a picker from. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs-call-vendor" })
export class CallVendorAction extends ObsAction<CallVendorSettings> {
  protected readonly pipelineName = "obs_call_vendor";
  protected readonly iconName = "device";

  protected override resolveParams(settings: CallVendorSettings): Record<string, unknown> {
    const params: Record<string, unknown> = {
      vendor: settings.vendor ?? "",
      request_type: settings.requestType ?? "",
    };
    if (settings.requestData) params.request_data = settings.requestData;
    return params;
  }
}
