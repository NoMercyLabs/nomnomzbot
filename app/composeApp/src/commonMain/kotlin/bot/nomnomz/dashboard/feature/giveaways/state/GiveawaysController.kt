// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.giveaways.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.AddCodesBody
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CodeInput
import bot.nomnomz.dashboard.core.network.CodePool
import bot.nomnomz.dashboard.core.network.CodePoolDetail
import bot.nomnomz.dashboard.core.network.CreateCodePoolBody
import bot.nomnomz.dashboard.core.network.Giveaway
import bot.nomnomz.dashboard.core.network.GiveawayStatus
import bot.nomnomz.dashboard.core.network.GiveawayWinner
import bot.nomnomz.dashboard.core.network.GiveawaysApi
import bot.nomnomz.dashboard.core.network.UpsertGiveawayBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_codepool_codes_added
import nomnomzbot.composeapp.generated.resources.feedback_codepool_deleted
import nomnomzbot.composeapp.generated.resources.feedback_codepool_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_codepool_saved
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_closed
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_deleted
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_drawn
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_opened
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_saved
import org.jetbrains.compose.resources.StringResource

// The Giveaways page's state-holder (giveaways.md §6 — the channel's giveaway campaigns and their code pools).
// Lists the channel's real giveaways from the backend (no fabricated rows) and drives the full lifecycle:
// create / edit / delete, open → close → draw, redraw a winner, and the winner history. The giveaways routes
// resolve the channel from the request, so this controller needs no channel resolve step — it talks straight to
// the giveaways facade. Every write re-lists on success so the screen always reflects the backend's truth.
//
// Four surfaces, four render states the screen projects: [state] (the campaigns), [codePools] (the Broadcaster-
// only secret pools), [winners] (the winner panel opened for one giveaway), and [poolDetail] (the manage-pool
// panel opened for one pool). The winner panel also holds the on-demand plaintext code reveals — the one path a
// code is ever decrypted for the operator — keyed by winner id and dropped when the panel closes.
class GiveawaysController(
    private val giveawaysApi: GiveawaysApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<GiveawaysState> = MutableStateFlow(GiveawaysState.Loading)

    /** The campaign list render state: loading / ready (with the giveaways) / empty / error. */
    val state: StateFlow<GiveawaysState> = _state.asStateFlow()

    private val _codePools: MutableStateFlow<CodePoolsState> = MutableStateFlow(CodePoolsState.Loading)

    /** The code-pool section render state — only ever loaded for a caller who clears `giveaways:codes:write`. */
    val codePools: StateFlow<CodePoolsState> = _codePools.asStateFlow()

    private val _winners: MutableStateFlow<WinnersState> = MutableStateFlow(WinnersState.Hidden)

    /** The winner panel: hidden, or open for one giveaway (with its history + any revealed codes). */
    val winners: StateFlow<WinnersState> = _winners.asStateFlow()

    private val _poolDetail: MutableStateFlow<PoolDetailState> = MutableStateFlow(PoolDetailState.Hidden)

    /** The manage-pool panel: hidden, or open for one pool (with its masked code rows). */
    val poolDetail: StateFlow<PoolDetailState> = _poolDetail.asStateFlow()

    // ── Campaign list ────────────────────────────────────────────────────────────

    /** List the channel's giveaways. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps the current
        // content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is GiveawaysState.Ready) _state.value = GiveawaysState.Loading

        when (val result: ApiResult<List<Giveaway>> = giveawaysApi.list()) {
            is ApiResult.Failure -> _state.value = GiveawaysState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) GiveawaysState.Empty
                    else GiveawaysState.Ready(result.value)
        }
    }

    /** Create a giveaway, then reload so the new row appears. Surfaces the error on failure. */
    suspend fun createGiveaway(body: UpsertGiveawayBody) {
        afterWrite(giveawaysApi.create(body))
    }

    /** Edit a draft/closed giveaway, addressed by its [id]. Reloads on success. Surfaces the error on failure. */
    suspend fun updateGiveaway(id: String, body: UpsertGiveawayBody) {
        afterWrite(giveawaysApi.update(id, body))
    }

    /** Delete a giveaway, addressed by its [id]. Reloads on success. Surfaces the error on failure. */
    suspend fun deleteGiveaway(id: String) {
        afterWrite(giveawaysApi.delete(id), success = Res.string.feedback_giveaway_deleted)
    }

    /** Open a giveaway for entries. Reloads on success. Surfaces the error on failure. */
    suspend fun openGiveaway(id: String) {
        afterWrite(giveawaysApi.open(id), success = Res.string.feedback_giveaway_opened)
    }

    /** Stop accepting entries (the giveaway stays drawable). Reloads on success. Surfaces the error on failure. */
    suspend fun closeGiveaway(id: String) {
        afterWrite(giveawaysApi.close(id), success = Res.string.feedback_giveaway_closed)
    }

    /**
     * Draw the winners for [giveaway], announce the outcome, reload the list (its status flips to drawn), and open
     * the winner panel on the freshly-drawn winners so the operator sees them immediately. Surfaces the error on
     * failure without opening the panel.
     */
    suspend fun drawGiveaway(giveaway: Giveaway) {
        when (val result: ApiResult<List<GiveawayWinner>> = giveawaysApi.draw(giveaway.id)) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_giveaway_drawn)
                load()
                _winners.value =
                    WinnersState.Ready(giveaway.copy(status = GiveawayStatus.Drawn), result.value)
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    // ── Winner panel ─────────────────────────────────────────────────────────────

    /** Open the winner panel for [giveaway] and load its history. */
    suspend fun showWinners(giveaway: Giveaway) {
        _winners.value = WinnersState.Loading(giveaway)
        loadWinnersInto(giveaway)
    }

    /** Close the winner panel, dropping any revealed codes it held. */
    fun hideWinners() {
        _winners.value = WinnersState.Hidden
    }

    /**
     * Replace one winner (forfeit / no-show) with a fresh draw, then reload the panel AND the list. Surfaces the
     * error over the kept panel on failure.
     */
    suspend fun redrawWinner(giveaway: Giveaway, winnerId: String) {
        when (val result: ApiResult<Unit> = giveawaysApi.redraw(giveaway.id, winnerId)) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_giveaway_saved)
                loadWinnersInto(giveaway)
                load()
            }
            is ApiResult.Failure -> winnersActionError(result.error.message)
        }
    }

    /**
     * Reveal [winnerId]'s assigned code — the failed-whisper fallback (Broadcaster-only). On success the plaintext
     * is held in the open winner panel, keyed by winner, so the row shows it with a copy control. Surfaces the
     * error over the kept panel on failure.
     */
    suspend fun revealCode(giveaway: Giveaway, winnerId: String) {
        when (val result: ApiResult<String> = giveawaysApi.revealCode(giveaway.id, winnerId)) {
            is ApiResult.Ok -> {
                val current: WinnersState = _winners.value
                if (current is WinnersState.Ready) {
                    _winners.value =
                        current.copy(revealedCodes = current.revealedCodes + (winnerId to result.value))
                }
            }
            is ApiResult.Failure -> winnersActionError(result.error.message)
        }
    }

    // Re-fetch the winner history into an open panel, preserving the giveaway header. A failure surfaces on the
    // kept panel (Ready.actionError) rather than blanking it, unless the panel was never populated (then Error).
    private suspend fun loadWinnersInto(giveaway: Giveaway) {
        when (val result: ApiResult<List<GiveawayWinner>> = giveawaysApi.winners(giveaway.id)) {
            is ApiResult.Ok -> _winners.value = WinnersState.Ready(giveaway, result.value)
            is ApiResult.Failure -> {
                val current: WinnersState = _winners.value
                _winners.value =
                    if (current is WinnersState.Ready) current.copy(actionError = result.error.message)
                    else WinnersState.Error(giveaway, result.error.message)
            }
        }
    }

    private fun winnersActionError(detail: String) {
        val current: WinnersState = _winners.value
        if (current is WinnersState.Ready) _winners.value = current.copy(actionError = detail)
    }

    // ── Code pools (Broadcaster-only) ─────────────────────────────────────────────

    /** List the channel's code pools. The screen calls this only for a caller who clears `giveaways:codes:write`. */
    suspend fun loadCodePools() {
        if (_codePools.value !is CodePoolsState.Ready) _codePools.value = CodePoolsState.Loading

        when (val result: ApiResult<List<CodePool>> = giveawaysApi.listCodePools()) {
            is ApiResult.Failure -> _codePools.value = CodePoolsState.Error(result.error.message)
            is ApiResult.Ok ->
                _codePools.value =
                    if (result.value.isEmpty()) CodePoolsState.Empty
                    else CodePoolsState.Ready(result.value)
        }
    }

    /** Create a code pool, then reload the pool list. Surfaces the error on failure. */
    suspend fun createCodePool(name: String, description: String?) {
        afterPoolWrite(
            giveawaysApi.createCodePool(CreateCodePoolBody(name.trim(), description.orNullIfBlank())),
        )
    }

    /** Delete a code pool, addressed by its [poolId]. Reloads the pool list. Surfaces the error on failure. */
    suspend fun deleteCodePool(poolId: String) {
        afterPoolWrite(giveawaysApi.deleteCodePool(poolId), success = Res.string.feedback_codepool_deleted)
    }

    /**
     * Add [codes] to the pool [poolId] (each trimmed; blanks dropped) as label-less code inputs, then reload the
     * pool list AND — when the manage-pool panel is open for this pool — its masked code rows. Surfaces the error
     * on failure.
     */
    suspend fun addCodes(poolId: String, codes: List<String>) {
        val inputs: List<CodeInput> =
            codes.mapNotNull { it.trim().takeIf(String::isNotBlank)?.let { code -> CodeInput(code) } }
        when (val result: ApiResult<Unit> = giveawaysApi.addCodes(poolId, AddCodesBody(inputs))) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_codepool_codes_added)
                loadCodePools()
                if (_poolDetail.value.let { it is PoolDetailState.Ready && it.pool.id == poolId }) {
                    loadPoolDetailInto(poolId)
                }
            }
            is ApiResult.Failure -> failPoolWrite(result.error.message)
        }
    }

    // ── Manage-pool panel ─────────────────────────────────────────────────────────

    /** Open the manage-pool panel for [pool] and load its masked code rows. */
    suspend fun showPoolDetail(pool: CodePool) {
        _poolDetail.value = PoolDetailState.Loading(pool.name)
        loadPoolDetailInto(pool.id)
    }

    /** Close the manage-pool panel. */
    fun hidePoolDetail() {
        _poolDetail.value = PoolDetailState.Hidden
    }

    private suspend fun loadPoolDetailInto(poolId: String) {
        val name: String =
            when (val current: PoolDetailState = _poolDetail.value) {
                is PoolDetailState.Loading -> current.name
                is PoolDetailState.Ready -> current.pool.name
                is PoolDetailState.Error -> current.name
                PoolDetailState.Hidden -> ""
            }
        when (val result: ApiResult<CodePoolDetail> = giveawaysApi.codePool(poolId)) {
            is ApiResult.Ok -> _poolDetail.value = PoolDetailState.Ready(result.value)
            is ApiResult.Failure -> _poolDetail.value = PoolDetailState.Error(name, result.error.message)
        }
    }

    // ── Shared write plumbing ──────────────────────────────────────────────────────

    // A campaign write either reloads the list AND announces success on the frame, or surfaces its error over the
    // current Ready list without losing it (failure) — so a failed edit/lifecycle leaves the page intact with a
    // visible reason AND a frame-level error message. [success] lets each action name its own outcome.
    private suspend fun afterWrite(
        result: ApiResult<Unit>,
        success: StringResource = Res.string.feedback_giveaway_saved,
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
        feedback.error(Res.string.feedback_giveaway_save_failed, detail)
        val current: GiveawaysState = _state.value
        _state.value =
            if (current is GiveawaysState.Ready) current.copy(actionError = detail)
            else GiveawaysState.Error(detail)
    }

    private suspend fun afterPoolWrite(
        result: ApiResult<Unit>,
        success: StringResource = Res.string.feedback_codepool_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                loadCodePools()
            }
            is ApiResult.Failure -> failPoolWrite(result.error.message)
        }
    }

    private fun failPoolWrite(detail: String) {
        feedback.error(Res.string.feedback_codepool_save_failed, detail)
        val current: CodePoolsState = _codePools.value
        _codePools.value =
            if (current is CodePoolsState.Ready) current.copy(actionError = detail)
            else CodePoolsState.Error(detail)
    }

    // A blank description is sent as null (omitted from the wire body) — an empty string is not a description.
    private fun String?.orNullIfBlank(): String? = this?.takeIf { it.isNotBlank() }
}

/** The Giveaways campaign-list render state. */
sealed interface GiveawaysState {
    data object Loading : GiveawaysState

    /**
     * The channel's giveaways are listed. [actionError] is non-null only when the last create/edit/lifecycle/
     * delete failed — the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val giveaways: List<Giveaway>, val actionError: String? = null) : GiveawaysState

    data object Empty : GiveawaysState

    data class Error(val detail: String) : GiveawaysState
}

/** The code-pool section render state (only ever driven for a Broadcaster-capable caller). */
sealed interface CodePoolsState {
    data object Loading : CodePoolsState

    data class Ready(val pools: List<CodePool>, val actionError: String? = null) : CodePoolsState

    data object Empty : CodePoolsState

    data class Error(val detail: String) : CodePoolsState
}

/** The winner-panel render state, opened for one giveaway. */
sealed interface WinnersState {
    data object Hidden : WinnersState

    data class Loading(val giveaway: Giveaway) : WinnersState

    /**
     * The [giveaway]'s winner history. [revealedCodes] maps a winner id to its just-revealed plaintext code (the
     * broadcaster reveal, one at a time, held only while the panel is open). [actionError] surfaces a failed
     * redraw / reveal over the kept panel.
     */
    data class Ready(
        val giveaway: Giveaway,
        val winners: List<GiveawayWinner>,
        val revealedCodes: Map<String, String> = emptyMap(),
        val actionError: String? = null,
    ) : WinnersState

    data class Error(val giveaway: Giveaway, val detail: String) : WinnersState
}

/** The manage-pool panel render state, opened for one code pool. */
sealed interface PoolDetailState {
    data object Hidden : PoolDetailState

    data class Loading(val name: String) : PoolDetailState

    data class Ready(val pool: CodePoolDetail, val actionError: String? = null) : PoolDetailState

    data class Error(val name: String, val detail: String) : PoolDetailState
}
