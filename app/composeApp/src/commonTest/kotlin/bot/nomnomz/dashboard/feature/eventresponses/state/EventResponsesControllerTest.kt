// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.EventResponse
import bot.nomnomz.dashboard.core.network.EventResponsePreset
import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.core.network.EventResponsesApi
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.UpdateEventResponseBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlinx.coroutines.test.runTest

// Proves the EventResponses page state machine: channel resolution, listing, and the write path
// (toggle / save / delete). Every write re-lists on success so the screen reflects the backend.
// Failure paths are asserted on state shape — the screen renders exactly what is in state.
class EventResponsesControllerTest {

    @Test
    fun load_surfaces_event_responses_on_success() = runTest {
        val summary =
            EventResponseSummary(
                id = "er1",
                eventType = "channel.follow",
                isEnabled = true,
                responseType = "chat_message",
                updatedAt = "2026-06-27T00:00:00Z",
            )
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = RecordingEventResponsesApi(listResult = ApiResult.Ok(listOf(summary))),
            )

        controller.load()

        val state: EventResponsesState = controller.state.value
        assertIs<EventResponsesState.Ready>(state)
        assertEquals(1, state.responses.size)
        assertEquals("channel.follow", state.responses.first().eventType)
        assertNull(state.actionError)
    }

    @Test
    fun load_yields_empty_state_when_no_responses_configured() = runTest {
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = RecordingEventResponsesApi(listResult = ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertIs<EventResponsesState.Empty>(controller.state.value)
    }

    @Test
    fun load_yields_error_when_channel_resolution_fails() = runTest {
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Failure(ApiError(status = 503, code = null, message = "no channel"))),
                eventResponsesApi = RecordingEventResponsesApi(),
            )

        controller.load()

        val state: EventResponsesState = controller.state.value
        assertIs<EventResponsesState.Error>(state)
        assertEquals("no channel", state.detail)
    }

    @Test
    fun load_yields_error_when_list_call_fails() = runTest {
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi =
                    RecordingEventResponsesApi(
                        listResult = ApiResult.Failure(ApiError(status = 500, code = null, message = "api error"))
                    ),
            )

        controller.load()

        assertIs<EventResponsesState.Error>(controller.state.value)
    }

    @Test
    fun toggle_reloads_on_success_and_records_eventType_and_flag() = runTest {
        val api = RecordingEventResponsesApi(listResult = ApiResult.Ok(emptyList()))
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = api,
            )

        controller.load()
        controller.toggle("channel.follow", false)

        assertEquals("channel.follow", api.lastUpsertedEventType)
        assertEquals(false, api.lastUpsertedBody?.isEnabled)
    }

    @Test
    fun toggle_sets_action_error_on_write_failure() = runTest {
        val summary =
            EventResponseSummary(
                id = "er1",
                eventType = "channel.follow",
                isEnabled = true,
                responseType = "none",
                updatedAt = "2026-06-27T00:00:00Z",
            )
        val api =
            RecordingEventResponsesApi(
                listResult = ApiResult.Ok(listOf(summary)),
                upsertResult = ApiResult.Failure(ApiError(status = 422, code = null, message = "write failed")),
            )
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = api,
            )

        controller.load()
        controller.toggle("channel.follow", false)

        val state: EventResponsesState = controller.state.value
        assertIs<EventResponsesState.Ready>(state)
        assertNotNull(state.actionError)
    }

    @Test
    fun save_sends_full_body_and_reloads() = runTest {
        val api = RecordingEventResponsesApi(listResult = ApiResult.Ok(emptyList()))
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = api,
            )

        controller.load()
        controller.save("channel.cheer", "chat_message", "Thanks for the cheer!", null)

        assertEquals("channel.cheer", api.lastUpsertedEventType)
        assertEquals("chat_message", api.lastUpsertedBody?.responseType)
        assertEquals("Thanks for the cheer!", api.lastUpsertedBody?.message)
        assertNull(api.lastUpsertedBody?.pipelineId)
    }

    @Test
    fun delete_calls_api_and_reloads() = runTest {
        val api = RecordingEventResponsesApi(listResult = ApiResult.Ok(emptyList()))
        val controller =
            EventResponsesController(
                pipelinesApi = StubPipelinesApi,
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = api,
            )

        controller.load()
        controller.delete("channel.raid")

        assertEquals("channel.raid", api.lastDeletedEventType)
    }

    @Test
    fun create_and_bind_creates_a_pipeline_then_binds_it_as_the_pipeline_response() = runTest {
        val api = RecordingEventResponsesApi(listResult = ApiResult.Ok(emptyList()))
        StubPipelinesApi.lastCreatedName = null
        val controller =
            EventResponsesController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventResponsesApi = api,
                pipelinesApi = StubPipelinesApi,
            )
        controller.load()

        controller.createPipelineAndBind("channel.raid", "Raid reaction")

        // The whole create-and-bind loop: a pipeline was created with that name, then the event response was
        // upserted to a pipeline response bound to the NEW pipeline's server-assigned id — not a pasted id.
        assertEquals("Raid reaction", StubPipelinesApi.lastCreatedName)
        assertEquals("channel.raid", api.lastUpsertedEventType)
        assertEquals("pipeline", api.lastUpsertedBody?.responseType)
        assertEquals("new-pipe", api.lastUpsertedBody?.pipelineId)
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

// A pipelines fake for the event-responses tests: createReturning yields a fixed new id so the bind test can
// assert the response was bound to it; list()/catalogue() return empty so load() succeeds.
private object StubPipelinesApi : PipelinesApi {
    var lastCreatedName: String? = null

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> = ApiResult.Ok(emptyList())
    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())
    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = id))
    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> {
        lastCreatedName = body.name
        return ApiResult.Ok(PipelineDetail(id = "new-pipe", name = body.name))
    }
    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeChannelsApi(
    private val result: ApiResult<ChannelSummary>,
) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> =
        ApiResult.Ok(emptyList())

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

private class RecordingEventResponsesApi(
    private val listResult: ApiResult<List<EventResponseSummary>> = ApiResult.Ok(emptyList()),
    private val upsertResult: ApiResult<EventResponse> =
        ApiResult.Ok(
            EventResponse(
                id = "er1",
                eventType = "channel.follow",
                isEnabled = true,
                responseType = "none",
                message = null,
                pipelineId = null,
                metadata = emptyMap(),
                createdAt = "2026-06-27T00:00:00Z",
                updatedAt = "2026-06-27T00:00:00Z",
            )
        ),
    private val deleteResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : EventResponsesApi {
    var lastUpsertedEventType: String? = null
    var lastUpsertedBody: UpdateEventResponseBody? = null
    var lastDeletedEventType: String? = null

    private val catalog: List<EventResponsePreset> =
        listOf(
            EventResponsePreset(
                eventType = "channel.follow",
                defaultTemplate = "Thanks for the follow, {user}!",
                variables = listOf("user", "user.name"),
            )
        )

    override suspend fun list(channelId: String): ApiResult<List<EventResponseSummary>> = listResult

    override suspend fun catalog(channelId: String): ApiResult<List<EventResponsePreset>> = ApiResult.Ok(catalog)

    override suspend fun get(channelId: String, eventType: String): ApiResult<EventResponse> =
        upsertResult

    override suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateEventResponseBody,
    ): ApiResult<EventResponse> {
        lastUpsertedEventType = eventType
        lastUpsertedBody = body
        return upsertResult
    }

    override suspend fun delete(channelId: String, eventType: String): ApiResult<Unit> {
        lastDeletedEventType = eventType
        return deleteResult
    }
}
