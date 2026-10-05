// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.realtime.AdminHubClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * A 403 means "signed in, not allowed". It must show access denied and never start a token refresh: the
 * refresh path ends the session when it fails, which signed a non-admin out of the whole app.
 */
class AdminControllerForbiddenTest {

    private val forbidden: ApiError = ApiError(403, "FORBIDDEN", "No permission (HTTP 403).")

    private fun forbiddenApi(): AdminApi =
        object : AdminApi by PagedOpsFakeAdminApi() {
            override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Failure(forbidden)

            override suspend fun getChannels(
                search: String?,
                page: Int,
                pageSize: Int,
                sort: String?,
                isLive: Boolean?,
            ): ApiResult<PaginatedEnvelope<AdminChannel>> = ApiResult.Failure(forbidden)

            override suspend fun getUsers(
                search: String?,
                page: Int,
                pageSize: Int,
                sort: String?,
                role: String?,
            ): ApiResult<PaginatedEnvelope<AdminUser>> = ApiResult.Failure(forbidden)

            override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Failure(forbidden)
        }

    @Test
    fun a_403_on_the_admin_reads_shows_access_denied_and_never_refreshes_the_token() = runTest {
        var refreshCalls = 0
        val controller = AdminController(
            api = forbiddenApi(),
            iamApi = PagedFakePlatformIamApi(),
            platformAdminApi = PagedFakePlatformAdminApi(),
            hubClient = AdminHubClient(),
            baseUrl = { "http://127.0.0.1:1" },
            accessToken = { "jwt" },
            refreshToken = {
                refreshCalls++
                false
            },
        )

        controller.load()
        // The hub reconnect loop runs on its own dispatcher; give it time to fail and (wrongly) refresh.
        withContext(Dispatchers.Default) { delay(1_500) }

        assertTrue(controller.state.value.accessDenied)
        assertEquals(0, refreshCalls)
    }
}
