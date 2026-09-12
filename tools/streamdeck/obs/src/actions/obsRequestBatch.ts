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

export interface RequestBatchSettings extends JsonObject {
  requests?: string;
}

/** Invokes the dashboard's `obs_request_batch` pipeline (obs-control.md §5) — a batch of raw
 * OBS-WS requests, sent as one JSON array of `{ request_type, request_data? }` objects. Free-text
 * field (ui/simple-param.html): there is no backend enumeration of the obs-websocket request-type
 * space to build a picker from, same as `obs_request`/`obs_call_vendor`.
 *
 * Was deferred (S-STREAMDECK-OBS-REMAINDER) until `PipelineEngine.ResolveTemplatedFieldsAsync`
 * stopped always re-wrapping a resolved Templated Text field's value as a JSON string — before that
 * fix, the auto-provisioned pipeline's whole-value `{requests}` placeholder always resolved to a
 * JSON *string*, and `ObsRequestBatchAction` requires a real JSON array. Now that the engine
 * preserves an array/object shape when the resolved text parses as one, this key's `requests` value
 * (typed here as one JSON array) survives the round trip intact. */
@action({ UUID: "bot.nomnomzbot.streamdeck.obs.obs-request-batch" })
export class RequestBatchAction extends ObsAction<RequestBatchSettings> {
  protected readonly pipelineName = "obs_request_batch";
  protected readonly iconName = "device";

  protected override resolveParams(settings: RequestBatchSettings): Record<string, unknown> {
    return { requests: settings.requests ?? "" };
  }
}
