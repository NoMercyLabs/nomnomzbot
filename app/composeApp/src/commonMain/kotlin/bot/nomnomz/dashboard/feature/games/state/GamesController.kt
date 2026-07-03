// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.games.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.GamePlayEntry
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.GamesApi
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.UpsertGameConfigBody
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Games page's state-holder (economy.md §3.5 — the channel's configured mini-games). Resolves the active
// channel, then loads its real game config from the backend (no fabricated games). It also drives the page's
// writes — toggle enabled / edit config — for the fixed catalog of built-in games (the backend has no create or
// delete route). Every write re-lists on success so the screen always reflects the backend's truth. The screen
// renders [state]; a retry / reconnect calls [load] again.
class GamesController(
    private val channelsApi: ChannelsApi,
    private val gamesApi: GamesApi,
) {
    private val _state: MutableStateFlow<GamesState> = MutableStateFlow(GamesState.Loading)

    /** The page render state: loading / ready (with the games) / empty / error. */
    val state: StateFlow<GamesState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then load its configured games and recent play history. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is GamesState.Ready) _state.value = GamesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = GamesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        coroutineScope {
            val gamesDeferred = async { gamesApi.list(channel.id) }
            val historyDeferred = async { gamesApi.history(channel.id) }

            val gamesResult: ApiResult<List<GameSummary>> = gamesDeferred.await()
            val history: List<GamePlayEntry> =
                when (val r: ApiResult<PaginatedEnvelope<GamePlayEntry>> = historyDeferred.await()) {
                    is ApiResult.Ok -> r.value.data
                    is ApiResult.Failure -> emptyList()
                }

            when (gamesResult) {
                is ApiResult.Failure -> _state.value = GamesState.Error(gamesResult.error.message)
                is ApiResult.Ok ->
                    _state.value =
                        if (gamesResult.value.isEmpty()) GamesState.Empty
                        else GamesState.Ready(games = gamesResult.value, history = history)
            }
        }
    }

    /** Revoke a viewer's 18+ age-consent grant (Broadcaster/Editor manages consent on behalf of the viewer). */
    suspend fun revokeConsent(viewerUserId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(gamesApi.revokeConsent(channel, viewerUserId))
    }

    /**
     * Flip a game's enabled flag. The upsert is a full PUT, so this echoes the rest of [game] back unchanged and
     * sends only [enabled] flipped. Reloads on success; surfaces the error on failure.
     */
    suspend fun toggleGame(game: GameSummary, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(gamesApi.upsert(channel, game.toBody(isEnabled = enabled)))
    }

    /**
     * Edit a game's bet limits, cooldown, and 18+ gate. The upsert is a full PUT, so this carries [game]'s other
     * fields (category, permission, odds, per-stream cap, config) back unchanged and overrides only the edited
     * fields. Reloads on success; surfaces the error on failure.
     */
    suspend fun updateGameConfig(
        game: GameSummary,
        minBet: Long?,
        maxBet: Long?,
        cooldownSeconds: Int,
        requires18Plus: Boolean,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            gamesApi.upsert(
                channel,
                game.toBody(
                    minBet = minBet,
                    maxBet = maxBet,
                    cooldownSeconds = cooldownSeconds,
                    requires18Plus = requires18Plus,
                ),
            )
        )
    }

    // A write either reloads the list (success) or surfaces its error over the current Ready list without losing
    // it (failure) — so a failed toggle/edit leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: GamesState = _state.value
        _state.value =
            if (current is GamesState.Ready) current.copy(actionError = detail)
            else GamesState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

// Build a full upsert body from the current row, overriding only the named fields. Every other field of the PUT
// is echoed back verbatim so the full-replace write never resets an unedited field (category, permission, the
// odds, the per-stream cap, and the opaque tuning [config] map).
private fun GameSummary.toBody(
    isEnabled: Boolean = this.isEnabled,
    requires18Plus: Boolean = this.requires18Plus,
    minBet: Long? = this.minBet,
    maxBet: Long? = this.maxBet,
    cooldownSeconds: Int = this.cooldownSeconds,
): UpsertGameConfigBody =
    UpsertGameConfigBody(
        gameType = gameType,
        category = category,
        isEnabled = isEnabled,
        requires18Plus = requires18Plus,
        minBet = minBet,
        maxBet = maxBet,
        houseEdgePercent = houseEdgePercent,
        winChancePercent = winChancePercent,
        payoutMultiplier = payoutMultiplier,
        cooldownSeconds = cooldownSeconds,
        maxPlaysPerStream = maxPlaysPerStream,
        permission = permission,
        config = config,
    )

/** The Games page render state. */
sealed interface GamesState {
    data object Loading : GamesState

    /**
     * The channel's games are listed. [actionError] is non-null only when the last toggle/edit failed — the
     * screen surfaces it as a transient banner while keeping the list rendered. [history] carries the first page
     * of recent plays (empty when the feature is unused or the caller lacks `economy:games:history:read`).
     */
    data class Ready(
        val games: List<GameSummary>,
        val history: List<GamePlayEntry> = emptyList(),
        val actionError: String? = null,
    ) : GamesState

    data object Empty : GamesState

    data class Error(val detail: String) : GamesState
}
