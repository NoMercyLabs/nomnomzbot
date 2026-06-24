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

    // ── Viewer (no management role) — Plane A is NOT a nav surface (frontend-ia.md §1/§3) ──────────────────

    @Test
    fun a_viewer_with_no_management_role_sees_no_management_pages() {
        // Every shell page floors at Moderator+, and Plane A "never renders a dashboard": a role-less viewer
        // gets the participation-only surface, not the management shell. The visible page set is empty.
        val visible: List<NavPage> = ShellNav.visiblePagesFor(null)

        assertTrue(visible.isEmpty(), "a viewer must see no management pages, got $visible")
    }

    @Test
    fun a_viewer_can_manage_nothing() {
        // With no management role, every page's write affordances are gated off — there is nothing to manage.
        ShellNav.pages.forEach { page ->
            assertFalse(ShellNav.canManage(null, page.route), "viewer could manage ${page.route}")
        }
    }

    // ── Per-page manage-floor gate (the gated-action set per role, frontend-ia.md §7) ─────────────────────

    @Test
    fun moderator_can_manage_only_the_moderator_floored_pages() {
        // A Mod sees the shell (minus Broadcaster pages) but may only MUTATE pages whose manage floor is
        // Moderator — Chat, Moderation, Community. Commands/Quotes/Timers etc. (Editor manage floor) are
        // read-only to a Mod (the write button disables with "Requires Editor").
        val manageable: Set<ShellRoute> =
            ShellNav.pages
                .filter { ShellNav.canManage(ManagementRole.Moderator, it.route) }
                .map { it.route }
                .toSet()

        assertEquals(
            setOf(ShellRoute.Chat, ShellRoute.Moderation, ShellRoute.Community),
            manageable,
        )
    }

    @Test
    fun editor_can_manage_every_editor_and_moderator_floored_page_but_not_the_broadcaster_ones() {
        val manageable: Set<ShellRoute> =
            ShellNav.pages
                .filter { ShellNav.canManage(ManagementRole.Editor, it.route) }
                .map { it.route }
                .toSet()

        // An Editor can manage the Editor-floored content pages...
        assertTrue(manageable.contains(ShellRoute.Commands))
        assertTrue(manageable.contains(ShellRoute.Rewards))
        assertTrue(manageable.contains(ShellRoute.Pipelines))
        // ...and still the Moderator-floored ones (Editor > Moderator on the ladder)...
        assertTrue(manageable.contains(ShellRoute.Moderation))
        // ...but never the Broadcaster-floored pages (Discord, Roles, Integrations).
        assertFalse(manageable.contains(ShellRoute.Discord))
        assertFalse(manageable.contains(ShellRoute.Roles))
        assertFalse(manageable.contains(ShellRoute.Integrations))
    }

    @Test
    fun broadcaster_can_manage_every_page_that_has_a_manage_floor() {
        // The Broadcaster clears every manage floor; the only non-manageable pages are the read-only ones
        // (Dashboard, Analytics, Settings — manage floor null), which nobody "manages" from the sidebar.
        val expected: Set<ShellRoute> =
            ShellNav.pages.filter { it.manageFloor != null }.map { it.route }.toSet()
        val manageable: Set<ShellRoute> =
            ShellNav.pages
                .filter { ShellNav.canManage(ManagementRole.Broadcaster, it.route) }
                .map { it.route }
                .toSet()

        assertEquals(expected, manageable)
        // Sanity: a read-only page is never "manageable", even for the Broadcaster.
        assertFalse(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Dashboard))
        assertFalse(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Analytics))
    }
}
