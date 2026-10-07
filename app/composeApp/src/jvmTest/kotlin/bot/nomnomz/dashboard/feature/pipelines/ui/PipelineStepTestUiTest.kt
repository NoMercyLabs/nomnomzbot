// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesState
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * S-PIPE-TEST-STEP: "Test this step" on a step row runs that step alone and shows its outcome on that row only.
 */
@OptIn(ExperimentalTestApi::class)
class PipelineStepTestUiTest {

    private val steps: List<PipelineStep> =
        listOf(
            PipelineStep(PipelineNode("send_chat", mapOf("message" to "hello chat")), id = "s1", order = 0),
            PipelineStep(PipelineNode("stop"), id = "s2", order = 1),
        )

    private fun openEditor(testRunResult: ApiResult<TestRunResult>): Pair<PipelinesController, RecordingStepTestApi> {
        val detail = PipelineDetail(id = "pipe-1", name = "Greeter", graph = PipelineGraph(steps).toJson())
        val api = RecordingStepTestApi(detail, testRunResult)
        val controller =
            PipelinesController(
                channelsApi = FakeChannelsApiForTreeVisTest(ChannelSummary(id = "chan-1")),
                pipelinesApi = api,
                webhooksApi = FakeWebhooksApiForTreeVisTest(),
                pickListsApi = FakePickListsApiForTreeVisTest(),
            )
        runTest {
            controller.load()
            controller.openEditor(PipelineSummary(id = detail.id, name = detail.name))
        }
        return controller to api
    }

    @Test
    fun testing_the_send_chat_step_shows_its_chat_line_on_that_step_only() {
        val (controller, api) =
            openEditor(ApiResult.Ok(TestRunResult(success = true, chatOutput = listOf("hello chat from the test"), log = listOf("sent one line"))))

        runComposeUiTest {
            setContent {
                EnglishThemedContent {
                    val state: PipelinesState by controller.state.collectAsState()
                    ChainEditor(
                        editing = state as PipelinesState.Editing,
                        manage = ManageDecision.Allowed,
                        controller = controller,
                        scope = rememberCoroutineScope(),
                        templateHelpersApi = FakeTemplateHelpersApiForTreeVisTest(),
                        onOpenCodeScript = {},
                    )
                }
            }
            waitForIdle()
            onAllNodesWithText("Test this step").assertCountEquals(2)
            onAllNodesWithText("It would send in chat").assertCountEquals(0)

            onAllNodesWithText("Test this step")[0].performClick()
            waitForIdle()

            // The result shows once, under the send_chat step; the second step's row shows nothing.
            onNodeWithText("hello chat from the test").assertExists()
            onAllNodesWithText("It would send in chat").assertCountEquals(1)
            onNodeWithText("sent one line").assertExists()
            // Only the first step's action reached the backend, not the whole chain.
            assertEquals(1, api.testRuns.size)
            assertEquals(PipelineNode("send_chat", mapOf("message" to "hello chat")).toJson(), api.testRuns.single().step)
        }
    }

    @Test
    fun an_invalid_step_error_shows_as_a_readable_message_on_the_step() {
        val (controller, _) =
            openEditor(ApiResult.Failure(ApiError(400, "INVALID_STEP", "A step needs an action type.")))

        runComposeUiTest {
            setContent {
                EnglishThemedContent {
                    val state: PipelinesState by controller.state.collectAsState()
                    ChainEditor(
                        editing = state as PipelinesState.Editing,
                        manage = ManageDecision.Allowed,
                        controller = controller,
                        scope = rememberCoroutineScope(),
                        templateHelpersApi = FakeTemplateHelpersApiForTreeVisTest(),
                        onOpenCodeScript = {},
                    )
                }
            }
            waitForIdle()

            onAllNodesWithText("Test this step")[0].performClick()
            waitForIdle()

            onNodeWithText("This step could not be tested: A step needs an action type.").assertExists()
            onAllNodesWithText("This step could not be tested", substring = true).assertCountEquals(1)
        }
    }
}

private class RecordingStepTestApi(
    private val detail: PipelineDetail,
    private val testRunResult: ApiResult<TestRunResult>,
) : PipelinesApi {
    val testRuns: MutableList<PipelineTestRunBody> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        ApiResult.Ok(listOf(PipelineSummary(id = detail.id, name = detail.name)))

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> = ApiResult.Ok(detail)

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = NotImplementedInTest

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        NotImplementedInTest

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        NotImplementedInTest

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = NotImplementedInTest

    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        NotImplementedInTest

    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> {
        testRuns += body
        return testRunResult
    }
}
