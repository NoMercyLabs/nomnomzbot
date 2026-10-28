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

export interface RequestSettings extends JsonObject {
  requestType?: string;
  requestData?: string;
}

/** Invokes the dashboard's `obs_request` pipeline (obs-control.md §5) — a raw OBS-WS request
 * passthrough for anything not covered by a dedicated action. Free-text fields
 * (ui/simple-param.html): the request type name plus an optional JSON object of request data,
 * matching `ObsRequestAction.ReadDataObject`'s expected shape on the backend. No picker is
 * possible here — the request-type space is the entire obs-websocket protocol. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-request" })
export class RequestAction extends ObsAction<RequestSettings> {
  protected readonly pipelineName = "obs_request";
  protected readonly iconName = "device";

  protected override resolveParams(settings: RequestSettings): Record<string, unknown> {
    const params: Record<string, unknown> = { request_type: settings.requestType ?? "" };
    if (settings.requestData) params.request_data = settings.requestData;
    return params;
  }
}
