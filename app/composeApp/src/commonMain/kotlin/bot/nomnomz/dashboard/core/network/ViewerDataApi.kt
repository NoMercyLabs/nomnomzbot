// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed viewer custom-data facade — a viewer's per-channel key/value store (death counters, quest flags,
// "favorite game", …) that the streamer's pipelines write (set_viewer_data / adjust_viewer_data) and mods
// view/fix from the viewer detail card. Real data only: the backend returns the stored map; keys are lowercase
// slugs (≤50 chars), values ≤500 chars (the backend REJECTS over-cap writes — it never truncates — so the
// caller surfaces its error). The channel is resolved from the active-channel X-Channel-Id header the ApiClient
// injects, so these paths carry only the viewer id.
//
// Backend routes (Viewer Data tag):
//   GET    /api/v1/viewers/{viewerId}/data          →  StatusResponseDto<Map<string,string>>  (the stored map)
//   PUT    /api/v1/viewers/{viewerId}/data/{key}     →  SetViewerDatumRequest { value }         (upsert one key)
//   DELETE /api/v1/viewers/{viewerId}/data/{key}     →  200                                     (remove one key)
interface ViewerDataApi {
    /** The viewer's stored key→value map (empty when the viewer has no custom data). */
    suspend fun getData(viewerId: String): ApiResult<Map<String, String>>

    /** Upsert one [key] to [value]. The backend rejects (never truncates) an over-cap value with its message. */
    suspend fun setDatum(viewerId: String, key: String, value: String): ApiResult<Unit>

    /** Remove one [key] from the viewer's data. */
    suspend fun deleteDatum(viewerId: String, key: String): ApiResult<Unit>
}

class RestViewerDataApi(private val client: ApiClient) : ViewerDataApi {
    // StatusResponseDto<Map<string,string>> — getEnvelope unwraps `.data` to the map.
    override suspend fun getData(viewerId: String): ApiResult<Map<String, String>> =
        client.getEnvelope("api/v1/viewers/$viewerId/data")

    // Partial upsert of a single key; any 2xx is success (the controller refetches the map after).
    override suspend fun setDatum(viewerId: String, key: String, value: String): ApiResult<Unit> =
        client.putUnit("api/v1/viewers/$viewerId/data/$key", SetViewerDatumBody(value))

    override suspend fun deleteDatum(viewerId: String, key: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/viewers/$viewerId/data/$key")
}

/** The set-datum request body (backend `SetViewerDatumRequest`). */
@Serializable
data class SetViewerDatumBody(val value: String)
