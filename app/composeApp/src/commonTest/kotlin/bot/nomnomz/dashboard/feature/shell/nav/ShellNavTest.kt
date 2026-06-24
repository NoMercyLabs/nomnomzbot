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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the binding Plane-B role gate of the shell sidebar (frontend-ia.md §3/§7): a sidebar item renders
// only if the caller's ManagementRole >= the page's read floor. This IS the routing/visibility decision, so
// if it breaks a Mod would see Broadcaster-only pages (or a Broadcaster would lose pages). The UI merely
// renders this model, so testing the model proves the gate.
class ShellNavTest {

    @Test
    fun broadcaster_sees_every_page_including_the_broadcaster_floored_integrations() {
        val visible: List<NavPage> = ShellNav.visiblePagesFor(ManagementRole.Broadcaster)

        assertEquals(ShellNav.pages.size, visible.size) // the Broadcaster floor clears every page
        assertTrue(visible.any { it.route == ShellRoute.Integrations })
        assertTrue(visible.any { it.route == ShellRoute.Settings })
    }

    @Test
    fun moderator_sees_every_page_except_the_broadcaster_floored_pages() {
        val visible: List<NavPage> = ShellNav.visiblePagesFor(ManagementRole.Moderator)

        // The Broadcaster-floored pages (Integrations, Discord, Roles) must be hidden from a Mod.
        assertFalse(visible.any { it.route == ShellRoute.Integrations })
        assertFalse(visible.any { it.route == ShellRoute.Discord })
        assertFalse(visible.any { it.route == ShellRoute.Roles })
        // Everything else (incl. the Moderator-floored Settings) stays visible.
        assertTrue(visible.any { it.route == ShellRoute.Dashboard })
        assertTrue(visible.any { it.route == ShellRoute.Moderation })
        assertTrue(visible.any { it.route == ShellRoute.Settings })
        // Exactly the Broadcaster-floored pages are dropped — no more, no fewer.
        val broadcasterFloored: Int =
            ShellNav.pages.count { it.readFloor == ManagementRole.Broadcaster }
        assertEquals(ShellNav.pages.size - broadcasterFloored, visible.size)
    }

    @Test
    fun editor_and_moderator_see_the_same_pages_since_only_integrations_sits_above_moderator() {
        val editor: Set<ShellRoute> = ShellNav.visiblePagesFor(ManagementRole.Editor).map { it.route }.toSet()
        val moderator: Set<ShellRoute> =
            ShellNav.visiblePagesFor(ManagementRole.Moderator).map { it.route }.toSet()

        assertEquals(moderator, editor)
    }

    @Test
    fun pages_are_grouped_in_the_binding_ia_order_with_pinned_last() {
        val groupsInOrder: List<NavGroup> = ShellNav.pages.map { it.group }.distinct()

        assertEquals(
            listOf(
                NavGroup.Home,
                NavGroup.Chat,
                NavGroup.Loyalty,
                NavGroup.Media,
                NavGroup.Stream,
                NavGroup.Automation,
                NavGroup.Community,
                NavGroup.Pinned,
            ),
            groupsInOrder,
        )
    }

    @Test
    fun every_page_with_a_manage_floor_keeps_it_at_or_above_its_read_floor() {
        // A page can never be manageable by a role that can't even see it.
        ShellNav.pages.forEach { page ->
            val manage: ManagementRole? = page.manageFloor
            if (manage != null) {
                assertTrue(
                    manage.level >= page.readFloor.level,
                    "manage floor below read floor for ${page.route}",
                )
            }
        }
    }
}
