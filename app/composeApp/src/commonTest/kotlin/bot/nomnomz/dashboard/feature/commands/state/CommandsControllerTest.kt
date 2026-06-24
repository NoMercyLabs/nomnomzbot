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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Commands page state machine the screen renders: resolve the active channel, then surface the
// channel's real commands — empty when there are none, error if either step fails. The screen is a pure
// projection of this, so testing it proves the page shows real data (no fabricated rows) and degrades cleanly.
class CommandsControllerTest {

    @Test
    fun load_surfaces_the_channel_commands_on_success() = runTest {
        val controller =
            CommandsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommandsApi(
                    ApiResult.Ok(
                        listOf(
                            CommandSummary(
                                id = 7,
                                name = "!hello",
                                type = "text",
                                permission = "everyone",
                                isEnabled = true,
                                cooldownSeconds = 5,
                                description = "Greets the chat",
                                usageCount = 42,
                            )
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
        assertEquals(42, command.usageCount)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_commands() = runTest {
        val controller =
            CommandsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommandsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is CommandsState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            CommandsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeCommandsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is CommandsState.Error)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            CommandsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommandsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is CommandsState.Error)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeCommandsApi(private val result: ApiResult<List<CommandSummary>>) : CommandsApi {
    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> = result
}
