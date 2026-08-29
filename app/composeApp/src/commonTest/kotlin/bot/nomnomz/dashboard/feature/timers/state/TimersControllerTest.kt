// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.CreateTimerRequest
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.TimerDetail
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.UpdateTimerRequest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_timer_deleted
import nomnomzbot.composeapp.generated.resources.feedback_timer_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_timer_saved
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary

// Proves the Timers page state machine the screen renders: resolve the active channel, then surface the real
// scheduled timers — Empty when there are none, or an Error if either step fails — and the create / edit /
// toggle / delete management surface: a successful write reloads the list (so the screen reflects the backend's
// truth), and a failed write surfaces the message without disturbing the list. The screen is a pure projection
// of this, so testing it proves the page shows real rows (no fabricated timers), mutates them, and degrades
// cleanly.
class TimersControllerTest {

    @Test
    fun load_surfaces_the_channels_timers_on_success() = runTest {
        val controller =
            timersController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTimersApi(
                    listOf(
                        TimerSummary(
                            id = "00000007-0000-0000-0000-000000000007",
                            name = "Follow reminder",
                            intervalMinutes = 10,
                            isEnabled = true,
                            messageCount = 3,
                        )
                    )
                ),
            )

        controller.load()

        val state: TimersState = controller.state.value
        assertTrue(state is TimersState.Ready)
        val timers: List<TimerSummary> = (state as TimersState.Ready).timers
        assertEquals(1, timers.size)
        val timer: TimerSummary = timers.first()
        assertEquals("00000007-0000-0000-0000-000000000007", timer.id)
        assertEquals("Follow reminder", timer.name)
        assertEquals(10, timer.intervalMinutes)
        assertEquals(true, timer.isEnabled)
        assertEquals(3, timer.messageCount)
    }

    @Test
    fun load_reports_empty_when_the_channel_has_no_timers() = runTest {
        val controller =
            timersController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTimersApi(emptyList()),
            )

        controller.load()

        assertTrue(controller.state.value is TimersState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            timersController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeTimersApi(emptyList()),
            )

        controller.load()

        assertTrue(controller.state.value is TimersState.Error)
    }

    @Test
    fun load_errors_when_the_timers_call_fails() = runTest {
        val controller =
            timersController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTimersApi(emptyList(), listFailure = ApiError(500, "ERR", "boom")),
            )

        controller.load()

        assertTrue(controller.state.value is TimersState.Error)
    }

    @Test
    fun createTimer_persists_the_request_and_reloads_the_list() = runTest {
        val api = FakeTimersApi(emptyList())
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.createTimer(name = "Discord", messages = listOf("Join the Discord!"), intervalMinutes = 20, minChatActivity = 3, enabled = true, fireOnce = true, pipelineId = null)

        // The write hit the backend with exactly the dialog's fields, single message folded into the list.
        assertEquals(1, api.created.size)
        val request: CreateTimerRequest = api.created.first()
        assertEquals("Discord", request.name)
        assertEquals(listOf("Join the Discord!"), request.messages)
        assertEquals(20, request.intervalMinutes)
        assertEquals(3, request.minChatActivity)
        assertEquals(true, request.isEnabled)
        assertEquals(true, request.fireOnce)

        // The list reloaded after the write, so the new row is now on the page (no fabricated state).
        val state: TimersState = controller.state.value
        assertTrue(state is TimersState.Ready)
        val timers: List<TimerSummary> = (state as TimersState.Ready).timers
        assertEquals(1, timers.size)
        assertEquals("Discord", timers.first().name)
        assertNull(controller.writeError.value)
    }

    @Test
    fun updateTimer_sends_the_edited_fields_and_reloads() = runTest {
        val api =
            FakeTimersApi(
                listOf(TimerSummary(id = "00000003-0000-0000-0000-000000000003", name = "Old", intervalMinutes = 5, isEnabled = false, messageCount = 1))
            )
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.updateTimer(id = "00000003-0000-0000-0000-000000000003", name = "New", messages = listOf("Updated"), intervalMinutes = 15, minChatActivity = 0, enabled = true, fireOnce = false, pipelineId = null)

        val updatedId = "00000003-0000-0000-0000-000000000003"
        val update: UpdateTimerRequest = api.updated.getValue(updatedId)
        assertEquals("New", update.name)
        assertEquals(listOf("Updated"), update.messages)
        assertEquals(15, update.intervalMinutes)
        assertEquals(true, update.isEnabled)
        assertEquals(false, update.fireOnce)

        val state: TimersState = controller.state.value
        assertTrue(state is TimersState.Ready)
        val timer: TimerSummary = (state as TimersState.Ready).timers.single { it.id == updatedId }
        assertEquals("New", timer.name)
        assertEquals(15, timer.intervalMinutes)
        assertEquals(true, timer.isEnabled)
    }

    @Test
    fun toggleTimer_flips_the_row_server_side_and_reloads() = runTest {
        val api =
            FakeTimersApi(
                listOf(TimerSummary(id = "00000009-0000-0000-0000-000000000009", name = "Ad", intervalMinutes = 30, isEnabled = true, messageCount = 2))
            )
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.toggleTimer(id = "00000009-0000-0000-0000-000000000009", enabled = false)

        assertEquals(listOf("00000009-0000-0000-0000-000000000009"), api.toggled)
        val state: TimersState = controller.state.value
        assertTrue(state is TimersState.Ready)
        val toggledId = "00000009-0000-0000-0000-000000000009"
        assertEquals(false, (state as TimersState.Ready).timers.single { it.id == toggledId }.isEnabled)
    }

    @Test
    fun deleteTimer_removes_the_row_and_reloads_to_empty() = runTest {
        val api =
            FakeTimersApi(
                listOf(TimerSummary(id = "00000004-0000-0000-0000-000000000004", name = "Solo", intervalMinutes = 10, isEnabled = true, messageCount = 1))
            )
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.deleteTimer(id = "00000004-0000-0000-0000-000000000004")

        assertEquals(listOf("00000004-0000-0000-0000-000000000004"), api.deleted)
        // The reload now sees an empty channel, so the page degrades to Empty rather than a stale row.
        assertTrue(controller.state.value is TimersState.Empty)
        assertNull(controller.writeError.value)
    }

    @Test
    fun createTimer_failure_surfaces_a_write_error_and_leaves_the_list_intact() = runTest {
        val existing: List<TimerSummary> =
            listOf(TimerSummary(id = "00000001-0000-0000-0000-000000000001", name = "Keep", intervalMinutes = 10, isEnabled = true, messageCount = 1))
        val api = FakeTimersApi(existing, writeFailure = ApiError(403, "FORBIDDEN", "not allowed"))
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.createTimer(name = "Nope", messages = listOf("blocked"), intervalMinutes = 30, minChatActivity = 0, enabled = true, fireOnce = false, pipelineId = null)

        // The error is surfaced verbatim, the list never reloaded, and the original row is still on the page.
        assertEquals("not allowed", controller.writeError.value)
        assertEquals(1, api.listCalls) // only the initial load, no reload after the failed write
        val state: TimersState = controller.state.value
        assertTrue(state is TimersState.Ready)
        assertEquals(listOf("Keep"), (state as TimersState.Ready).timers.map { it.name })
    }

    @Test
    fun clearWriteError_dismisses_the_banner() = runTest {
        val api = FakeTimersApi(emptyList(), writeFailure = ApiError(500, "ERR", "boom"))
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()
        controller.deleteTimer(id = "00000001-0000-0000-0000-000000000001")
        assertEquals("boom", controller.writeError.value)

        controller.clearWriteError()

        assertNull(controller.writeError.value)
    }

    @Test
    fun a_successful_create_announces_save_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), FakeTimersApi(emptyList()), feedback = feedback)
        controller.load()

        controller.createTimer(name = "Discord", messages = listOf("Join!"), intervalMinutes = 20, minChatActivity = 3, enabled = true, fireOnce = true, pipelineId = null)

        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_timer_saved, feedback.only.label)
    }

    @Test
    fun a_successful_delete_announces_the_deleted_label() = runTest {
        val feedback = RecordingFeedback()
        val api = FakeTimersApi(listOf(TimerSummary(id = "00000005-0000-0000-0000-000000000005", name = "Bye", intervalMinutes = 5, isEnabled = true, messageCount = 1)))
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, feedback = feedback)
        controller.load()

        controller.deleteTimer(id = "00000005-0000-0000-0000-000000000005")

        // A delete says "deleted", not the generic "saved" — the success message is action-specific.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_timer_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_write_announces_an_error_carrying_the_backend_detail_and_no_success() = runTest {
        val feedback = RecordingFeedback()
        val api = FakeTimersApi(emptyList(), writeFailure = ApiError(403, "FORBIDDEN", "no permission"))
        val controller = timersController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, feedback = feedback)
        controller.load()

        controller.createTimer(name = "Nope", messages = listOf("blocked"), intervalMinutes = 30, minChatActivity = 0, enabled = true, fireOnce = false, pipelineId = null)

        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_timer_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("no permission"), feedback.only.formatArgs)
    }

    // S-BUDGETS-b3: the values [TimersScreen] feeds its warn-before-refuse New-button wrapper must be exactly
    // what the billing-limits endpoint returned — never a client-recomputed count — and must find the "timers"
    // row among other unrelated resources in the same report.
    @Test
    fun load_exposes_the_channels_real_timers_usage_from_the_billing_limits_report() = runTest {
        val usage =
            bot.nomnomz.dashboard.core.network.ResourceUsage(
                limitKey = "timers",
                classWire = 1,
                displayName = "Timers",
                currentCount = 97,
                limit = 100,
                safetyBaseline = 100,
            )
        val unrelated =
            bot.nomnomz.dashboard.core.network.ResourceUsage(
                limitKey = "custom_commands",
                classWire = 1,
                displayName = "Custom commands",
                currentCount = 1,
                limit = 200,
                safetyBaseline = 200,
            )
        val controller =
            timersController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                timersApi = FakeTimersApi(emptyList()),
                resourceLimits = { ApiResult.Ok(listOf(unrelated, usage)) },
            )

        controller.load()

        val resolved: bot.nomnomz.dashboard.core.network.ResourceUsage? = controller.timersUsage.value
        assertEquals(97L, resolved?.currentCount)
        assertEquals(100L, resolved?.limit)
    }

    @Test
    fun a_failed_limits_fetch_leaves_the_usage_null_rather_than_failing_the_page() = runTest {
        val controller =
            timersController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                timersApi = FakeTimersApi(emptyList()),
                resourceLimits = { ApiResult.Failure(ApiError(500, "ERR", "boom")) },
            )

        controller.load()

        assertEquals(null, controller.timersUsage.value)
        assertTrue(controller.state.value is TimersState.Empty)
    }
}

// Builds a controller with an empty-pipeline fake so the tests that don't exercise the picker stay unchanged.
private fun timersController(
    channelsApi: ChannelsApi,
    timersApi: TimersApi,
    pipelinesApi: PipelinesApi = FakePipelinesApi(),
    feedback: Feedback = NoOpFeedback,
    resourceLimits: suspend (String) -> ApiResult<List<bot.nomnomz.dashboard.core.network.ResourceUsage>> =
        { ApiResult.Ok(emptyList()) },
): TimersController =
    TimersController(channelsApi, timersApi, pipelinesApi, FakePickListsApi(), feedback, resourceLimits)

private class FakePickListsApi : bot.nomnomz.dashboard.core.network.PickListsApi {
    // Not exercised here: the counted delete preview has its own tests (DeleteBlastRadiusDialogTest and the
    // backend's blast-radius suites). The seam is implemented so the double stays a real implementation.
    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

    override suspend fun list(): ApiResult<List<bot.nomnomz.dashboard.core.network.PickList>> =
        ApiResult.Ok(emptyList())
    override suspend fun get(id: String): ApiResult<bot.nomnomz.dashboard.core.network.PickList> = error("stub")
    override suspend fun create(body: bot.nomnomz.dashboard.core.network.CreatePickListBody): ApiResult<Unit> =
        error("stub")
    override suspend fun update(
        id: String,
        body: bot.nomnomz.dashboard.core.network.UpdatePickListBody,
    ): ApiResult<Unit> = error("stub")
    override suspend fun delete(id: String): ApiResult<Unit> = error("stub")
    override suspend fun pick(id: String): ApiResult<bot.nomnomz.dashboard.core.network.PickListPreview> =
        error("stub")
}

private class FakePipelinesApi(private val pipelines: List<PipelineSummary> = emptyList()) : PipelinesApi {
    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> = ApiResult.Ok(pipelines)

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> = error("stub")

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = error("stub")

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        error("stub")

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        error("stub")

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = error("stub")

    override suspend fun blastRadius(channelId: String, id: String): ApiResult<bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary> =
        error("stub")

    override suspend fun testRun(
        channelId: String,
        id: String,
        body: bot.nomnomz.dashboard.core.network.PipelineTestRunBody,
    ): ApiResult<bot.nomnomz.dashboard.core.network.TestRunResult> = error("stub")
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

// A stateful fake backing the timer list: writes mutate an in-memory list (so a reload reflects them), every
// call is recorded for assertions, and an optional failure lets a test prove the error path. `listCalls`
// proves whether a reload actually ran after a write.
private class FakeTimersApi(
    initial: List<TimerSummary>,
    private val listFailure: ApiError? = null,
    private val writeFailure: ApiError? = null,
) : TimersApi {
    private val rows: MutableList<TimerSummary> = initial.toMutableList()

    var listCalls: Int = 0
        private set

    val created: MutableList<CreateTimerRequest> = mutableListOf()
    val updated: MutableMap<String, UpdateTimerRequest> = mutableMapOf()
    val deleted: MutableList<String> = mutableListOf()
    val toggled: MutableList<String> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<TimerSummary>> {
        listCalls++
        return listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(rows.toList())
    }

    override suspend fun create(channelId: String, request: CreateTimerRequest): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        created += request
        val nextId: String = "test-timer-${rows.size + 1}"
        rows +=
            TimerSummary(
                id = nextId,
                name = request.name,
                intervalMinutes = request.intervalMinutes,
                isEnabled = request.isEnabled,
                messageCount = request.messages.size,
            )
        return ApiResult.Ok(Unit)
    }

    override suspend fun update(channelId: String, id: String, request: UpdateTimerRequest): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        updated[id] = request
        val index: Int = rows.indexOfFirst { it.id == id }
        if (index >= 0) {
            val current: TimerSummary = rows[index]
            rows[index] =
                current.copy(
                    name = request.name ?: current.name,
                    intervalMinutes = request.intervalMinutes ?: current.intervalMinutes,
                    isEnabled = request.isEnabled ?: current.isEnabled,
                    messageCount = request.messages?.size ?: current.messageCount,
                )
        }
        return ApiResult.Ok(Unit)
    }

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        deleted += id
        rows.removeAll { it.id == id }
        return ApiResult.Ok(Unit)
    }

    override suspend fun toggle(channelId: String, id: String): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        toggled += id
        val index: Int = rows.indexOfFirst { it.id == id }
        if (index >= 0) rows[index] = rows[index].copy(isEnabled = !rows[index].isEnabled)
        return ApiResult.Ok(Unit)
    }

    override suspend fun detail(channelId: String, id: String): ApiResult<TimerDetail> {
        val row: TimerSummary? = rows.firstOrNull { it.id == id }
        return if (row != null)
            ApiResult.Ok(
                TimerDetail(
                    id = row.id,
                    name = row.name,
                    intervalMinutes = row.intervalMinutes,
                    isEnabled = row.isEnabled,
                )
            )
        else ApiResult.Failure(ApiError(404, "NOT_FOUND", "no timer"))
    }
}
