// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.navigation

import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// The browser address is `#/c/<login>/<page>[/<tab>]`: which channel, then which page and tab. These prove the
// parse and the format agree for every route, that an address with no channel still opens its page, and that a
// login in the address resolves to the channel the shell must switch to.
class ShellAddressTest {

    @Test
    fun every_route_round_trips_with_and_without_a_channel() {
        ShellRoute.entries.forEach { route ->
            val withChannel = ShellAddress(login = "stoney_eagle", route = route)
            assertEquals(withChannel, ShellAddress.parse(withChannel.format()), "with channel: $route")
            val bare = ShellAddress(login = null, route = route)
            assertEquals(bare, ShellAddress.parse(bare.format()), "without channel: $route")
        }
    }

    @Test
    fun the_format_puts_the_channel_before_the_page_and_tab() {
        assertEquals(
            "c/other/people/queue",
            ShellAddress(login = "other", route = ShellRoute.PeopleQueue).format(),
        )
        assertEquals("commands", ShellAddress(login = null, route = ShellRoute.Commands).format())
    }

    @Test
    fun a_channel_address_names_the_login_and_the_route() {
        val address: ShellAddress = ShellAddress.parse("c/other/commands")

        assertEquals("other", address.login)
        assertEquals(ShellRoute.Commands, address.route)
    }

    @Test
    fun the_login_is_lower_cased() {
        assertEquals("stoney_eagle", ShellAddress.parse("c/Stoney_Eagle/home").login)
    }

    @Test
    fun a_page_address_without_a_channel_has_no_login() {
        val address: ShellAddress = ShellAddress.parse("music/song-requests")

        assertNull(address.login)
        assertEquals(ShellRoute.MusicSongRequests, address.route)
    }

    @Test
    fun blank_unknown_and_pre_layout_addresses_open_home() {
        assertEquals(ShellAddress(null, ShellRoute.Home), ShellAddress.parse(""))
        assertEquals(ShellAddress(null, ShellRoute.Home), ShellAddress.parse("songrequests"))
        assertEquals(ShellAddress("other", ShellRoute.Home), ShellAddress.parse("c/other/songrequests"))
        assertEquals(ShellAddress(null, ShellRoute.Home), ShellAddress.parse("c"))
        assertEquals(ShellAddress(null, ShellRoute.Home), ShellAddress.parse("c/"))
    }

    @Test
    fun a_login_in_the_address_resolves_to_that_channels_id() {
        val roster: List<ChannelSummary> =
            listOf(ChannelSummary(id = "1", login = "mine"), ChannelSummary(id = "2", login = "Other"))

        assertEquals("2", channelIdForLogin(roster, "other"))
        assertEquals("1", channelIdForLogin(roster, "MINE"))
        assertNull(channelIdForLogin(roster, "stranger"))
        assertNull(channelIdForLogin(roster, ""))
    }
}
