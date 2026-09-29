// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlinx.datetime.Instant
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// The saved route is the signed-in account's. Over a real SessionStore: the operator's route is read and written
// as usual; while acting as someone the shell opens on the target's default page, never writes a single route into
// the operator's history, and ignores Back/Forward moves through it; after the act-as session ends the operator's
// route is live again.
class ShellRouteMemoryTest {

    /** The saved route custody (web: the address-bar hash), recording every write. */
    private class SavedRoute(var current: ShellRoute) {
        val writes: MutableList<ShellRoute> = mutableListOf()

        fun write(route: ShellRoute) {
            writes += route
            current = route
        }
    }

    private fun memory(store: SessionStore, saved: SavedRoute): ShellRouteMemory =
        ShellRouteMemory(readSaved = { saved.current }, writeSaved = saved::write, sessionStore = store)

    private fun SessionStore.actAsTarget() {
        beginImpersonation("target-jwt", "anda_six", Instant.parse("2030-01-01T00:00:00Z"), "grant-1")
    }

    @Test
    fun the_operator_opens_on_and_saves_their_own_route() {
        val store = SessionStore(NoVault, NoProfile, NoChannel)
        val saved = SavedRoute(ShellRoute.Admin)
        val routes: ShellRouteMemory = memory(store, saved)

        assertEquals(ShellRoute.Admin, routes.openingRoute())
        routes.save(ShellRoute.Commands)
        assertEquals(listOf(ShellRoute.Commands), saved.writes)
        assertEquals(ShellRoute.Timers, routes.externalMove(ShellRoute.Timers))
    }

    @Test
    fun while_acting_the_operators_route_is_neither_read_nor_written_nor_followed() {
        val store = SessionStore(NoVault, NoProfile, NoChannel)
        val saved = SavedRoute(ShellRoute.Admin)
        val routes: ShellRouteMemory = memory(store, saved)
        store.actAsTarget()

        // The target opens on their own default page, not the operator's admin console.
        assertEquals(ShellRoute.Dashboard, routes.openingRoute())
        // Every page the target visits stays out of the operator's saved route.
        routes.save(ShellRoute.Commands)
        routes.save(ShellRoute.Timers)
        assertEquals(emptyList(), saved.writes)
        assertEquals(ShellRoute.Admin, saved.current)
        // Back/Forward through the operator's history does not steer the target's session.
        assertNull(routes.externalMove(ShellRoute.Admin))
    }

    @Test
    fun after_the_act_as_session_ends_the_operators_route_is_live_again() {
        val store = SessionStore(NoVault, NoProfile, NoChannel)
        val saved = SavedRoute(ShellRoute.Admin)
        val routes: ShellRouteMemory = memory(store, saved)
        store.actAsTarget()
        routes.save(ShellRoute.Commands)

        store.endImpersonation()

        assertEquals(ShellRoute.Admin, routes.openingRoute())
        routes.save(ShellRoute.Dashboard)
        assertEquals(listOf(ShellRoute.Dashboard), saved.writes)
    }

    private object NoVault : SessionTokenStore {
        override suspend fun read(profileId: String): SessionTokens? = null
        override suspend fun write(profileId: String, tokens: SessionTokens) = Unit
        override suspend fun clear(profileId: String) = Unit
    }

    private object NoProfile : ActiveProfileStore {
        override suspend fun read(): ConnectionProfile? = null
        override suspend fun write(profile: ConnectionProfile) = Unit
        override suspend fun clear() = Unit
    }

    private object NoChannel : ActiveChannelStore {
        override suspend fun read(): String? = null
        override suspend fun write(channelId: String) = Unit
        override suspend fun clear() = Unit
    }
}
