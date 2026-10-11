// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Settings page state machine: resolve the active channel, then run the channel-management actions
// (join, leave, reset, delete). The stream info editor is not here any more: it lives on the Stream page (see
// StreamInfoControllerTest).
class SettingsControllerTest {

    @Test
    fun load_is_ready_once_the_active_channel_resolves() = runTest {
        val controller = SettingsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))))

        controller.load()

        assertTrue(controller.state.value is SettingsState.Ready)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            SettingsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded")))
            )

        controller.load()

        assertTrue(controller.state.value is SettingsState.Error)
    }

    @Test
    fun delete_channel_transitions_to_channel_deleted_on_success() = runTest {
        val controller =
            SettingsController(
                FakeChannelsApi(
                    ApiResult.Ok(ChannelSummary(id = "ch1")),
                    deleteResult = ApiResult.Ok(Unit),
                )
            )
        controller.load()
        assertTrue(controller.state.value is SettingsState.Ready)

        controller.deleteChannel()

        assertTrue(controller.state.value is SettingsState.ChannelDeleted)
    }

    @Test
    fun delete_channel_returns_the_failure_to_the_dialog_and_keeps_ready_state() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            SettingsController(
                FakeChannelsApi(
                    ApiResult.Ok(ChannelSummary(id = "ch1")),
                    deleteResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
                ),
                feedback,
            )
        controller.load()
        assertTrue(controller.state.value is SettingsState.Ready)

        val result: ApiResult<Unit> = controller.deleteChannel()

        // Stays Ready (so the user keeps the page); the failure goes back to the confirm dialog, not a toast.
        val state: SettingsState = controller.state.value
        assertTrue(state is SettingsState.Ready)
        assertEquals("Missing scope.", (result as ApiResult.Failure).error.message)
        assertEquals(0, feedback.messages.size)
    }
}

private class FakeChannelsApi(
    private val result: ApiResult<ChannelSummary>,
    private val deleteResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = deleteResult
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}
