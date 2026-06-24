// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.nav

// The PARTICIPANT rung (Rung 0) of the one-shell, three-rungs IA: the user-first surface a viewer/sub/VIP sees
// per-channel — read-mostly + self-service, never management controls. It is the SAME shell as the management
// rungs (one shell, never forked); only the page set and the controls differ. Where the management sidebar gates
// pages by [ManagementRole], the participant sidebar gates by [ParticipantStanding] (Plane A: a role-less viewer
// still has a community standing). A sub/VIP unlocks MORE within this surface (a sub-only lane, sub leaderboards,
// higher pending limits) — surfaced from `/effective/me`'s CommunityStanding — but the base view is always there.

/** The shell-layer mirror of the network `CommunityStanding` — the Plane-A rung the participant surface unlocks from. */
enum class ParticipantStanding(val level: Int) {
    Everyone(0),
    Subscriber(20),
    Vip(40),
    Artist(60),
    Moderator(100);

    /** True when this standing is a subscriber or above — the gate for sub-only lanes and sub leaderboards. */
    val isSubscriberOrAbove: Boolean
        get() = level >= Subscriber.level

    /** True when this standing is a VIP or above — the gate for VIP-only affordances (higher pending limits). */
    val isVipOrAbove: Boolean
        get() = level >= Vip.level
}

/** A participant sidebar content page (Rung 0). Each is a read-mostly + self-service slice of an existing API. */
enum class ParticipantPage {
    /** Home: the caller's own profile/standing/activity + the channel's public summary. */
    MyChannel,

    /** Live now-playing + queue; submit a song request (`music:request:submit`, every standing). */
    NowPlaying,

    /** The channel's leaderboards (read) + the caller's own opt-in / opt-out toggle. */
    Leaderboards,

    /** The caller's balance, the catalog (read + purchase), community jars, and points transfers. */
    PointsAndStore,

    /** The channel's games: read, play, and the caller's own play history. */
    Games,

    /** The caller's own data: pronouns (read), activity summary, and their participation footprint. */
    Me,
}

/**
 * One participant sidebar entry: its [page] and the minimum [ParticipantStanding] to SEE it. Most participant
 * pages floor at [ParticipantStanding.Everyone] (every signed-in viewer gets the base surface); a page can floor
 * higher to be a standing-unlocked lane. The progressive unlocks WITHIN a page (a sub-only queue lane, sub
 * leaderboards) are decided on the page from the caller's standing, not by hiding the whole page.
 */
data class ParticipantNavPage(val page: ParticipantPage, val floor: ParticipantStanding)

object ParticipantNav {

    /** The participant page inventory, in sidebar order. The whole base surface floors at Everyone. */
    val pages: List<ParticipantNavPage> =
        listOf(
            ParticipantNavPage(ParticipantPage.MyChannel, ParticipantStanding.Everyone),
            ParticipantNavPage(ParticipantPage.NowPlaying, ParticipantStanding.Everyone),
            ParticipantNavPage(ParticipantPage.Leaderboards, ParticipantStanding.Everyone),
            ParticipantNavPage(ParticipantPage.PointsAndStore, ParticipantStanding.Everyone),
            ParticipantNavPage(ParticipantPage.Games, ParticipantStanding.Everyone),
            ParticipantNavPage(ParticipantPage.Me, ParticipantStanding.Everyone),
        )

    /**
     * The participant pages a caller of [standing] may see — those whose floor the standing clears. Every signed-in
     * viewer (Everyone) gets the full base surface; a sub/VIP would additionally see any higher-floored lane page.
     * This is never empty for a signed-in participant — Rung 0 is a real surface, not a dead-end placeholder.
     */
    fun pagesFor(standing: ParticipantStanding): List<ParticipantPage> =
        pages.filter { standing.level >= it.floor.level }.map { it.page }
}
