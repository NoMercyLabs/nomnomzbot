// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasAnyChild
import androidx.compose.ui.test.hasContentDescription
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
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.CreateTimerRequest
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListPreview
import bot.nomnomz.dashboard.core.network.PickListsApi
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
import bot.nomnomz.dashboard.core.network.TimerDetail
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.UpdateTimerRequest
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// S047-remaining — proves the Test action next to a timer's bound pipeline actually works, the same two
// behaviors already proven for Commands (CommandsScreenTest), now proven for Timers too: with a pipeline
// bound, clicking it opens the shared dry-run dialog and calls the REAL test-run endpoint for the EXACT bound
// pipeline id (asserted on the fake pipelines API, not a "didn't crash" check); with no pipeline bound, the
// button renders DISABLED — never hidden, per the house "disable, don't hide" rule.
@OptIn(ExperimentalTestApi::class)
class TimersScreenTest {

    @Test
    fun test_action_calls_the_dry_run_endpoint_for_the_bound_pipeline_when_editing_a_bound_timer() =
        runComposeUiTest {
            val pipelinesApi = RecordingPipelinesApi()
            val bound = TimerSummary(id = "t1", name = "shoutout timer", intervalMinutes = 30, isEnabled = true)
            val detail =
                TimerDetail(
                    id = "t1",
                    name = "shoutout timer",
                    messages = listOf("hello"),
                    intervalMinutes = 30,
                    isEnabled = true,
                    pipelineId = "pipe-1",
                )
            val controller =
                TimersController(
                    channelsApi = FakeChannelsApi(),
                    timersApi = FakeTimersApi(listOf(bound), detail),
                    pipelinesApi = pipelinesApi,
                    pickListsApi = FakePickListsApi(),
                )
            runBlocking { controller.load() }

            setContent {
                withLifecycle {
                    NomNomzTheme {
                        bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                            TimersScreen(
                                controller = controller,
                                role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                                templateHelpersApi = FakeTemplateHelpersApi(),
                            )
                        }
                    }
                }
            }
            waitForIdle()

            onNodeWithContentDescription("Edit shoutout timer").performClick()
            waitForIdle()

            onNodeWithContentDescription("Test pipeline").performClick()
            waitForIdle()
            onNodeWithText("Run test").performClick()
            waitForIdle()

            assertEquals("pipe-1", pipelinesApi.lastTestRunPipelineId)
        }

    @Test
    fun test_action_is_disabled_when_no_pipeline_is_bound_yet() = runComposeUiTest {
        val bound = TimerSummary(id = "t1", name = "shoutout timer", intervalMinutes = 30, isEnabled = true)
        val detail =
            TimerDetail(
                id = "t1",
                name = "shoutout timer",
                messages = listOf("hello"),
                intervalMinutes = 30,
                isEnabled = true,
                pipelineId = null,
            )
        val controller =
            TimersController(
                channelsApi = FakeChannelsApi(),
                // Non-empty pipelines list — the "Test pipeline" row only renders when either the picker has
                // options or a pipeline is already bound (TimersScreen.kt: `pipelines.isNotEmpty() || pipelineId
                // != null`); with none bound the action must still show, just disabled.
                timersApi = FakeTimersApi(listOf(bound), detail),
                pipelinesApi = RecordingPipelinesApi(),
                pickListsApi = FakePickListsApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        TimersScreen(
                            controller = controller,
                            role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Edit shoutout timer").performClick()
        waitForIdle()

        // Never hidden — same shape as CommandsScreenTest: the shared ManageGate wraps the GlyphButton in a
        // parent node carrying [Disabled] + stateDescription, so assert there rather than on the leaf.
        onNode(hasAnyChild(hasContentDescription("Test pipeline")), useUnmergedTree = true).assertIsNotEnabled()
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

// `detail` always answers with [fixedDetail] so the edit dialog pre-fills the exact bound pipeline id the test
// wants to exercise, regardless of which row id is opened.
private class FakeTimersApi(
    private val summaries: List<TimerSummary>,
    private val fixedDetail: TimerDetail,
) : TimersApi {
    override suspend fun list(channelId: String): ApiResult<List<TimerSummary>> = ApiResult.Ok(summaries)
    override suspend fun create(channelId: String, request: CreateTimerRequest): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun update(channelId: String, id: String, request: UpdateTimerRequest): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun toggle(channelId: String, id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun detail(channelId: String, id: String): ApiResult<TimerDetail> = ApiResult.Ok(fixedDetail)
}

private class FakePickListsApi : PickListsApi {
    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> = ApiResult.Ok(BlastRadiusSummary())
    override suspend fun list(): ApiResult<List<PickList>> = ApiResult.Ok(emptyList())
    override suspend fun get(id: String): ApiResult<PickList> = error("stub")
    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun delete(id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun pick(id: String): ApiResult<PickListPreview> = error("stub")
}

// Records the exact pipeline id the Test action's dry-run call reaches the backend with — the proof that
// clicking Test on a bound pipeline calls the real endpoint for THAT pipeline, not a stub.
private class RecordingPipelinesApi : PipelinesApi {
    var lastTestRunPipelineId: String? = null
    var lastTestRunChannelId: String? = null

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        ApiResult.Ok(listOf(PipelineSummary(id = "pipe-1", name = "Shoutout chain")))
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
    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> {
        lastTestRunPipelineId = id
        lastTestRunChannelId = channelId
        return ApiResult.Ok(TestRunResult(success = true))
    }
}
