// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.alerts.state

import bot.nomnomz.dashboard.core.network.AlertQueueDto
import bot.nomnomz.dashboard.core.network.AlertQueueEntryDto
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlinx.coroutines.test.runTest

// Proves the alert-queue tab's state machine the screen renders: resolve the active channel, then surface that
// channel's real queue with the overlay presence the backend reported — error if either step fails. The queue
// is read for the resolved channel id, so the test asserts which channel the call went to, not only that it ran.
class AlertsControllerTest {

    @Test
    fun load_reads_the_queue_of_the_resolved_channel_and_keeps_every_entry_and_the_presence_flag() = runTest {
        val entries: List<AlertQueueEntryDto> =
            listOf(
                AlertQueueEntryDto(id = "1", provider = "twitch", kind = "follow", status = "delivered"),
                AlertQueueEntryDto(id = "2", provider = "patreon", kind = "supporter.tip", status = "queued"),
            )
        val alertsApi = RecordingAlertsApi(ApiResult.Ok(AlertQueueDto(overlayConnected = true, entries = entries)))
        val controller = AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)

        controller.load()

        val state: AlertQueueUiState.Ready = assertIs<AlertQueueUiState.Ready>(controller.queueState.value)
        assertEquals(true, state.overlayConnected)
        assertEquals(entries, state.entries)
        assertEquals(listOf("ch1"), alertsApi.queueChannels)
    }

    @Test
    fun load_keeps_a_disconnected_overlay_disconnected_even_when_the_queue_has_entries() = runTest {
        val entries: List<AlertQueueEntryDto> =
            listOf(AlertQueueEntryDto(id = "1", provider = "treatstream", kind = "supporter.tip", status = "queued"))
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingAlertsApi(ApiResult.Ok(AlertQueueDto(overlayConnected = false, entries = entries))),
            )

        controller.load()

        val state: AlertQueueUiState.Ready = assertIs<AlertQueueUiState.Ready>(controller.queueState.value)
        assertEquals(false, state.overlayConnected)
        assertEquals(1, state.entries.size)
    }

    @Test
    fun load_errors_with_the_reason_and_never_reads_the_queue_when_no_channel_resolves() = runTest {
        val alertsApi = RecordingAlertsApi(ApiResult.Ok(AlertQueueDto()))
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NOT_FOUND", "no channel"))),
                alertsApi,
            )

        controller.load()

        val state: AlertQueueUiState.Error = assertIs<AlertQueueUiState.Error>(controller.queueState.value)
        assertEquals("no channel", state.detail)
        assertEquals(emptyList(), alertsApi.queueChannels)
    }

    @Test
    fun load_errors_with_the_reason_when_the_queue_call_fails() = runTest {
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingAlertsApi(ApiResult.Failure(ApiError(500, "BOOM", "queue unavailable"))),
            )

        controller.load()

        val state: AlertQueueUiState.Error = assertIs<AlertQueueUiState.Error>(controller.queueState.value)
        assertEquals("queue unavailable", state.detail)
    }

    @Test
    fun a_reload_after_a_failure_replaces_the_error_with_the_fresh_queue() = runTest {
        val alertsApi = RecordingAlertsApi(ApiResult.Failure(ApiError(500, "BOOM", "queue unavailable")))
        val controller = AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()

        alertsApi.queueResult = ApiResult.Ok(AlertQueueDto(overlayConnected = true))
        controller.load()

        assertIs<AlertQueueUiState.Ready>(controller.queueState.value)
        assertEquals(listOf("ch1", "ch1"), alertsApi.queueChannels)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

// A recording fake: returns [queueResult] and records which channel id each queue call targeted.
private class RecordingAlertsApi(var queueResult: ApiResult<AlertQueueDto>) : AlertsApi {
    val queueChannels: MutableList<String> = mutableListOf()

    override suspend fun queue(channelId: String): ApiResult<AlertQueueDto> {
        queueChannels += channelId
        return queueResult
    }
}
