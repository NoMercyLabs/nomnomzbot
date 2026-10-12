// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.UpsertVtsConnectionBody
import bot.nomnomz.dashboard.core.network.VtsApi
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsControlBody
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.core.network.VtsRequestResult

// A scriptable VtsApi: the connection, the inventory and the upsert answer from the constructor; every other
// call fails, which the page treats as best-effort. Records each connection body and control body it receives.
class FakeVtsApi(
    private val connection: VtsConnection = VtsConnection(),
    private val inventory: VtsModelInventory? = null,
    private val upsert: suspend () -> ApiResult<VtsConnection> = { ApiResult.Ok(VtsConnection()) },
) : VtsApi {
    val sent: MutableList<UpsertVtsConnectionBody> = mutableListOf()
    val controls: MutableList<VtsControlBody> = mutableListOf()

    private fun <T> unavailable(): ApiResult<T> = ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun connection(channelId: String): ApiResult<VtsConnection> = ApiResult.Ok(connection)

    override suspend fun upsertConnection(channelId: String, body: UpsertVtsConnectionBody): ApiResult<VtsConnection> {
        sent += body
        return upsert()
    }

    override suspend fun authorize(channelId: String): ApiResult<Unit> = unavailable()

    override suspend fun rotateBridgeToken(channelId: String): ApiResult<VtsConnection> = unavailable()

    override suspend fun inventory(channelId: String): ApiResult<VtsModelInventory> =
        inventory?.let { ApiResult.Ok(it) } ?: unavailable()

    override suspend fun control(channelId: String, body: VtsControlBody): ApiResult<VtsRequestResult> {
        controls += body
        return ApiResult.Ok(VtsRequestResult(ok = true))
    }
}
