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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.DeviceBotPoll
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.PlatformBotAdminApi
import bot.nomnomz.dashboard.core.network.PlatformBotAdminStatus
import bot.nomnomz.dashboard.core.network.PlatformBotReconnectPreview
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull

/**
 * A shared-bot reconnect is confirmed against a counted blast radius. When that count moves mid-login the
 * server answers PREVIEW_STALE on every poll, which is final: the panel used to keep polling (a spinner) until
 * the device code expired. It must stop at once and drop the stale preview so the operator re-previews.
 */
class AdminControllerPlatformBotStaleTest {

    @Test
    fun a_stale_count_during_the_device_login_stops_polling_and_clears_the_preview() = runTest {
        val bot = StalePollPlatformBotAdminApi(startFails = false)
        val controller = controllerWith(bot)
        controller.setPlatformBotJustification("rotating the shared bot password")
        controller.previewPlatformBotReconnect()
        assertNotNull(controller.state.value.platformBotReconnectPreview)

        controller.confirmPlatformBotReconnect()
        controller.awaitPlatformBotReconnect()

        assertEquals(1, bot.pollCalls)
        assertNull(controller.state.value.platformBotReconnectDevice)
        assertNull(controller.state.value.platformBotReconnectPreview)
    }

    @Test
    fun a_stale_count_at_the_start_is_handed_back_to_the_dialog_and_never_opens_a_device_login() = runTest {
        val bot = StalePollPlatformBotAdminApi(startFails = true)
        val controller = controllerWith(bot)
        controller.setPlatformBotJustification("rotating the shared bot password")
        controller.previewPlatformBotReconnect()

        val result: ApiResult<Unit> = controller.confirmPlatformBotReconnect()
        controller.awaitPlatformBotReconnect()

        assertEquals(stale, (result as ApiResult.Failure).error)
        assertEquals(0, bot.pollCalls)
        assertNull(controller.state.value.platformBotReconnectDevice)
        assertNotNull(controller.state.value.platformBotReconnectPreview)
    }

    private fun controllerWith(bot: PlatformBotAdminApi): AdminController =
        AdminController(
            api = PagedOpsFakeAdminApi(),
            iamApi = PagedFakePlatformIamApi(),
            platformAdminApi = PagedFakePlatformAdminApi(),
            platformBotAdminApi = bot,
        )
}

private val stale: ApiError =
    ApiError(status = 409, code = "PREVIEW_STALE", message = "The affected channel count changed; preview again.")

private class StalePollPlatformBotAdminApi(private val startFails: Boolean) : PlatformBotAdminApi {
    var pollCalls: Int = 0
        private set

    override suspend fun status(): ApiResult<PlatformBotAdminStatus> =
        ApiResult.Failure(ApiError(status = 500, code = null, message = "not stubbed"))

    override suspend fun previewReconnect(justification: String): ApiResult<PlatformBotReconnectPreview> =
        ApiResult.Ok(PlatformBotReconnectPreview(affectedChannelCount = 4))

    override suspend fun startReconnect(
        justification: String,
        confirmedAffectedChannelCount: Int,
    ): ApiResult<DeviceCodeStart> =
        if (startFails) {
            ApiResult.Failure(stale)
        } else {
            ApiResult.Ok(DeviceCodeStart(deviceCode = "dev", userCode = "ABCD", verificationUri = "https://twitch.tv/activate"))
        }

    override suspend fun pollReconnect(
        deviceCode: String,
        justification: String,
        confirmedAffectedChannelCount: Int,
    ): ApiResult<DeviceBotPoll> {
        pollCalls += 1
        return ApiResult.Failure(stale)
    }
}
