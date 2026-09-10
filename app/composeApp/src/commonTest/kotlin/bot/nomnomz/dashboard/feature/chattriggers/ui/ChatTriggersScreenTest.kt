// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chattriggers.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatTrigger
import bot.nomnomz.dashboard.core.network.ChatTriggersApi
import bot.nomnomz.dashboard.core.network.CreateChatTriggerBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdateChatTriggerBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.feature.chattriggers.state.ChatTriggersController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// S-RICH-PICKERS — Chat Triggers' pipeline-bind field now goes through the shared [PipelineBindPicker] (the same
// component Commands / Giveaways / Rewards / EventResponses / Timers already use), not a raw id text field or an
// EntityPickerField showing the bare id. Proves the bind field renders the pipeline's NAME on screen (the raw
// server id never appears as visible text) while the write that actually reaches the backend still carries the
// real pipeline id — the picker is cosmetic sugar over the id, not a replacement for it.
@OptIn(ExperimentalTestApi::class)
class ChatTriggersScreenTest {

    @Test
    fun editing_a_pipeline_bound_trigger_shows_the_pipeline_by_name_and_saves_the_real_id() = runComposeUiTest {
        val trigger =
            ChatTrigger(
                id = "t1",
                pattern = "hello",
                matchType = "contains",
                caseSensitive = false,
                isEnabled = true,
                response = null,
                pipelineId = "pipe-abc123",
                cooldownSeconds = 30,
                minPermissionLevel = "Everyone",
            )
        val chatTriggersApi = RecordingChatTriggersApi(listOf(trigger))
        val pipelinesApi = FakePipelinesApi(listOf(PipelineSummary(id = "pipe-abc123", name = "Follow chain")))
        val controller =
            ChatTriggersController(
                channelsApi = FakeChannelsApi(),
                chatTriggersApi = chatTriggersApi,
                pipelinesApi = pipelinesApi,
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        ChatTriggersScreen(
                            controller = controller,
                            role = ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Edit trigger hello").performClick()
        waitForIdle()

        // The picker already resolved the bound id to its real name — the server-assigned id string is never
        // rendered as visible text anywhere on the dialog.
        onNodeWithText("Follow chain", substring = true).assertExists()
        onNodeWithText("pipe-abc123", substring = true).assertDoesNotExist()

        onNodeWithText("Save").performClick()
        waitForIdle()

        // The write that actually reached the backend still carries the real pipeline id, not the displayed name —
        // the picker only changes what an operator SEES, never what gets persisted.
        assertEquals("pipe-abc123", chatTriggersApi.lastUpdateBody?.pipelineId)
    }
}

@androidx.compose.runtime.Composable
private fun withLifecycle(content: @androidx.compose.runtime.Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    androidx.compose.runtime.CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

private class FakeTemplateHelpersApi : TemplateHelpersApi {
    override suspend fun helpers(context: TemplateHelperContext, eventType: String?): ApiResult<List<TemplateHelperDto>> =
        ApiResult.Ok(emptyList())
}

private class FakeChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))
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

// Records the exact body the trigger's Save sends on update — the proof the picker binds by the real pipeline id
// while showing the operator only the pipeline's name.
private class RecordingChatTriggersApi(initial: List<ChatTrigger>) : ChatTriggersApi {
    private val rows: MutableList<ChatTrigger> = initial.toMutableList()
    var lastUpdateBody: UpdateChatTriggerBody? = null

    override suspend fun list(channelId: String): ApiResult<List<ChatTrigger>> = ApiResult.Ok(rows.toList())

    override suspend fun create(channelId: String, body: CreateChatTriggerBody): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun update(
        channelId: String,
        triggerId: String,
        body: UpdateChatTriggerBody,
    ): ApiResult<Unit> {
        lastUpdateBody = body
        return ApiResult.Ok(Unit)
    }

    override suspend fun delete(channelId: String, triggerId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakePipelinesApi(private val pipelines: List<PipelineSummary>) : PipelinesApi {
    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> = ApiResult.Ok(pipelines)
    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())
    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = id))
    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = "new-pipe", name = body.name))
    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        ApiResult.Ok(PipelineBlastRadiusSummary())
    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> =
        ApiResult.Ok(TestRunResult(success = true))
}
