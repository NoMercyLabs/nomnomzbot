// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.supporters.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.SupporterConnection
import bot.nomnomz.dashboard.core.network.SupporterConnectionMode
import bot.nomnomz.dashboard.core.network.SupporterConnectionStatus
import bot.nomnomz.dashboard.core.network.SupporterEvent
import bot.nomnomz.dashboard.core.network.SupporterEventKind
import bot.nomnomz.dashboard.core.network.SupporterEventsPage
import bot.nomnomz.dashboard.core.network.SupporterSourceKey
import bot.nomnomz.dashboard.core.network.SupportersApi
import bot.nomnomz.dashboard.core.network.UpsertSupporterConnectionBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_supporter_connection_removed
import nomnomzbot.composeapp.generated.resources.feedback_supporter_save_failed

// Proves the Supporters page state machine the screen renders (supporter-events.md §5). It surfaces the channel's
// REAL connections + event feed, and follows through on every action by observing its CONSEQUENCE: connect upserts
// the provider ENABLED, sending its verification secret so the backend one-step provisions the ingest endpoint
// (returned as endpointUrl to copy); a plain toggle sends no secret and keeps the endpoint; disconnect removes the
// row; a failed write is surfaced over the kept tiles; and the event feed pages + filters against the backend. It
// also pins the minor→major amount formatting the row renders. The screen is a pure projection of this holder, so
// testing the holder proves the behaviour without rendering Compose.
class SupportersControllerTest {

    // ── Connections ────────────────────────────────────────────────────────────────

    @Test
    fun load_connections_surfaces_the_rows_on_success() = runTest {
        val controller =
            SupportersController(
                RecordingSupportersApi(
                    ApiResult.Ok(
                        listOf(
                            SupporterConnection(
                                sourceKey = SupporterSourceKey.Kofi,
                                connectionMode = SupporterConnectionMode.Webhook,
                                isEnabled = true,
                                status = SupporterConnectionStatus.Active,
                            )
                        )
                    )
                )
            )

        controller.loadConnections()

        val state: ConnectionsState = controller.connections.value
        assertTrue(state is ConnectionsState.Ready)
        val connection: SupporterConnection = (state as ConnectionsState.Ready).connections.single()
        assertEquals(SupporterSourceKey.Kofi, connection.sourceKey)
        assertEquals(SupporterConnectionMode.Webhook, connection.connectionMode)
        assertTrue(connection.isEnabled)
        assertEquals(SupporterConnectionStatus.Active, connection.status)
        assertNull(state.actionError)
    }

    @Test
    fun load_connections_is_ready_empty_when_none_configured() = runTest {
        // An empty connection list is a valid Ready (the provider tiles still render "not connected"), NOT an error
        // and NOT a phantom empty — a fresh channel must still be able to connect its first provider.
        val controller = SupportersController(RecordingSupportersApi(ApiResult.Ok(emptyList())))

        controller.loadConnections()

        val state: ConnectionsState = controller.connections.value
        assertTrue(state is ConnectionsState.Ready)
        assertTrue((state as ConnectionsState.Ready).connections.isEmpty())
    }

    @Test
    fun load_connections_errors_when_the_call_fails() = runTest {
        val controller =
            SupportersController(RecordingSupportersApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))))

        controller.loadConnections()

        val state: ConnectionsState = controller.connections.value
        assertTrue(state is ConnectionsState.Error)
        assertEquals("boom", (state as ConnectionsState.Error).detail)
    }

    @Test
    fun connect_sends_the_verification_secret_and_provisions_the_ingest_endpoint() = runTest {
        val api = RecordingSupportersApi(ApiResult.Ok(emptyList()))
        val controller = SupportersController(api)
        controller.loadConnections()

        controller.upsertConnection(
            SupporterSourceKey.Kofi,
            SupporterConnectionMode.Webhook,
            isEnabled = true,
            authSecret = "verify-123",
        )

        // The one-step connect carries the provider's verification secret on the wire body (the backend used to
        // reject one; that behaviour is GONE — it now provisions the inbound endpoint from it).
        val body: UpsertSupporterConnectionBody = api.upserts.single()
        assertEquals(SupporterSourceKey.Kofi, body.sourceKey)
        assertEquals(SupporterConnectionMode.Webhook, body.connectionMode)
        assertTrue(body.isEnabled)
        assertEquals("verify-123", body.authSecret)
        // The post-write reload surfaced the enabled provider WITH its auto-provisioned ingest URL to copy.
        val connection: SupporterConnection = readyConnections(controller).single()
        assertEquals(SupporterSourceKey.Kofi, connection.sourceKey)
        assertTrue(connection.isEnabled)
        assertTrue(connection.hasSecret)
        assertEquals("https://ingest.example/supporters/kofi", connection.endpointUrl)
    }

    @Test
    fun toggling_enabled_sends_no_secret_and_keeps_the_endpoint() = runTest {
        // A plain enable-toggle (no secret typed) leaves the stored secret + provisioned endpoint untouched.
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(
                    listOf(
                        SupporterConnection(
                            sourceKey = SupporterSourceKey.Kofi,
                            connectionMode = SupporterConnectionMode.Webhook,
                            hasSecret = true,
                            isEnabled = true,
                            status = SupporterConnectionStatus.Active,
                            endpointUrl = "https://ingest.example/supporters/kofi",
                        )
                    )
                )
            )
        val controller = SupportersController(api)
        controller.loadConnections()

        controller.upsertConnection(SupporterSourceKey.Kofi, SupporterConnectionMode.Webhook, isEnabled = false)

        assertNull(api.upserts.single().authSecret)
        val connection: SupporterConnection = readyConnections(controller).single()
        assertEquals(false, connection.isEnabled)
        assertEquals("https://ingest.example/supporters/kofi", connection.endpointUrl)
    }

    @Test
    fun toggle_disables_the_connection_and_reloads() = runTest {
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(
                    listOf(
                        SupporterConnection(
                            sourceKey = SupporterSourceKey.Kofi,
                            connectionMode = SupporterConnectionMode.Webhook,
                            isEnabled = true,
                            status = SupporterConnectionStatus.Active,
                        )
                    )
                )
            )
        val controller = SupportersController(api)
        controller.loadConnections()

        controller.upsertConnection(SupporterSourceKey.Kofi, SupporterConnectionMode.Webhook, isEnabled = false)

        assertEquals(false, api.upserts.single().isEnabled)
        assertEquals(false, readyConnections(controller).single().isEnabled)
    }

    @Test
    fun disconnect_removes_the_connection_and_announces_it() = runTest {
        val feedback = RecordingFeedback()
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(
                    listOf(
                        SupporterConnection(
                            sourceKey = SupporterSourceKey.Kofi,
                            connectionMode = SupporterConnectionMode.Webhook,
                            isEnabled = true,
                            status = SupporterConnectionStatus.Idle,
                        )
                    )
                )
            )
        val controller = SupportersController(api, feedback)
        controller.loadConnections()

        controller.disconnect(SupporterSourceKey.Kofi)

        assertEquals(listOf(SupporterSourceKey.Kofi), api.deleted)
        assertTrue(readyConnections(controller).isEmpty())
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_supporter_connection_removed, feedback.only.label)
    }

    @Test
    fun a_failed_connection_write_surfaces_the_error_over_the_kept_tiles() = runTest {
        val feedback = RecordingFeedback()
        val kept =
            SupporterConnection(
                sourceKey = SupporterSourceKey.Kofi,
                connectionMode = SupporterConnectionMode.Webhook,
                isEnabled = true,
                status = SupporterConnectionStatus.Active,
            )
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(listOf(kept)),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = SupportersController(api, feedback)
        controller.loadConnections()

        controller.upsertConnection(SupporterSourceKey.Kofi, SupporterConnectionMode.Webhook, isEnabled = false)

        // The tiles are kept (not blown away) and the failure is surfaced on them AND on the frame; the store was
        // left untouched so the toggle stays where it was.
        val state: ConnectionsState = controller.connections.value
        assertTrue(state is ConnectionsState.Ready)
        assertTrue((state as ConnectionsState.Ready).connections.single().isEnabled)
        assertEquals("no permission", state.actionError)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_supporter_save_failed, feedback.only.label)
    }

    // ── Events feed ──────────────────────────────────────────────────────────────

    @Test
    fun load_events_surfaces_the_feed_with_the_event_shape() = runTest {
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(emptyList()),
                eventStore =
                    listOf(
                        SupporterEvent(
                            id = "e1",
                            sourceKey = SupporterSourceKey.Kofi,
                            kind = SupporterEventKind.Tip,
                            supporterDisplayName = "Alice",
                            amountMinor = 500,
                            currency = "USD",
                            messageText = "gg",
                            receivedAt = "2026-07-12T12:00:00Z",
                        )
                    ),
            )
        val controller = SupportersController(api)

        controller.loadEvents()

        val state: EventsState = controller.events.value
        assertTrue(state is EventsState.Ready)
        val event: SupporterEvent = (state as EventsState.Ready).events.single()
        assertEquals(SupporterEventKind.Tip, event.kind)
        assertEquals("Alice", event.supporterDisplayName)
        assertEquals(500L, event.amountMinor)
        assertEquals("USD", event.currency)
        assertEquals(1, state.page)
        assertEquals(false, state.hasMore)
    }

    @Test
    fun load_events_is_empty_when_the_unfiltered_first_page_has_none() = runTest {
        val controller = SupportersController(RecordingSupportersApi(ApiResult.Ok(emptyList())))

        controller.loadEvents()

        assertTrue(controller.events.value is EventsState.Empty)
    }

    @Test
    fun a_filtered_empty_page_stays_ready_so_the_filter_bar_survives() = runTest {
        // A filter that matches nothing must NOT collapse to the first-run Empty state — the screen keeps the filter
        // bar visible so the operator can clear it.
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(emptyList()),
                eventStore = listOf(tipEvent("e1", "Alice")),
            )
        val controller = SupportersController(api)

        controller.loadEvents(kind = SupporterEventKind.Charity)

        val state: EventsState = controller.events.value
        assertTrue(state is EventsState.Ready)
        assertTrue((state as EventsState.Ready).events.isEmpty())
        assertEquals(SupporterEventKind.Charity, api.lastEventQuery?.kind)
    }

    @Test
    fun load_events_filters_by_kind() = runTest {
        val api =
            RecordingSupportersApi(
                ApiResult.Ok(emptyList()),
                eventStore =
                    listOf(
                        tipEvent("e1", "Alice"),
                        SupporterEvent(
                            id = "e2",
                            sourceKey = SupporterSourceKey.Kofi,
                            kind = SupporterEventKind.Membership,
                            supporterDisplayName = "Bob",
                            isRecurring = true,
                        ),
                    ),
            )
        val controller = SupportersController(api)

        controller.loadEvents(kind = SupporterEventKind.Membership)

        val state: EventsState = controller.events.value
        assertTrue(state is EventsState.Ready)
        assertEquals(listOf("Bob"), (state as EventsState.Ready).events.map { it.supporterDisplayName })
        assertEquals(SupporterEventKind.Membership, api.lastEventQuery?.kind)
    }

    @Test
    fun load_events_pages_with_the_take_size_and_reports_has_more() = runTest {
        // 30 events over a page size of 25: page 1 fills and reports hasMore; page 2 carries the remainder and does
        // not. Proves the controller sends its PageSize as `take` and threads the backend's hasMore into the pager.
        val store: List<SupporterEvent> = (1..30).map { tipEvent("e$it", "Supporter $it") }
        val api = RecordingSupportersApi(ApiResult.Ok(emptyList()), eventStore = store)
        val controller = SupportersController(api)

        controller.loadEvents(page = 1)
        val first: EventsState = controller.events.value
        assertTrue(first is EventsState.Ready)
        assertEquals(SupportersController.PageSize, (first as EventsState.Ready).events.size)
        assertTrue(first.hasMore)
        assertEquals(SupportersController.PageSize, api.lastEventQuery?.take)

        controller.loadEvents(page = 2)
        val second: EventsState = controller.events.value
        assertTrue(second is EventsState.Ready)
        assertEquals(5, (second as EventsState.Ready).events.size)
        assertEquals(2, second.page)
        assertEquals(false, second.hasMore)
    }

    @Test
    fun load_events_errors_when_the_call_fails() = runTest {
        val controller =
            SupportersController(
                RecordingSupportersApi(
                    ApiResult.Ok(emptyList()),
                    eventsResult = ApiResult.Failure(ApiError(500, "ERR", "kaboom")),
                )
            )

        controller.loadEvents()

        val state: EventsState = controller.events.value
        assertTrue(state is EventsState.Error)
        assertEquals("kaboom", (state as EventsState.Error).detail)
    }

    // ── Minor→major amount formatting (the row's amount) ─────────────────────────────

    @Test
    fun format_supporter_amount_converts_minor_units_to_major_with_currency() = runTest {
        assertEquals("5.00 USD", formatSupporterAmount(500, "USD"))
        assertEquals("12.34", formatSupporterAmount(1234, null))
        assertEquals("0.05 EUR", formatSupporterAmount(5, "EUR"))
        assertEquals("0.00 USD", formatSupporterAmount(0, "USD"))
        assertEquals("10.00 GBP", formatSupporterAmount(1000, "GBP"))
        // A refund/chargeback keeps its sign; a blank currency drops the suffix.
        assertEquals("-2.50 USD", formatSupporterAmount(-250, "USD"))
        assertEquals("2.50", formatSupporterAmount(250, "  "))
        // No amount → no string (the row renders nothing rather than a bogus 0.00).
        assertNull(formatSupporterAmount(null, "USD"))
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private fun readyConnections(controller: SupportersController): List<SupporterConnection> =
        (controller.connections.value as ConnectionsState.Ready).connections

    private fun tipEvent(id: String, name: String): SupporterEvent =
        SupporterEvent(
            id = id,
            sourceKey = SupporterSourceKey.Kofi,
            kind = SupporterEventKind.Tip,
            supporterDisplayName = name,
            amountMinor = 500,
            currency = "USD",
            receivedAt = "2026-07-12T12:00:00Z",
        )
}

/** The query args the events endpoint was last called with — lets a test assert the filter/paging was threaded. */
private data class EventQuery(val page: Int, val take: Int, val kind: String?, val sourceKey: String?)

// A recording fake that behaves like the backend store: connections() returns the live store, and each successful
// write mutates it so the controller's post-write reload observes the real consequence. Upsert adds/updates by
// sourceKey; delete removes. The event feed filters the seeded store by kind/sourceKey and pages by the `take` it
// receives. [writeResult] forces every connection write to fail (store untouched) to exercise the error path.
private class RecordingSupportersApi(
    connectionsInitial: ApiResult<List<SupporterConnection>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    eventStore: List<SupporterEvent> = emptyList(),
    private val eventsResult: ApiResult<SupporterEventsPage>? = null,
) : SupportersApi {
    private val listFailure: ApiError? = (connectionsInitial as? ApiResult.Failure)?.error
    private val store: MutableList<SupporterConnection> =
        (connectionsInitial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()
    private val events: List<SupporterEvent> = eventStore

    val upserts: MutableList<UpsertSupporterConnectionBody> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()
    var lastEventQuery: EventQuery? = null
        private set

    override suspend fun connections(): ApiResult<List<SupporterConnection>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun upsertConnection(body: UpsertSupporterConnectionBody): ApiResult<Unit> {
        upserts += body
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.sourceKey == body.sourceKey }
            val existing: SupporterConnection? = if (index >= 0) store[index] else null
            // Mirror the backend one-step provisioning: a webhook connection given a secret gets an ingest
            // endpoint provisioned from it; a secret-less write leaves the stored secret + endpoint untouched.
            val hasSecret: Boolean = !body.authSecret.isNullOrBlank() || existing?.hasSecret == true
            val endpointUrl: String? =
                if (body.connectionMode == SupporterConnectionMode.Webhook && !body.authSecret.isNullOrBlank())
                    "https://ingest.example/supporters/${body.sourceKey}"
                else existing?.endpointUrl
            val updated =
                SupporterConnection(
                    sourceKey = body.sourceKey,
                    connectionMode = body.connectionMode,
                    hasSecret = hasSecret,
                    isEnabled = body.isEnabled,
                    status = existing?.status ?: SupporterConnectionStatus.Idle,
                    lastEventAt = existing?.lastEventAt,
                    endpointUrl = endpointUrl,
                )
            if (index >= 0) store[index] = updated else store += updated
        }
        return writeResult
    }

    override suspend fun deleteConnection(sourceKey: String): ApiResult<Unit> {
        deleted += sourceKey
        if (writeResult is ApiResult.Ok) store.removeAll { it.sourceKey == sourceKey }
        return writeResult
    }

    override suspend fun events(
        page: Int,
        take: Int,
        kind: String?,
        sourceKey: String?,
    ): ApiResult<SupporterEventsPage> {
        lastEventQuery = EventQuery(page, take, kind, sourceKey)
        eventsResult?.let { return it }

        val filtered: List<SupporterEvent> =
            events.filter { (kind == null || it.kind == kind) && (sourceKey == null || it.sourceKey == sourceKey) }
        val from: Int = (page - 1) * take
        val slice: List<SupporterEvent> =
            if (from >= filtered.size) emptyList() else filtered.subList(from, minOf(from + take, filtered.size))
        val hasMore: Boolean = filtered.size > page * take
        return ApiResult.Ok(SupporterEventsPage(data = slice, hasMore = hasMore))
    }
}
