// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chatpolls.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatPoll
import bot.nomnomz.dashboard.core.network.ChatPollOption
import bot.nomnomz.dashboard.core.network.ChatPollsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OpenChatPollRequest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the "Start poll" modal's chat-poll path actually follows through — not that a call happened. The fake
// behaves like the backend store (one open poll at a time), so the controller's post-open reload observes the
// real consequence: the new poll becomes the RUNNING poll the card renders (right question + numbered options),
// a second open is refused with the current poll kept intact, closing moves it into history, and an open fired
// before the card's first load still resolves the channel instead of silently doing nothing.
class ChatPollsControllerTest {

    @Test
    fun open_makes_the_new_poll_the_running_poll() = runTest {
        val api = FakeChatPollsApi()
        val controller = ChatPollsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        val opened: Boolean = controller.open("Best game?", listOf("Left 4 Dead", "Portal"), null, true)

        assertTrue(opened)
        val ready: ChatPollsState = controller.state.value
        assertTrue(ready is ChatPollsState.Ready)
        val running: ChatPoll? = (ready as ChatPollsState.Ready).openPoll
        assertNotNull(running)
        assertEquals("Best game?", running.question)
        assertEquals("open", running.status)
        // The typed options became the numbered choices viewers vote with (1., 2.).
        assertEquals(listOf(1 to "Left 4 Dead", 2 to "Portal"), running.options.map { it.index to it.label })
        assertNull(ready.actionError)
    }

    @Test
    fun open_before_load_resolves_the_channel_and_still_opens() = runTest {
        val api = FakeChatPollsApi()
        val controller = ChatPollsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)

        // No load() first — the modal can fire before the card mounts. The controller must resolve the channel.
        val opened: Boolean = controller.open("Map?", listOf("A", "B"), 60, false)

        assertTrue(opened)
        val running: ChatPoll? = (controller.state.value as? ChatPollsState.Ready)?.openPoll
        assertEquals("Map?", running?.question)
        assertEquals("ch1", api.openedOnChannel)
    }

    @Test
    fun open_is_refused_when_a_poll_is_already_open_and_keeps_the_current_one() = runTest {
        val api = FakeChatPollsApi()
        val controller = ChatPollsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.open("First?", listOf("A", "B"), null, true)

        val second: Boolean = controller.open("Second?", listOf("C", "D"), null, true)

        // The store refuses a concurrent poll (409); the failure surfaces and the FIRST poll is still running.
        assertFalse(second)
        val ready: ChatPollsState.Ready? = controller.state.value as? ChatPollsState.Ready
        assertNotNull(ready)
        assertNotNull(ready.actionError)
        assertEquals("First?", ready.openPoll?.question)
    }

    @Test
    fun close_moves_the_running_poll_into_history() = runTest {
        val api = FakeChatPollsApi()
        val controller = ChatPollsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.open("Best game?", listOf("A", "B"), null, true)
        val runningId: String = (controller.state.value as ChatPollsState.Ready).openPoll!!.id

        controller.close(runningId)

        val ready: ChatPollsState.Ready = controller.state.value as ChatPollsState.Ready
        // Nothing is running now, and the closed poll is in history — the close actually followed through.
        assertNull(ready.openPoll)
        val closed: ChatPoll? = ready.history.firstOrNull { it.id == runningId }
        assertNotNull(closed)
        assertEquals("closed", closed.status)
    }
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

// A recording fake that behaves like the backend chat-poll store: at most one poll may be "open" at a time
// (a second open answers 409), list() returns the open poll first then closed history, and close() flips the
// poll to "closed" — so the controller's post-write reload observes the real consequence, not a bare call.
private class FakeChatPollsApi : ChatPollsApi {
    private val store: MutableList<ChatPoll> = mutableListOf()
    private var seq: Int = 0
    var openedOnChannel: String? = null
        private set

    override suspend fun list(channelId: String): ApiResult<List<ChatPoll>> {
        val open: ChatPoll? = store.firstOrNull { it.status == "open" }
        val ordered: List<ChatPoll> = listOfNotNull(open) + store.filter { it.id != open?.id }.reversed()
        return ApiResult.Ok(ordered)
    }

    override suspend fun open(channelId: String, request: OpenChatPollRequest): ApiResult<ChatPoll> {
        if (store.any { it.status == "open" })
            return ApiResult.Failure(ApiError(409, "CONFLICT", "A poll is already open."))
        openedOnChannel = channelId
        seq += 1
        val poll =
            ChatPoll(
                id = "poll-$seq",
                question = request.question,
                options =
                    request.options.mapIndexed { index, label ->
                        ChatPollOption(index = index + 1, label = label, votes = 0)
                    },
                status = "open",
                totalVotes = 0,
                openedAt = "2026-07-20T00:00:00Z",
                closesAt = request.durationSeconds?.let { "2026-07-20T00:10:00Z" },
            )
        store.add(poll)
        return ApiResult.Ok(poll)
    }

    override suspend fun close(channelId: String, pollId: String): ApiResult<ChatPoll> {
        val index: Int = store.indexOfFirst { it.id == pollId }
        if (index < 0) return ApiResult.Failure(ApiError(404, "NOT_FOUND", "No such poll."))
        val closed: ChatPoll = store[index].copy(status = "closed", closedAt = "2026-07-20T00:05:00Z")
        store[index] = closed
        return ApiResult.Ok(closed)
    }
}
