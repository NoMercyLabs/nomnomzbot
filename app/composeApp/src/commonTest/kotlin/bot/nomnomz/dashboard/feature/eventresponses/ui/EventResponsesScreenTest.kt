// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.ui

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
import bot.nomnomz.dashboard.core.network.EventResponse
import bot.nomnomz.dashboard.core.network.EventResponsePreset
import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.core.network.EventResponsesApi
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
import bot.nomnomz.dashboard.core.network.UpdateEventResponseBody
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import bot.nomnomz.dashboard.feature.eventresponses.state.EventResponsesController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// S047-remaining — proves the Test action next to an event response's bound pipeline actually works, the same
// two behaviors already proven for Commands (CommandsScreenTest), now proven for EventResponses too: with a
// pipeline bound, clicking it opens the shared dry-run dialog and calls the REAL test-run endpoint for the
// EXACT bound pipeline id (asserted on the fake pipelines API, not a "didn't crash" check); with no pipeline
// bound, the button renders DISABLED — never hidden, per the house "disable, don't hide" rule.
@OptIn(ExperimentalTestApi::class)
class EventResponsesScreenTest {

    @Test
    fun test_action_calls_the_dry_run_endpoint_for_the_bound_pipeline_when_editing_a_bound_response() =
        runComposeUiTest {
            val pipelinesApi = RecordingPipelinesApi()
            val summary =
                EventResponseSummary(
                    id = "er1",
                    eventType = "channel.follow",
                    isEnabled = true,
                    responseType = "pipeline",
                    updatedAt = "2026-06-27T00:00:00Z",
                )
            val eventResponsesApi =
                FakeEventResponsesApi(
                    summaries = listOf(summary),
                    detailResponse = EventResponse(
                        id = "er1",
                        eventType = "channel.follow",
                        isEnabled = true,
                        responseType = "pipeline",
                        pipelineId = "pipe-1",
                    ),
                )
            val controller =
                EventResponsesController(
                    channelsApi = FakeChannelsApi(),
                    eventResponsesApi = eventResponsesApi,
                    pipelinesApi = pipelinesApi,
                    pickListsApi = FakePickListsApi(),
                    widgetsApi = FakeWidgetsApi(),
                )
            runBlocking { controller.load() }

            setContent {
                withLifecycle {
                    NomNomzTheme {
                        bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                            EventResponsesScreen(
                                controller = controller,
                                role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                                templateHelpersApi = FakeTemplateHelpersApi(),
                            )
                        }
                    }
                }
            }
            waitForIdle()

            onNodeWithContentDescription("Edit New Follow").performClick()
            waitForIdle()

            onNodeWithContentDescription("Test pipeline").performClick()
            waitForIdle()
            onNodeWithText("Run test").performClick()
            waitForIdle()

            assertEquals("pipe-1", pipelinesApi.lastTestRunPipelineId)
        }

    @Test
    fun test_action_is_disabled_when_no_pipeline_is_bound_yet() = runComposeUiTest {
        val summary =
            EventResponseSummary(
                id = "er1",
                eventType = "channel.follow",
                isEnabled = true,
                responseType = "pipeline",
                updatedAt = "2026-06-27T00:00:00Z",
            )
        val eventResponsesApi =
            FakeEventResponsesApi(
                summaries = listOf(summary),
                detailResponse = EventResponse(
                    id = "er1",
                    eventType = "channel.follow",
                    isEnabled = true,
                    responseType = "pipeline",
                    pipelineId = null,
                ),
            )
        val controller =
            EventResponsesController(
                channelsApi = FakeChannelsApi(),
                eventResponsesApi = eventResponsesApi,
                pipelinesApi = RecordingPipelinesApi(),
                pickListsApi = FakePickListsApi(),
                widgetsApi = FakeWidgetsApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        EventResponsesScreen(
                            controller = controller,
                            role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Edit New Follow").performClick()
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
    override suspend fun helpers(context: TemplateHelperContext): ApiResult<List<TemplateHelperDto>> =
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

// Records the exact eventType/body the screen sends; `get` always answers with [detailResponse] so the edit
// dialog pre-fills the exact response type + bound pipeline id the test wants to exercise.
private class FakeEventResponsesApi(
    private val summaries: List<EventResponseSummary>,
    private val detailResponse: EventResponse,
) : EventResponsesApi {
    override suspend fun list(channelId: String): ApiResult<List<EventResponseSummary>> = ApiResult.Ok(summaries)
    override suspend fun catalog(channelId: String): ApiResult<List<EventResponsePreset>> = ApiResult.Ok(emptyList())
    override suspend fun get(channelId: String, eventType: String): ApiResult<EventResponse> =
        ApiResult.Ok(detailResponse)
    override suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateEventResponseBody,
    ): ApiResult<EventResponse> = ApiResult.Ok(detailResponse)
    override suspend fun resetToDefault(channelId: String, eventType: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeWidgetsApi : WidgetsApi {
    override suspend fun list(channelId: String): ApiResult<List<WidgetSummary>> = ApiResult.Ok(emptyList())
    override suspend fun setEnabled(channelId: String, widgetId: String, enabled: Boolean): ApiResult<Unit> =
        error("stub")
    override suspend fun delete(channelId: String, widgetId: String): ApiResult<Unit> = error("stub")
    override suspend fun blastRadius(channelId: String, widgetId: String) = error("stub")
    override suspend fun create(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.CreateWidgetBody,
    ): ApiResult<WidgetSummary> = error("stub")
    override suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit> = error("stub")
    override suspend fun updateSettings(
        channelId: String,
        widgetId: String,
        settings: kotlinx.serialization.json.JsonObject,
    ): ApiResult<Unit> = error("stub")
    override suspend fun getSettingsSchema(
        channelId: String,
        widgetId: String,
    ): ApiResult<bot.nomnomz.dashboard.core.network.WidgetSettingsSchemaDto> = error("stub")
    override suspend fun compile(
        channelId: String,
        widgetId: String,
        sourceCode: String,
    ): ApiResult<bot.nomnomz.dashboard.core.network.WidgetVersionDetail> = error("stub")
    override suspend fun listVersions(
        channelId: String,
        widgetId: String,
    ): ApiResult<List<bot.nomnomz.dashboard.core.network.WidgetVersionSummary>> = error("stub")
    override suspend fun getVersion(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<bot.nomnomz.dashboard.core.network.WidgetVersionDetail> = error("stub")
    override suspend fun rollback(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<WidgetSummary> = error("stub")
    override suspend fun getProject(
        channelId: String,
        widgetId: String,
    ): ApiResult<bot.nomnomz.dashboard.core.network.ProjectDto> = error("stub")
    override suspend fun putProject(
        channelId: String,
        widgetId: String,
        project: bot.nomnomz.dashboard.core.network.ProjectDto,
    ): ApiResult<bot.nomnomz.dashboard.core.network.WidgetVersionDetail> = error("stub")
    override suspend fun listTemplates(
        channelId: String,
    ): ApiResult<List<bot.nomnomz.dashboard.core.network.WidgetTemplate>> = error("stub")
    override suspend fun clone(channelId: String, installedWidgetId: String): ApiResult<WidgetSummary> =
        error("stub")
    override suspend fun install(channelId: String, galleryItemId: String): ApiResult<WidgetSummary> = error("stub")
    override suspend fun cloneFromGallery(channelId: String, galleryItemId: String): ApiResult<WidgetSummary> =
        error("stub")
    override suspend fun rotateOverlayToken(channelId: String): ApiResult<String> = error("stub")
    override suspend fun updateFromGallery(channelId: String, widgetId: String): ApiResult<WidgetSummary> =
        error("stub")
    override suspend fun testEvent(channelId: String, eventType: String): ApiResult<String> = error("stub")
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
        ApiResult.Ok(listOf(PipelineSummary(id = "pipe-1", name = "Follow chain")))
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
