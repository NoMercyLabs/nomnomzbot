// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.state

import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.ChannelAppearance
import bot.nomnomz.dashboard.core.network.CurrencyAccount
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.GamePlay
import bot.nomnomz.dashboard.core.network.GamePlayResult
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.PronounOption
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.UserActivity
import bot.nomnomz.dashboard.core.network.UserProfile
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding

// The render states for the six participant screens. Each screen is a pure projection of its state; a write
// re-loads on success (so the screen reflects the backend's truth) and surfaces a transient [actionError] over
// the kept-rendered state on failure (so a failed self-service action never blanks the page).

/** The My-Channel home state: the caller's own identity/activity beside the channel's public summary. */
sealed interface MyChannelState {
    data object Loading : MyChannelState

    data class Ready(
        val profile: UserProfile,
        val activity: UserActivity,
        val channel: DashboardStats,
        val standing: ParticipantStanding,
    ) : MyChannelState

    data class Error(val detail: String) : MyChannelState
}

/** The Now-Playing / Queue state: the live snapshot plus the caller's standing-driven request allowance. */
sealed interface NowPlayingState {
    data object Loading : NowPlayingState

    /**
     * [pendingLimit] is the caller's per-request song cap (higher for sub/VIP). [subscriberLaneUnlocked] flags
     * whether the sub-only queue lane is available to them. [actionError] is non-null only after a failed submit.
     */
    data class Ready(
        val snapshot: MusicSnapshot,
        val pendingLimit: Int,
        val subscriberLaneUnlocked: Boolean,
        val actionError: String? = null,
    ) : NowPlayingState

    data class Error(val detail: String) : NowPlayingState
}

/** The Leaderboards state: the ranking plus the caller's own opt-in state and sub-board unlock. */
sealed interface LeaderboardsState {
    data object Loading : LeaderboardsState

    /**
     * [optedIn] is the caller's current leaderboard visibility (toggled by opt-in/opt-out). [subscriberBoardUnlocked]
     * flags whether the sub-only leaderboard is shown. [actionError] is non-null only after a failed toggle.
     */
    data class Ready(
        val ranking: List<LeaderboardEntry>,
        val subscriberBoardUnlocked: Boolean,
        val optedIn: Boolean = true,
        val actionError: String? = null,
    ) : LeaderboardsState

    data class Error(val detail: String) : LeaderboardsState
}

/** The Points & Store state: the caller's wallet, the catalog, the jars, and whether transfers are unlocked. */
sealed interface StoreState {
    data object Loading : StoreState

    data class Ready(
        val account: CurrencyAccount,
        val catalog: List<CatalogItem>,
        val jars: List<SavingsJar>,
        val canTransfer: Boolean,
        val actionError: String? = null,
    ) : StoreState

    data class Error(val detail: String) : StoreState
}

/** The Games state: the playable games, the caller's own history, and the last settled play outcome. */
sealed interface ParticipantGamesState {
    data object Loading : ParticipantGamesState

    /**
     * [games] are the channel's enabled games. [history] is the caller's own recent plays. [lastOutcome] holds the
     * just-settled play so the screen can show what happened; [actionError] is non-null only after a failed play.
     */
    data class Ready(
        val games: List<GameSummary>,
        val history: List<GamePlay>,
        val lastOutcome: GamePlayResult? = null,
        val actionError: String? = null,
    ) : ParticipantGamesState

    data class Error(val detail: String) : ParticipantGamesState
}

/** The Me state: the caller's own profile (pronouns editable), activity, and participation footprint. */
sealed interface MeState {
    data object Loading : MeState

    data class Ready(
        val profile: UserProfile,
        val activity: UserActivity,
        val channels: List<ChannelAppearance>,
        val standing: ParticipantStanding,
        val pronouns: List<PronounOption> = emptyList(),
        val profileSaving: Boolean = false,
        val profileError: String? = null,
    ) : MeState

    data class Error(val detail: String) : MeState
}
