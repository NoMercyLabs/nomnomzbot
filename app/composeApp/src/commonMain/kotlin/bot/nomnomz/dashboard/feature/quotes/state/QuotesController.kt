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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.AddQuoteBody
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.EditQuoteBody
import bot.nomnomz.dashboard.core.network.Quote
import bot.nomnomz.dashboard.core.network.QuotesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_quote_deleted
import nomnomzbot.composeapp.generated.resources.feedback_quote_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_quote_saved

// The Quotes page's state-holder (frontend-ia.md §3 — the Chat group). Lists the channel's real numbered
// quotes from the backend (no fabricated rows). The quotes routes resolve the channel from the JWT, so this
// controller needs no channel resolve step — it talks straight to the quotes facade. It drives the page's
// writes — create / edit / delete — each of which re-lists on success so the screen always reflects the
// backend's truth. The screen renders [state]; a retry / reconnect calls [load] again.
class QuotesController(
    private val quotesApi: QuotesApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<QuotesState> = MutableStateFlow(QuotesState.Loading)

    /** The page render state: loading / ready (with the quotes) / empty / error. */
    val state: StateFlow<QuotesState> = _state.asStateFlow()

    /** List the channel's quotes. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is QuotesState.Ready) _state.value = QuotesState.Loading

        when (val result: ApiResult<List<Quote>> = quotesApi.list()) {
            is ApiResult.Failure -> _state.value = QuotesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) QuotesState.Empty
                    else QuotesState.Ready(result.value)
        }
    }

    /** Create a quote, then reload so the new row appears. Surfaces the error on failure. */
    suspend fun createQuote(text: String, quotedDisplayName: String?, contextGame: String?) {
        afterWrite(quotesApi.create(AddQuoteBody(text, quotedDisplayName.orNullIfBlank(), contextGame.orNullIfBlank())))
    }

    /**
     * Edit a quote's text/attribution, addressed by its immutable [number]. Reloads on success. Surfaces the
     * error on failure.
     */
    suspend fun updateQuote(number: Int, text: String, quotedDisplayName: String?, contextGame: String?) {
        afterWrite(
            quotesApi.update(
                number,
                EditQuoteBody(text, quotedDisplayName.orNullIfBlank(), contextGame.orNullIfBlank()),
            )
        )
    }

    /** Delete a quote, addressed by its [number]. Reloads on success. Surfaces the error on failure. */
    suspend fun deleteQuote(number: Int) {
        afterWrite(quotesApi.delete(number), success = Res.string.feedback_quote_deleted)
    }

    // A write either reloads the list AND announces success on the frame, or surfaces its error over the
    // current Ready list without losing it (failure) — so a failed edit/delete leaves the page intact with a
    // visible reason AND a frame-level error message. [success] lets a delete say "Deleted" while the rest
    // default to "Saved".
    private suspend fun afterWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_quote_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                load()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        // Announce the failure on the frame (persistent until dismissed) AND keep the in-page banner.
        feedback.error(Res.string.feedback_quote_save_failed, detail)
        val current: QuotesState = _state.value
        _state.value =
            if (current is QuotesState.Ready) current.copy(actionError = detail)
            else QuotesState.Error(detail)
    }

    // Optional attribution fields are sent as null (omitted from the wire body) when the operator leaves them
    // blank — an empty string is not a meaningful "who said it".
    private fun String?.orNullIfBlank(): String? = this?.takeIf { it.isNotBlank() }
}

/** The Quotes page render state. */
sealed interface QuotesState {
    data object Loading : QuotesState

    /**
     * The channel's quotes are listed. [actionError] is non-null only when the last create/edit/delete failed —
     * the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val quotes: List<Quote>, val actionError: String? = null) : QuotesState

    data object Empty : QuotesState

    data class Error(val detail: String) : QuotesState
}
