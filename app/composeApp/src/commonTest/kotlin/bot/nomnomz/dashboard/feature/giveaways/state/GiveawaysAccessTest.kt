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

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the Giveaways page's capability gate (the two floors the GiveawaysScreen renders through). The
// consequence under test: campaign management and code management are gated on DIFFERENT resolved keys and never
// crossed — so a Moderator who holds `giveaways:write` can run campaigns but the secret code-pool tools + the
// winner code reveal stay off until they also hold the Broadcaster-only `giveaways:codes:write`. If the wiring
// regressed (the campaign controls gated on the codes key, or vice-versa), a Moderator would silently gain or
// lose the secret tools; these cases fail for exactly that reason.
class GiveawaysAccessTest {

    @Test
    fun write_key_enables_campaign_management_but_not_the_code_tools() {
        // A Moderator the write floor admits holds `giveaways:write` (plus `giveaways:read` to see the page), but
        // NOT the Broadcaster-only codes key. Campaign controls are live; the code tools + reveal stay off.
        val held: Set<String> = setOf("giveaways:read", "giveaways:write")

        assertTrue(GiveawaysAccess.canWrite(held), "giveaways:write must enable campaign management")
        assertFalse(
            GiveawaysAccess.canManageCodes(held),
            "the code tools + reveal stay off without giveaways:codes:write",
        )
    }

    @Test
    fun codes_key_gates_only_the_code_tools_and_not_the_campaign_writes() {
        // The gates are independent and not crossed: holding only the codes key unlocks the code tools + reveal
        // but not the campaign lifecycle.
        val held: Set<String> = setOf("giveaways:codes:write")

        assertTrue(GiveawaysAccess.canManageCodes(held))
        assertFalse(GiveawaysAccess.canWrite(held), "the codes key alone must not enable campaign writes")
    }

    @Test
    fun a_broadcaster_holds_both_keys_and_an_empty_set_disables_both() {
        // A Broadcaster clears both floors, so their resolved held set contains both keys → both surfaces live.
        val both: Set<String> = setOf("giveaways:write", "giveaways:codes:write")
        assertTrue(GiveawaysAccess.canWrite(both))
        assertTrue(GiveawaysAccess.canManageCodes(both))

        // A caller below the floors (or a fail-closed resolve) holds neither → both disabled-with-reason / hidden.
        assertFalse(GiveawaysAccess.canWrite(emptySet()))
        assertFalse(GiveawaysAccess.canManageCodes(emptySet()))
    }
}
