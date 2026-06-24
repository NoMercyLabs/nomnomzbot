// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.quotes.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.AddQuoteBody
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.EditQuoteBody
import bot.nomnomz.dashboard.core.network.Quote
import bot.nomnomz.dashboard.core.network.QuotesApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_quote_deleted
import nomnomzbot.composeapp.generated.resources.feedback_quote_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_quote_saved

// Proves the Quotes page state machine the screen renders: surface the channel's real quotes — empty when
// there are none, error if the list call fails — and follow through on every write (create / edit / delete)
// by re-listing so the consequence is observed (a new row, edited text, a removed row), not merely that a
// call happened. The screen is a pure projection of this, so testing it proves the page shows real data
// (no fabricated rows) and degrades cleanly.
class QuotesControllerTest {

    @Test
    fun load_surfaces_the_channel_quotes_on_success() = runTest {
        val controller =
            QuotesController(
                RecordingQuotesApi(
                    ApiResult.Ok(
                        listOf(
                            Quote(
                                id = "q1",
                                number = 3,
                                text = "Kappa is forever",
                                quotedDisplayName = "Stoney_Eagle",
                                contextGame = "Just Chatting",
                                quotedAt = "2026-06-01T12:00:00Z",
                                createdAt = "2026-06-01T12:00:00Z",
                            )
                        )
                    )
                )
            )

        controller.load()

        val state: QuotesState = controller.state.value
        assertTrue(state is QuotesState.Ready)
        val quotes: List<Quote> = (state as QuotesState.Ready).quotes
        assertEquals(1, quotes.size)
        val quote: Quote = quotes.first()
        assertEquals(3, quote.number)
        assertEquals("Kappa is forever", quote.text)
        assertEquals("Stoney_Eagle", quote.quotedDisplayName)
        assertEquals("Just Chatting", quote.contextGame)
        assertNull(state.actionError)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_quotes() = runTest {
        val controller = QuotesController(RecordingQuotesApi(ApiResult.Ok(emptyList())))

        controller.load()

        assertTrue(controller.state.value is QuotesState.Empty)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            QuotesController(RecordingQuotesApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))))

        controller.load()

        val state: QuotesState = controller.state.value
        assertTrue(state is QuotesState.Error)
        assertEquals("boom", (state as QuotesState.Error).detail)
    }

    @Test
    fun create_posts_the_body_then_reloads_with_the_new_quote() = runTest {
        // The fake starts empty; the create appends the new quote to its backing store, so the controller's
        // post-write reload must surface it — proving create actually calls the api AND re-lists.
        val quotesApi = RecordingQuotesApi(ApiResult.Ok(emptyList()))
        val controller = QuotesController(quotesApi)
        controller.load()
        assertTrue(controller.state.value is QuotesState.Empty)

        controller.createQuote(text = "GG WP", quotedDisplayName = "Bob", contextGame = "Chess")

        // The api recorded exactly the body the controller built — blank attribution would have become null,
        // but here both are present.
        assertEquals(1, quotesApi.created.size)
        val body: AddQuoteBody = quotesApi.created.first()
        assertEquals("GG WP", body.text)
        assertEquals("Bob", body.quotedDisplayName)
        assertEquals("Chess", body.contextGame)

        // And the reload surfaced the freshly-created row with its server-assigned number.
        val state: QuotesState = controller.state.value
        assertTrue(state is QuotesState.Ready)
        val quotes: List<Quote> = (state as QuotesState.Ready).quotes
        assertEquals(1, quotes.size)
        assertEquals("GG WP", quotes.first().text)
        assertNull(state.actionError)
    }

    @Test
    fun create_sends_blank_attribution_as_null() = runTest {
        // Blank optional fields are meaningless attribution: they must go over the wire as null (omitted), not
        // as empty strings.
        val quotesApi = RecordingQuotesApi(ApiResult.Ok(emptyList()))
        val controller = QuotesController(quotesApi)
        controller.load()

        controller.createQuote(text = "No source", quotedDisplayName = "   ", contextGame = "")

        val body: AddQuoteBody = quotesApi.created.first()
        assertEquals("No source", body.text)
        assertNull(body.quotedDisplayName)
        assertNull(body.contextGame)
    }

    @Test
    fun edit_puts_the_new_text_by_number_then_reloads_with_the_change() = runTest {
        val quotesApi =
            RecordingQuotesApi(
                ApiResult.Ok(
                    listOf(Quote(id = "q1", number = 5, text = "old text", quotedDisplayName = "Al"))
                )
            )
        val controller = QuotesController(quotesApi)
        controller.load()

        controller.updateQuote(number = 5, text = "new text", quotedDisplayName = "Al", contextGame = "Doom")

        // The edit is a PUT addressed by the immutable number, carrying the new text/attribution.
        assertEquals(1, quotesApi.updated.size)
        val update: Pair<Int, EditQuoteBody> = quotesApi.updated.first()
        assertEquals(5, update.first)
        assertEquals("new text", update.second.text)
        assertEquals("Doom", update.second.contextGame)

        // The reload reflects the persisted edit.
        val state: QuotesState = controller.state.value
        assertTrue(state is QuotesState.Ready)
        val quote: Quote = (state as QuotesState.Ready).quotes.first()
        assertEquals("new text", quote.text)
        assertEquals("Doom", quote.contextGame)
    }

    @Test
    fun delete_removes_the_quote_then_reloads_to_empty() = runTest {
        val quotesApi =
            RecordingQuotesApi(ApiResult.Ok(listOf(Quote(id = "q1", number = 9, text = "bye"))))
        val controller = QuotesController(quotesApi)
        controller.load()
        assertTrue(controller.state.value is QuotesState.Ready)

        controller.deleteQuote(number = 9)

        assertEquals(listOf(9), quotesApi.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is QuotesState.Empty)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val quotesApi =
            RecordingQuotesApi(
                ApiResult.Ok(listOf(Quote(id = "q1", number = 1, text = "keep me"))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = QuotesController(quotesApi)
        controller.load()

        controller.deleteQuote(number = 1)

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: QuotesState = controller.state.value
        assertTrue(state is QuotesState.Ready)
        assertEquals(1, (state as QuotesState.Ready).quotes.size)
        assertEquals("keep me", state.quotes.first().text)
        assertEquals("no permission", state.actionError)
    }

    @Test
    fun a_successful_edit_announces_save_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            QuotesController(
                RecordingQuotesApi(ApiResult.Ok(listOf(Quote(id = "q1", number = 2, text = "hi")))),
                feedback,
            )
        controller.load()

        controller.updateQuote(number = 2, text = "hello", quotedDisplayName = null, contextGame = null)

        // The edit (a save) announced exactly one success with the "saved" label.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_quote_saved, feedback.only.label)
    }

    @Test
    fun a_successful_delete_announces_the_deleted_label() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            QuotesController(
                RecordingQuotesApi(ApiResult.Ok(listOf(Quote(id = "q1", number = 4, text = "del me")))),
                feedback,
            )
        controller.load()

        controller.deleteQuote(number = 4)

        // A delete says "deleted", not the generic "saved" — the success message is action-specific.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_quote_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_write_announces_an_error_carrying_the_backend_detail() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            QuotesController(
                RecordingQuotesApi(
                    ApiResult.Ok(listOf(Quote(id = "q1", number = 7, text = "boom"))),
                    writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
                ),
                feedback,
            )
        controller.load()

        controller.deleteQuote(number = 7)

        // The failure path emits an ERROR (never a success), carrying the backend message as the detail arg.
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_quote_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("no permission"), feedback.only.formatArgs)
    }
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful
// write mutates the store so the controller's post-write reload observes the real consequence (a new row,
// edited text, a removed row) — not merely that a call happened. [writeResult] forces every write to fail
// (the store is left untouched) to exercise the error path. A list-level failure is modelled by passing a
// Failure as the initial result.
private class RecordingQuotesApi(
    initial: ApiResult<List<Quote>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : QuotesApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<Quote> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val created: MutableList<AddQuoteBody> = mutableListOf()
    val updated: MutableList<Pair<Int, EditQuoteBody>> = mutableListOf()
    val deleted: MutableList<Int> = mutableListOf()

    override suspend fun list(): ApiResult<List<Quote>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun create(body: AddQuoteBody): ApiResult<Unit> {
        created += body
        if (writeResult is ApiResult.Ok) {
            val nextNumber: Int = (store.maxOfOrNull { it.number } ?: 0) + 1
            store +=
                Quote(
                    id = "q$nextNumber",
                    number = nextNumber,
                    text = body.text,
                    quotedDisplayName = body.quotedDisplayName,
                    contextGame = body.contextGame,
                )
        }
        return writeResult
    }

    override suspend fun update(number: Int, body: EditQuoteBody): ApiResult<Unit> {
        updated += number to body
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.number == number }
            if (index >= 0) {
                store[index] =
                    store[index].copy(
                        text = body.text,
                        quotedDisplayName = body.quotedDisplayName,
                        contextGame = body.contextGame,
                    )
            }
        }
        return writeResult
    }

    override suspend fun delete(number: Int): ApiResult<Unit> {
        deleted += number
        if (writeResult is ApiResult.Ok) {
            store.removeAll { it.number == number }
        }
        return writeResult
    }
}
