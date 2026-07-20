// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsConfigUpdate
import bot.nomnomz.dashboard.core.network.TtsLexiconEntry
import bot.nomnomz.dashboard.core.network.TtsQueueEntry
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.TtsVoicePage
import bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody
import bot.nomnomz.dashboard.core.network.UserTtsVoice
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the TTS approval-queue state machine the panel renders: resolve the active channel, surface the real
// pending utterances (empty as Empty, a failure as Error), and prove approve/reject follow through — each
// really drops the entry from the queue on the reload, not merely records a call.
class TtsQueueControllerTest {

    @Test
    fun load_surfaces_the_pending_entries() = runTest {
        val controller =
            TtsQueueController(
                FakeQueueChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeQueueTtsApi(
                    ApiResult.Ok(
                        listOf(
                            TtsQueueEntry(id = "q1", requestedByDisplayName = "Viewer One", originalText = "hi"),
                            TtsQueueEntry(id = "q2", requestedByDisplayName = "Viewer Two", originalText = "yo"),
                        )
                    )
                ),
            )

        controller.load()

        val state: TtsQueueState = controller.state.value
        assertTrue(state is TtsQueueState.Ready)
        assertEquals(listOf("q1", "q2"), (state as TtsQueueState.Ready).entries.map { it.id })
    }

    @Test
    fun load_is_empty_when_nothing_is_pending() = runTest {
        val controller =
            TtsQueueController(
                FakeQueueChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeQueueTtsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is TtsQueueState.Empty)
    }

    @Test
    fun approve_calls_the_route_then_reloads_dropping_the_entry() = runTest {
        val ttsApi =
            FakeQueueTtsApi(
                ApiResult.Ok(
                    listOf(
                        TtsQueueEntry(id = "q1", requestedByDisplayName = "Viewer One", originalText = "hi"),
                        TtsQueueEntry(id = "q2", requestedByDisplayName = "Viewer Two", originalText = "yo"),
                    )
                )
            )
        val controller = TtsQueueController(FakeQueueChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.approve("q1")

        // The approve hit the route for exactly that entry.
        assertEquals(listOf("q1"), ttsApi.approved)
        // The reload reflects the consequence — q1 is gone, q2 remains.
        val state: TtsQueueState = controller.state.value
        assertTrue(state is TtsQueueState.Ready)
        assertEquals(listOf("q2"), (state as TtsQueueState.Ready).entries.map { it.id })
    }

    @Test
    fun reject_drops_the_entry_and_lands_on_empty_when_it_was_the_last() = runTest {
        val ttsApi =
            FakeQueueTtsApi(
                ApiResult.Ok(listOf(TtsQueueEntry(id = "q1", originalText = "spam")))
            )
        val controller = TtsQueueController(FakeQueueChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.reject("q1")

        assertEquals(listOf("q1"), ttsApi.rejected)
        // The store is now empty, so the post-reject reload lands on Empty — the entry is really gone.
        assertTrue(controller.state.value is TtsQueueState.Empty)
    }

    @Test
    fun a_failed_action_surfaces_the_error_over_the_kept_list() = runTest {
        val ttsApi =
            FakeQueueTtsApi(
                ApiResult.Ok(listOf(TtsQueueEntry(id = "q1", originalText = "hi"))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = TtsQueueController(FakeQueueChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.approve("q1")

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: TtsQueueState = controller.state.value
        assertTrue(state is TtsQueueState.Ready)
        assertEquals(1, (state as TtsQueueState.Ready).entries.size)
        assertEquals("no permission", state.actionError)
    }
}

private class FakeQueueChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
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

// A recording fake that behaves like the backend store for the queue: queue() returns the live store, and each
// successful approve/reject removes the entry so the controller's post-write reload observes the real
// consequence (the entry gone), not merely that a call happened. [writeResult] forces every write to fail (the
// store is left untouched) to exercise the error path. The config/voices/test methods are unused by the queue
// controller and stubbed.
private class FakeQueueTtsApi(
    initial: ApiResult<List<TtsQueueEntry>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : TtsApi {
    override suspend fun myVoice(channelId: String): ApiResult<UserTtsVoice?> = ApiResult.Ok(null)

    override suspend fun setMyVoice(channelId: String, voiceId: String): ApiResult<UserTtsVoice> =
        error("stub")

    override suspend fun clearMyVoice(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    private val queueFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<TtsQueueEntry> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val approved: MutableList<String> = mutableListOf()
    val rejected: MutableList<String> = mutableListOf()

    override suspend fun config(channelId: String): ApiResult<TtsConfig> = error("stub")

    override suspend fun lexicon(channelId: String): ApiResult<List<TtsLexiconEntry>> =
        error("stub")

    override suspend fun createLexiconEntry(
        channelId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> = error("stub")

    override suspend fun updateLexiconEntry(
        channelId: String,
        entryId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> = error("stub")

    override suspend fun deleteLexiconEntry(channelId: String, entryId: String): ApiResult<Unit> =
        error("stub")

    override suspend fun updateConfig(channelId: String, update: TtsConfigUpdate): ApiResult<TtsConfig> =
        error("stub")

    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> = error("stub")

    override suspend fun voicesPage(
        channelId: String,
        query: String,
        locale: String,
        gender: String,
        provider: String,
        accent: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<TtsVoicePage> = error("stub")

    override suspend fun setByokKey(
        channelId: String,
        provider: String,
        apiKey: String,
        region: String?,
    ): ApiResult<TtsConfig> = error("stub")

    override suspend fun removeByokKey(channelId: String, provider: String): ApiResult<TtsConfig> =
        error("stub")

    override suspend fun testSpeak(channelId: String, request: TtsTestRequest): ApiResult<TtsTestResult> =
        error("stub")

    override suspend fun queue(channelId: String): ApiResult<List<TtsQueueEntry>> =
        queueFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun approveQueueEntry(channelId: String, entryId: String): ApiResult<Unit> {
        approved += entryId
        if (writeResult is ApiResult.Ok) store.removeAll { it.id == entryId }
        return writeResult
    }

    override suspend fun rejectQueueEntry(channelId: String, entryId: String): ApiResult<Unit> {
        rejected += entryId
        if (writeResult is ApiResult.Ok) store.removeAll { it.id == entryId }
        return writeResult
    }

    // Not exercised by the queue tests — the per-viewer voice endpoints belong to the config controller.
    override suspend fun userVoice(channelId: String, userId: String): ApiResult<UserTtsVoice?> =
        ApiResult.Ok(null)

    override suspend fun setUserVoice(
        channelId: String,
        userId: String,
        voiceId: String,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun clearUserVoice(channelId: String, userId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}
