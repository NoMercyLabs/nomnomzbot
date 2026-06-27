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

import bot.nomnomz.dashboard.core.network.AlertDetail
import bot.nomnomz.dashboard.core.network.AlertSummary
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.UpdateAlertBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Alerts page state machine the screen renders: resolve the active channel, then surface the
// channel's real event responses — empty when there are none, error if either step fails. Writes mutate the
// fake's backing store, so the controller's post-write reload observes the real consequence (a new row, a
// flipped flag, a removed row), not merely that a call happened. The screen is a pure projection of this, so
// testing it proves the page shows real data (no fabricated rows) and degrades cleanly.
class AlertsControllerTest {

    @Test
    fun load_surfaces_the_channel_event_responses_on_success() = runTest {
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingAlertsApi(
                    ApiResult.Ok(
                        listOf(
                            AlertSummary(
                                id = "7",
                                eventType = "channel.follow",
                                isEnabled = true,
                                responseType = "chat_message",
                            )
                        )
                    )
                ),
            )

        controller.load()

        val state: AlertsState = controller.state.value
        assertTrue(state is AlertsState.Ready)
        val alerts: List<AlertSummary> = (state as AlertsState.Ready).alerts
        assertEquals(1, alerts.size)
        val alert: AlertSummary = alerts.first()
        assertEquals("channel.follow", alert.eventType)
        assertEquals(true, alert.isEnabled)
        assertEquals("chat_message", alert.responseType)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_event_responses() = runTest {
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingAlertsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is AlertsState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingAlertsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is AlertsState.Error)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            AlertsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingAlertsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is AlertsState.Error)
    }

    @Test
    fun create_upserts_the_body_then_reloads_with_the_new_event_response() = runTest {
        // The fake starts empty; the create appends the new response to its backing store, so the controller's
        // post-write reload must surface it — proving create actually calls the api AND re-lists.
        val alertsApi = RecordingAlertsApi(ApiResult.Ok(emptyList()))
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()
        assertTrue(controller.state.value is AlertsState.Empty)

        controller.createAlert(eventType = "channel.subscribe", message = "Thanks!", isEnabled = true)

        // The api recorded exactly the body the controller built — a create sets the chat_message type.
        assertEquals(1, alertsApi.upserts.size)
        val upsert: Triple<String, String, UpdateAlertBody> = alertsApi.upserts.first()
        assertEquals("ch1", upsert.first)
        assertEquals("channel.subscribe", upsert.second)
        val body: UpdateAlertBody = upsert.third
        assertEquals("Thanks!", body.message)
        assertEquals("chat_message", body.responseType)
        assertEquals(true, body.isEnabled)

        // And the reload surfaced the freshly-created row.
        val state: AlertsState = controller.state.value
        assertTrue(state is AlertsState.Ready)
        val alerts: List<AlertSummary> = (state as AlertsState.Ready).alerts
        assertEquals(1, alerts.size)
        assertEquals("channel.subscribe", alerts.first().eventType)
        assertNull(state.actionError)
    }

    @Test
    fun edit_upserts_only_the_message_and_enabled_then_reloads() = runTest {
        val alertsApi =
            RecordingAlertsApi(
                ApiResult.Ok(
                    listOf(
                        AlertSummary(id = "1", eventType = "channel.raid", isEnabled = true)
                    )
                ),
                detailMessage = "old",
            )
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()

        controller.updateAlert(eventType = "channel.raid", message = "Welcome raiders!", isEnabled = true)

        // An edit is a partial PUT carrying the message + enabled, but NOT the response type (left untouched).
        assertEquals(1, alertsApi.upserts.size)
        val body: UpdateAlertBody = alertsApi.upserts.first().third
        assertEquals("Welcome raiders!", body.message)
        assertEquals(true, body.isEnabled)
        assertNull(body.responseType)

        // The reload reflects the persisted message.
        val detail: AlertDetail? = controller.detail("channel.raid")
        assertEquals("Welcome raiders!", detail?.message)
    }

    @Test
    fun toggle_upserts_only_the_enabled_flag_then_reloads_with_the_flipped_state() = runTest {
        val alertsApi =
            RecordingAlertsApi(
                ApiResult.Ok(
                    listOf(AlertSummary(id = "1", eventType = "channel.cheer", isEnabled = true))
                )
            )
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()

        controller.toggleAlert(eventType = "channel.cheer", enabled = false)

        // A toggle is a partial PUT carrying only isEnabled.
        assertEquals(1, alertsApi.upserts.size)
        val body: UpdateAlertBody = alertsApi.upserts.first().third
        assertEquals(false, body.isEnabled)
        assertNull(body.message)
        assertNull(body.responseType)

        // The reload reflects the persisted flip.
        val state: AlertsState = controller.state.value
        assertTrue(state is AlertsState.Ready)
        assertEquals(false, (state as AlertsState.Ready).alerts.first().isEnabled)
    }

    @Test
    fun delete_removes_the_event_response_then_reloads_to_empty() = runTest {
        val alertsApi =
            RecordingAlertsApi(
                ApiResult.Ok(listOf(AlertSummary(id = "1", eventType = "channel.follow", isEnabled = true)))
            )
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()
        assertTrue(controller.state.value is AlertsState.Ready)

        controller.deleteAlert(eventType = "channel.follow")

        assertEquals(listOf("channel.follow"), alertsApi.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is AlertsState.Empty)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val alertsApi =
            RecordingAlertsApi(
                ApiResult.Ok(listOf(AlertSummary(id = "1", eventType = "channel.follow", isEnabled = true))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()

        controller.deleteAlert(eventType = "channel.follow")

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: AlertsState = controller.state.value
        assertTrue(state is AlertsState.Ready)
        assertEquals(1, (state as AlertsState.Ready).alerts.size)
        assertEquals("no permission", state.actionError)
    }

    @Test
    fun detail_reads_the_message_for_the_edit_dialog() = runTest {
        val alertsApi =
            RecordingAlertsApi(
                ApiResult.Ok(listOf(AlertSummary(id = "1", eventType = "channel.follow"))),
                detailMessage = "Thanks for the follow!",
            )
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()

        val detail: AlertDetail? = controller.detail("channel.follow")

        assertEquals("channel.follow", detail?.eventType)
        assertEquals("Thanks for the follow!", detail?.message)
    }

    @Test
    fun a_failed_detail_fetch_surfaces_the_error_over_the_kept_list() = runTest {
        val alertsApi =
            RecordingAlertsApi(
                ApiResult.Ok(listOf(AlertSummary(id = "1", eventType = "channel.follow"))),
                detailResult = ApiResult.Failure(ApiError(500, "ERR", "detail boom")),
            )
        val controller =
            AlertsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), alertsApi)
        controller.load()

        val detail: AlertDetail? = controller.detail("channel.follow")

        assertNull(detail)
        val state: AlertsState = controller.state.value
        assertTrue(state is AlertsState.Ready)
        assertEquals("detail boom", (state as AlertsState.Ready).actionError)
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
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful
// upsert/delete mutates the store so the controller's post-write reload observes the real consequence (a new
// row, a flipped flag, an edited message, a removed row) — not merely that a call happened. [writeResult]
// forces every write to fail (the store is left untouched) to exercise the error path. [detailResult] forces
// the detail fetch to fail; otherwise detail() reflects the live store with [detailMessage] for the seed rows.
private class RecordingAlertsApi(
    initial: ApiResult<List<AlertSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val detailResult: ApiResult<AlertDetail>? = null,
    private val detailMessage: String? = null,
) : AlertsApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<AlertSummary> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    // The persisted message per event type — seeded for the initial rows, updated on each successful upsert so
    // a post-edit detail() reads the new text.
    private val messages: MutableMap<String, String?> =
        store.associate { it.eventType to detailMessage }.toMutableMap()

    val upserts: MutableList<Triple<String, String, UpdateAlertBody>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<AlertSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun detail(channelId: String, eventType: String): ApiResult<AlertDetail> {
        detailResult?.let { return it }
        val row: AlertSummary? = store.firstOrNull { it.eventType == eventType }
        return if (row == null) {
            ApiResult.Failure(ApiError(404, "NOT_FOUND", "no such event response"))
        } else {
            ApiResult.Ok(
                AlertDetail(
                    id = row.id,
                    eventType = row.eventType,
                    isEnabled = row.isEnabled,
                    responseType = row.responseType,
                    message = messages[eventType],
                )
            )
        }
    }

    override suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateAlertBody,
    ): ApiResult<Unit> {
        upserts += Triple(channelId, eventType, body)
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.eventType == eventType }
            if (index >= 0) {
                val existing: AlertSummary = store[index]
                store[index] =
                    existing.copy(
                        isEnabled = body.isEnabled ?: existing.isEnabled,
                        responseType = body.responseType ?: existing.responseType,
                    )
            } else {
                store +=
                    AlertSummary(
                        id = (store.size + 1).toString(),
                        eventType = eventType,
                        isEnabled = body.isEnabled ?: true,
                        responseType = body.responseType ?: "chat_message",
                    )
            }
            if (body.message != null) messages[eventType] = body.message
        }
        return writeResult
    }

    override suspend fun delete(channelId: String, eventType: String): ApiResult<Unit> {
        deleted += eventType
        if (writeResult is ApiResult.Ok) {
            store.removeAll { it.eventType == eventType }
            messages.remove(eventType)
        }
        return writeResult
    }
}
