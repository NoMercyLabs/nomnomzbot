// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Proves the pure heart of mDNS discovery (frontend.md §6): a resolved advertisement → ConnectionProfile.
// This is the contract the jmDNS browse engine maps onto, so the mapping is what makes a discovered row
// click-connect to the RIGHT backend. The fields asserted are load-bearing:
//   - displayName  = the advertised instance name shown in the Connect row,
//   - baseUrl      = scheme://host:port the dashboard actually points at,
//   - id           = the stable `instance` TXT id, so re-announcements DEDUPE to one row (not pile up),
//   - source       = Discovered (vs the typed Manual path).
class LanDiscoveryMappingTest {

    @Test
    fun maps_instance_name_host_port_and_scheme_into_a_discovered_profile() {
        val profile: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "EAGLE",
                host = "192.168.1.42",
                port = 5080,
                txt = mapOf("instance" to "abc-123", "scheme" to "http"),
            )

        assertEquals("EAGLE", profile?.displayName)
        assertEquals("http://192.168.1.42:5080", profile?.baseUrl)
        assertEquals("abc-123", profile?.id)
        assertEquals(ProfileSource.Discovered, profile?.source)
    }

    @Test
    fun honours_the_advertised_https_scheme() {
        val profile: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "EAGLE",
                host = "bot.local",
                port = 443,
                txt = mapOf("instance" to "abc-123", "scheme" to "https"),
            )

        assertEquals("https://bot.local:443", profile?.baseUrl)
    }

    @Test
    fun defaults_scheme_to_http_when_the_txt_omits_it() {
        val profile: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "EAGLE",
                host = "10.0.0.5",
                port = 5080,
                txt = mapOf("instance" to "abc-123"),
            )

        assertEquals("http://10.0.0.5:5080", profile?.baseUrl)
    }

    @Test
    fun derives_the_dedupe_id_from_the_instance_txt_so_re_announcements_collapse() {
        // Same bot, two announcements from two interface addresses: the `instance` id is what makes them
        // ONE row. The ids must be equal even though the host differs.
        val first: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "EAGLE",
                host = "192.168.1.42",
                port = 5080,
                txt = mapOf("instance" to "stable-id"),
            )
        val second: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "EAGLE",
                host = "10.0.0.9",
                port = 5080,
                txt = mapOf("instance" to "stable-id"),
            )

        assertEquals("stable-id", first?.id)
        assertEquals(first?.id, second?.id)
    }

    @Test
    fun falls_back_to_host_port_id_when_the_instance_txt_is_absent() {
        val profile: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "EAGLE",
                host = "192.168.1.42",
                port = 5080,
                txt = emptyMap(),
            )

        assertEquals("192.168.1.42:5080", profile?.id)
    }

    @Test
    fun falls_back_to_host_for_display_name_when_the_instance_name_is_blank() {
        val profile: ConnectionProfile? =
            resolveDiscoveredProfile(
                instanceName = "   ",
                host = "192.168.1.42",
                port = 5080,
                txt = mapOf("instance" to "abc-123"),
            )

        assertEquals("192.168.1.42", profile?.displayName)
    }

    @Test
    fun returns_null_when_there_is_nothing_to_connect_to() {
        assertNull(
            resolveDiscoveredProfile(instanceName = "EAGLE", host = "  ", port = 5080, txt = emptyMap())
        )
        assertNull(
            resolveDiscoveredProfile(instanceName = "EAGLE", host = "host", port = 0, txt = emptyMap())
        )
    }
}
