// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.state

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the channel-scoped Spotify BYOC gate that IntegrationsScreen renders through (S-BYOC-spotify-b,
// done-when #3): the card is ABSENT below `integration:read`, and its write actions (save/clear) are
// present-but-disabled below `integration:write` even though the card itself is visible (the caller holds
// read but not write). If these gates were crossed or collapsed into one key, a read-only caller would either
// lose the whole card or silently gain the write actions.
class SpotifyChannelCredentialsAccessTest {

    @Test
    fun read_key_alone_shows_the_card_but_not_the_write_actions() {
        val held: Set<String> = setOf("integration:read")

        assertTrue(SpotifyChannelCredentialsAccess.canRead(held), "integration:read must show the card")
        assertFalse(
            SpotifyChannelCredentialsAccess.canWrite(held),
            "the write actions stay disabled without integration:write",
        )
    }

    @Test
    fun write_key_alone_does_not_imply_read() {
        // The gates are independent: holding only the write key does not grant the read floor. (In practice the
        // backend always folds read into a broadcaster's held set, but the client must not assume it.)
        val held: Set<String> = setOf("integration:write")

        assertTrue(SpotifyChannelCredentialsAccess.canWrite(held))
        assertFalse(SpotifyChannelCredentialsAccess.canRead(held))
    }

    @Test
    fun both_keys_enable_everything_and_an_empty_set_hides_the_card_entirely() {
        val both: Set<String> = setOf("integration:read", "integration:write")
        assertTrue(SpotifyChannelCredentialsAccess.canRead(both))
        assertTrue(SpotifyChannelCredentialsAccess.canWrite(both))

        assertFalse(SpotifyChannelCredentialsAccess.canRead(emptySet()))
        assertFalse(SpotifyChannelCredentialsAccess.canWrite(emptySet()))
    }
}
