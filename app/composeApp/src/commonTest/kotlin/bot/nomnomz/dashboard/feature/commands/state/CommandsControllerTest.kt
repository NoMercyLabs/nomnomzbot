// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CreateCommandBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.UpdateCommandBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_command_deleted
import nomnomzbot.composeapp.generated.resources.feedback_command_saved
import nomnomzbot.composeapp.generated.resources.feedback_command_save_failed

// Proves the Commands page state machine the screen renders: resolve the active channel, then surface the
// channel's real commands — empty when there are none, error if either step fails. The screen is a pure
// projection of this, so testing it proves the page shows real data (no fabricated rows) and degrades cleanly.
class CommandsControllerTest {

    @Test
    fun load_surfaces_the_channel_commands_on_success() = runTest {
        val controller =
            makeController(
                commandsResult = ApiResult.Ok(
                    listOf(
                        CommandSummary(
                            id = "00000007-0000-0000-0000-000000000007",
                            name = "!hello",
                            tier = "template",
                            minPermissionLevel = 0,
                            isEnabled = true,
                            cooldownSeconds = 5,
                            description = "Greets the chat",
                            useCount = 42,
                        )
                    )
                ),
            )

        controller.load()

        val state: CommandsState = controller.state.value
        assertTrue(state is CommandsState.Ready)
        val commands: List<CommandSummary> = (state as CommandsState.Ready).commands
        assertEquals(1, commands.size)
        val command: CommandSummary = commands.first()
        assertEquals("!hello", command.name)
        assertEquals(true, command.isEnabled)
        assertEquals("Greets the chat", command.description)
        assertEquals(42, command.useCount)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_commands() = runTest {
        val controller = makeController(commandsResult = ApiResult.Ok(emptyList()))

        controller.load()

        assertTrue(controller.state.value is CommandsState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            makeController(
                channelResult = ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded")),
                commandsResult = ApiResult.Ok(emptyList()),
            )

        controller.load()

        assertTrue(controller.state.value is CommandsState.Error)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            makeController(commandsResult = ApiResult.Failure(ApiError(500, "ERR", "boom")))

        controller.load()

        assertTrue(controller.state.value is CommandsState.Error)
    }

    @Test
    fun create_posts_the_body_then_reloads_with_the_new_command() = runTest {
        // The fake starts empty; the create appends the new command to its backing store, so the controller's
        // post-write reload must surface it — proving create actually calls the api AND re-lists.
        val commandsApi = RecordingCommandsApi(ApiResult.Ok(emptyList()))
        val controller = makeController(commandsApi = commandsApi)
        controller.load()
        assertTrue(controller.state.value is CommandsState.Empty)

        controller.createCommand(name = "!hi", templateResponse = "yo", pipelineId = null, isEnabled = true)

        // The api recorded exactly the body the controller built.
        assertEquals(1, commandsApi.created.size)
        val body: CreateCommandBody = commandsApi.created.first()
        assertEquals("ch1", commandsApi.createdChannelId)
        assertEquals("!hi", body.name)
        assertEquals("yo", body.templateResponse)
        assertEquals(true, body.isEnabled)

        // And the reload surfaced the freshly-created row.
        val state: CommandsState = controller.state.value
        assertTrue(state is CommandsState.Ready)
        val commands: List<CommandSummary> = (state as CommandsState.Ready).commands
        assertEquals(1, commands.size)
        assertEquals("!hi", commands.first().name)
        assertNull(state.actionError)
    }

    @Test
    fun toggle_puts_only_the_enabled_flag_then_reloads_with_the_flipped_state() = runTest {
        val commandsApi =
            RecordingCommandsApi(
                ApiResult.Ok(
                    listOf(
                        CommandSummary(id = "00000001-0000-0000-0000-000000000001", name = "!hi", isEnabled = true)
                    )
                )
            )
        val controller = makeController(commandsApi = commandsApi)
        controller.load()

        controller.toggleCommand(name = "!hi", enabled = false)

        // A toggle is a partial PUT carrying only isEnabled.
        assertEquals(1, commandsApi.updated.size)
        val update: Pair<String, UpdateCommandBody> = commandsApi.updated.first()
        assertEquals("!hi", update.first)
        assertEquals(false, update.second.isEnabled)
        assertNull(update.second.templateResponse)

        // The reload reflects the persisted flip.
        val state: CommandsState = controller.state.value
        assertTrue(state is CommandsState.Ready)
        assertEquals(false, (state as CommandsState.Ready).commands.first().isEnabled)
    }

    @Test
    fun delete_removes_the_command_then_reloads_to_empty() = runTest {
        val commandsApi =
            RecordingCommandsApi(
                ApiResult.Ok(listOf(CommandSummary(id = "00000001-0000-0000-0000-000000000001", name = "!hi", isEnabled = true)))
            )
        val controller = makeController(commandsApi = commandsApi)
        controller.load()
        assertTrue(controller.state.value is CommandsState.Ready)

        controller.deleteCommand(name = "!hi")

        assertEquals(listOf("!hi"), commandsApi.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is CommandsState.Empty)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val commandsApi =
            RecordingCommandsApi(
                ApiResult.Ok(listOf(CommandSummary(id = "00000001-0000-0000-0000-000000000001", name = "!hi", isEnabled = true))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = makeController(commandsApi = commandsApi)
        controller.load()

        controller.deleteCommand(name = "!hi")

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: CommandsState = controller.state.value
        assertTrue(state is CommandsState.Ready)
        assertEquals(1, (state as CommandsState.Ready).commands.size)
        assertEquals("no permission", state.actionError)
    }

    @Test
    fun a_successful_toggle_announces_save_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val commandsApi =
            RecordingCommandsApi(
                ApiResult.Ok(listOf(CommandSummary(id = "00000001-0000-0000-0000-000000000001", name = "!hi", isEnabled = true)))
            )
        val controller = makeController(commandsApi = commandsApi, feedback = feedback)
        controller.load()

        controller.toggleCommand(name = "!hi", enabled = false)

        // The toggle (a save) announced exactly one success with the "saved" label.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_command_saved, feedback.only.label)
    }

    @Test
    fun a_successful_delete_announces_the_deleted_label() = runTest {
        val feedback = RecordingFeedback()
        val commandsApi =
            RecordingCommandsApi(
                ApiResult.Ok(listOf(CommandSummary(id = "00000001-0000-0000-0000-000000000001", name = "!hi", isEnabled = true)))
            )
        val controller = makeController(commandsApi = commandsApi, feedback = feedback)
        controller.load()

        controller.deleteCommand(name = "!hi")

        // A delete says "deleted", not the generic "saved" — the success message is action-specific.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_command_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_write_announces_an_error_carrying_the_backend_detail() = runTest {
        val feedback = RecordingFeedback()
        val commandsApi =
            RecordingCommandsApi(
                ApiResult.Ok(listOf(CommandSummary(id = "00000001-0000-0000-0000-000000000001", name = "!hi", isEnabled = true))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = makeController(commandsApi = commandsApi, feedback = feedback)
        controller.load()

        controller.deleteCommand(name = "!hi")

        // The failure path emits an ERROR (never a success), carrying the backend message as the detail arg.
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_command_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("no permission"), feedback.only.formatArgs)
    }
}

// ── Fakes ────────────────────────────────────────────────────────────────────────────────────────

/** Factory helper — supplies sensible defaults so each test only overrides what it cares about. */
private fun makeController(
    channelResult: ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1")),
    commandsResult: ApiResult<List<CommandSummary>> = ApiResult.Ok(emptyList()),
    commandsApi: CommandsApi = RecordingCommandsApi(commandsResult),
    feedback: RecordingFeedback = RecordingFeedback(),
): CommandsController =
    CommandsController(
        channelsApi = FakeChannelsApi(channelResult),
        commandsApi = commandsApi,
        builtinsApi = FakeBuiltinsApi(),
        pipelinesApi = FakePipelinesApi(),
        feedback = feedback,
    )

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

private class FakeBuiltinsApi : BuiltinsApi {
    override suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>> =
        ApiResult.Ok(emptyList())

    override suspend fun setEnabled(
        channelId: String,
        builtinKey: String,
        enabled: Boolean,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakePipelinesApi : PipelinesApi {
    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        ApiResult.Ok(emptyList())

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        ApiResult.Failure(ApiError(404, "NOT_FOUND", "not found"))

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = "p1", name = body.name))

    override suspend fun update(
        channelId: String,
        id: String,
        body: UpdatePipelineBody,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful
// write mutates the store so the controller's post-write reload observes the real consequence (a new row, a
// flipped flag, a removed row) — not merely that a call happened. [writeResult] forces every write to fail
// (the store is left untouched) to exercise the error path. A list-level failure is modelled by passing
// a Failure as the initial result.
private class RecordingCommandsApi(
    initial: ApiResult<List<CommandSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : CommandsApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<CommandSummary> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val created: MutableList<CreateCommandBody> = mutableListOf()
    var createdChannelId: String? = null
    val updated: MutableList<Pair<String, UpdateCommandBody>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun create(channelId: String, body: CreateCommandBody): ApiResult<Unit> {
        created += body
        createdChannelId = channelId
        if (writeResult is ApiResult.Ok) {
            store +=
                CommandSummary(
                    id = "cmd-${store.size + 1}",
                    name = body.name,
                    templateResponse = body.templateResponse,
                    isEnabled = body.isEnabled,
                )
        }
        return writeResult
    }

    override suspend fun update(
        channelId: String,
        commandName: String,
        body: UpdateCommandBody,
    ): ApiResult<Unit> {
        updated += commandName to body
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.name == commandName }
            if (index >= 0) {
                val existing: CommandSummary = store[index]
                store[index] =
                    existing.copy(
                        isEnabled = body.isEnabled ?: existing.isEnabled,
                        templateResponse = body.templateResponse ?: existing.templateResponse,
                    )
            }
        }
        return writeResult
    }

    override suspend fun delete(channelId: String, commandName: String): ApiResult<Unit> {
        deleted += commandName
        if (writeResult is ApiResult.Ok) {
            store.removeAll { it.name == commandName }
        }
        return writeResult
    }
}
