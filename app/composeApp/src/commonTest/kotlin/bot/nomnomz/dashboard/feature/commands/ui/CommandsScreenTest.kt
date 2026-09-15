// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasAnyChild
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CreateCommandBody
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
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
import bot.nomnomz.dashboard.core.network.UpdateCommandBody
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.feature.commands.state.CommandsController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// S047-remaining — proves the Test action next to a command's bound pipeline actually works: with a pipeline
// bound, clicking it opens the shared dry-run dialog and calls the REAL test-run endpoint for the EXACT bound
// pipeline id (asserted on the fake pipelines API, not a "didn't crash" check); with no pipeline bound, the
// button renders DISABLED — never hidden, per the house "disable, don't hide" rule.
@OptIn(ExperimentalTestApi::class)
class CommandsScreenTest {

    @Test
    fun test_action_calls_the_dry_run_endpoint_for_the_bound_pipeline_when_editing_a_bound_command() = runComposeUiTest {
        val pipelinesApi = RecordingPipelinesApi()
        val bound =
            CommandSummary(id = "c1", name = "shoutout", tier = "pipeline", pipelineId = "pipe-1", isEnabled = true)
        val controller =
            CommandsController(
                channelsApi = FakeChannelsApi(),
                commandsApi = FakeCommandsApi(listOf(bound)),
                builtinsApi = FakeBuiltinsApi(),
                pipelinesApi = pipelinesApi,
                pickListsApi = FakePickListsApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        CommandsScreen(
                            controller = controller,
                            role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        // Open the edit dialog for the already pipeline-bound command.
        onNodeWithContentDescription("Edit shoutout").performClick()
        waitForIdle()

        // The Test action is enabled (a pipeline IS bound) — click it, then run the dry-run. GlyphButton carries
        // its label as the accessible content description (a Tooltip on hover), not visible text.
        onNodeWithContentDescription("Test pipeline").performClick()
        waitForIdle()
        onNodeWithText("Run test").performClick()
        waitForIdle()

        assertEquals("pipe-1", pipelinesApi.lastTestRunPipelineId)
    }

    @Test
    fun test_action_is_disabled_when_no_pipeline_is_bound_yet() = runComposeUiTest {
        val bound = CommandSummary(id = "c1", name = "shoutout", tier = "pipeline", pipelineId = null, isEnabled = true)
        val controller =
            CommandsController(
                channelsApi = FakeChannelsApi(),
                commandsApi = FakeCommandsApi(listOf(bound)),
                builtinsApi = FakeBuiltinsApi(),
                pipelinesApi = RecordingPipelinesApi(),
                pickListsApi = FakePickListsApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        CommandsScreen(
                            controller = controller,
                            role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Edit shoutout").performClick()
        waitForIdle()

        // Never hidden — the Test action always renders under its fixed content description; only its enabled
        // state changes. GlyphButton's own semantics node only carries the content description (via
        // clearAndSetSemantics); the shared ManageGate wraps it in a PARENT node carrying [Disabled] +
        // stateDescription — the same shape every other disabled write control in this app has, so assert there.
        onNode(hasAnyChild(hasContentDescription("Test pipeline")), useUnmergedTree = true).assertIsNotEnabled()
    }

    // Owner punch list §7 — the streamer bug report: a custom command silently shadows a built-in with no
    // warning anywhere. These prove the fix fires for a REAL name collision (case-insensitively, prefix
    // stripped) and does NOT fire for an ordinary command — not a surface/smoke check.

    @Test
    fun list_view_flags_a_custom_command_whose_name_collides_with_a_builtin() = runComposeUiTest {
        val shadowing = CommandSummary(id = "c1", name = "quote", tier = "template", isEnabled = true)
        val ordinary = CommandSummary(id = "c2", name = "hello", tier = "template", isEnabled = true)
        val controller =
            CommandsController(
                channelsApi = FakeChannelsApi(),
                commandsApi = FakeCommandsApi(listOf(shadowing, ordinary)),
                builtinsApi = FakeBuiltinsApi(listOf(BuiltinCommand(builtinKey = "quote", name = "!quote"))),
                pipelinesApi = RecordingPipelinesApi(),
                pickListsApi = FakePickListsApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        CommandsScreen(
                            controller = controller,
                            role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        // The shadowing row carries the badge naming what it shadows, exactly once (not once per row)...
        onAllNodes(hasText("Shadows built-in !quote")).assertCountEquals(1)
        // ...and the ordinary "hello" command never renders any shadow badge at all.
        onNodeWithText("Shadows built-in !hello").assertDoesNotExist()
    }

    @Test
    fun editor_warns_live_while_typing_a_name_that_collides_with_a_builtin_and_blocks_submit_until_acknowledged() =
        runComposeUiTest {
            val controller =
                CommandsController(
                    channelsApi = FakeChannelsApi(),
                    commandsApi = FakeCommandsApi(emptyList()),
                    builtinsApi = FakeBuiltinsApi(listOf(BuiltinCommand(builtinKey = "quote", name = "!quote"))),
                    pipelinesApi = RecordingPipelinesApi(),
                    pickListsApi = FakePickListsApi(),
                )
            runBlocking { controller.load() }

            setContent {
                withLifecycle {
                    NomNomzTheme {
                        bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                            CommandsScreen(
                                controller = controller,
                                role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                                templateHelpersApi = FakeTemplateHelpersApi(),
                            )
                        }
                    }
                }
            }
            waitForIdle()

            onNodeWithText("New command").performClick()
            waitForIdle()

            // A fresh create dialog already pre-fills a default response template (no empty reaction), so
            // typing the colliding name is the only thing needed to isolate the shadow gate below. Index [1]:
            // index [0] is the page's own search field, which stays mounted behind the dialog.
            onAllNodes(hasSetTextAction())[1].performTextInput("quote")
            waitForIdle()

            // The warning is visible before any save attempt (never a silent shadow), and Create is blocked.
            onNodeWithText("This name matches a built-in command").assertExists()
            onNodeWithText("Create").assertIsNotEnabled()

            // Acknowledging it unblocks the save — a deliberate override is allowed, just never silent.
            onNodeWithContentDescription("I understand — save it anyway").performClick()
            waitForIdle()
            onNodeWithText("Create").assertIsEnabled()
        }

    @Test
    fun editor_shows_no_shadow_warning_for_a_name_that_does_not_collide_with_any_builtin() = runComposeUiTest {
        val controller =
            CommandsController(
                channelsApi = FakeChannelsApi(),
                commandsApi = FakeCommandsApi(emptyList()),
                builtinsApi = FakeBuiltinsApi(listOf(BuiltinCommand(builtinKey = "quote", name = "!quote"))),
                pipelinesApi = RecordingPipelinesApi(),
                pickListsApi = FakePickListsApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        CommandsScreen(
                            controller = controller,
                            role = bot.nomnomz.dashboard.feature.shell.nav.ManagementRole.Broadcaster,
                            templateHelpersApi = FakeTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithText("New command").performClick()
        waitForIdle()

        // Index [1]: index [0] is the page's own search field, mounted behind the dialog.
        onAllNodes(hasSetTextAction())[1].performTextInput("hello")
        waitForIdle()

        onNodeWithText("This name matches a built-in command").assertDoesNotExist()
        onNodeWithText("Create").assertIsEnabled()
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

private class FakeCommandsApi(private val commands: List<CommandSummary>) : CommandsApi {
    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> = ApiResult.Ok(commands)
    override suspend fun create(channelId: String, body: CreateCommandBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun update(channelId: String, commandName: String, body: UpdateCommandBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, commandName: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeBuiltinsApi(private val builtins: List<BuiltinCommand> = emptyList()) : BuiltinsApi {
    override suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>> = ApiResult.Ok(builtins)
    override suspend fun setEnabled(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun setResponseOverride(
        channelId: String,
        builtinKey: String,
        template: String?,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun setSpeakWithTts(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)
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

// Records the exact pipeline id + channel id + variables the Test action's dry-run call reaches the backend
// with — the proof that clicking Test on a bound pipeline calls the real endpoint for THAT pipeline, not a stub.
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
