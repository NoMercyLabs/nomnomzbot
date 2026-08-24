// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chattriggers.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatTrigger
import bot.nomnomz.dashboard.core.network.ChatTriggersApi
import bot.nomnomz.dashboard.core.network.CreateChatTriggerBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.UpdateChatTriggerBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_chat_trigger_deleted
import nomnomzbot.composeapp.generated.resources.feedback_chat_trigger_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_chat_trigger_saved

// Proves the Chat Triggers page announces every write's outcome on the frame (the "consequence visibility"
// requirement — a save must be seen, a failed save must show the real reason and never look like success), on
// top of the existing reload-on-write behavior the controller already had. Mirrors QuotesControllerTest /
// TimersControllerTest's feedback assertions.
class ChatTriggersControllerTest {

    @Test
    fun a_successful_create_announces_save_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val api = FakeChatTriggersApi(emptyList())
        val controller = chatTriggersController(api = api, feedback = feedback)
        controller.load()

        controller.createTrigger(
            pattern = "hello",
            matchType = "contains",
            caseSensitive = false,
            isEnabled = true,
            response = "Hi there!",
            pipelineId = null,
            cooldownSeconds = 30,
            minPermissionLevel = 0,
        )

        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_chat_trigger_saved, feedback.only.label)
    }

    @Test
    fun a_successful_delete_announces_the_deleted_label() = runTest {
        val feedback = RecordingFeedback()
        val api = FakeChatTriggersApi(listOf(ChatTrigger(id = "t1", pattern = "bye")))
        val controller = chatTriggersController(api = api, feedback = feedback)
        controller.load()

        controller.deleteTrigger(triggerId = "t1")

        // A delete says "deleted", not the generic "saved" — the success message is action-specific.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_chat_trigger_deleted, feedback.only.label)
        // And the row is actually gone (real consequence, not just a call that happened).
        assertTrue(controller.state.value is ChatTriggersState.Empty)
    }

    @Test
    fun a_failed_write_announces_an_error_carrying_the_backend_detail_and_no_success() = runTest {
        val feedback = RecordingFeedback()
        val api =
            FakeChatTriggersApi(
                emptyList(),
                writeFailure = ApiError(400, "VALIDATION_FAILED", "regex did not compile"),
            )
        val controller = chatTriggersController(api = api, feedback = feedback)
        controller.load()

        controller.createTrigger(
            pattern = "(",
            matchType = "regex",
            caseSensitive = false,
            isEnabled = true,
            response = "boom",
            pipelineId = null,
            cooldownSeconds = 30,
            minPermissionLevel = 0,
        )

        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_chat_trigger_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("regex did not compile"), feedback.only.formatArgs)
        // The failure banner is ALSO surfaced in-page — never a silent failure.
        val state: ChatTriggersState = controller.state.value
        assertTrue(state is ChatTriggersState.Empty)
        assertEquals("regex did not compile", (state as ChatTriggersState.Empty).actionError)
    }
}

private fun chatTriggersController(
    api: ChatTriggersApi,
    feedback: Feedback = NoOpFeedback,
): ChatTriggersController =
    ChatTriggersController(
        channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
        chatTriggersApi = api,
        pipelinesApi = FakePipelinesApi(),
        feedback = feedback,
    )

// A stateful fake backing the trigger list: writes mutate an in-memory list (so a reload reflects them); an
// optional [writeFailure] forces every write to fail (the store is left untouched) to exercise the error path.
private class FakeChatTriggersApi(
    initial: List<ChatTrigger>,
    private val writeFailure: ApiError? = null,
) : ChatTriggersApi {
    private val rows: MutableList<ChatTrigger> = initial.toMutableList()

    override suspend fun list(channelId: String): ApiResult<List<ChatTrigger>> = ApiResult.Ok(rows.toList())

    override suspend fun create(channelId: String, body: CreateChatTriggerBody): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        rows +=
            ChatTrigger(
                id = "trigger-${rows.size + 1}",
                pattern = body.pattern,
                matchType = body.matchType,
                caseSensitive = body.caseSensitive,
                isEnabled = body.isEnabled,
                response = body.response,
                pipelineId = body.pipelineId,
                cooldownSeconds = body.cooldownSeconds,
                minPermissionLevel = body.minPermissionLevel,
            )
        return ApiResult.Ok(Unit)
    }

    override suspend fun update(
        channelId: String,
        triggerId: String,
        body: UpdateChatTriggerBody,
    ): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        return ApiResult.Ok(Unit)
    }

    override suspend fun delete(channelId: String, triggerId: String): ApiResult<Unit> {
        writeFailure?.let { return ApiResult.Failure(it) }
        rows.removeAll { it.id == triggerId }
        return ApiResult.Ok(Unit)
    }
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
