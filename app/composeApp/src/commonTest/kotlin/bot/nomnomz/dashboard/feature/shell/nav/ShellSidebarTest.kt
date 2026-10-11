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

import bot.nomnomz.dashboard.core.navigation.ShellPage
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// S-IA-NAV: the sidebar shows the groups Home, Live, Build, People, Loyalty, Music & Voice, Connect and Setup, one
// row per page (a page's tabs are not rows), and each role sees only the groups and rows its floors clear.
class ShellSidebarTest {

    private fun rowsByGroup(role: ManagementRole?): Map<NavGroup, List<ShellPage>> =
        ShellNav.sidebarFor(role).associate { section -> section.group to section.rows.map { it.page } }

    private val moderatorRows: Map<NavGroup, List<ShellPage>> =
        linkedMapOf(
            NavGroup.Home to listOf(ShellPage.Home),
            NavGroup.Live to listOf(ShellPage.Chat, ShellPage.Stream, ShellPage.VtubeStudio, ShellPage.Analytics),
            NavGroup.Build to
                listOf(
                    ShellPage.Commands,
                    ShellPage.EventResponses,
                    ShellPage.TriggersTimers,
                    ShellPage.Pipelines,
                    ShellPage.Scripts,
                    ShellPage.Overlays,
                    ShellPage.Library,
                    ShellPage.Gallery,
                ),
            NavGroup.People to listOf(ShellPage.People),
            NavGroup.Loyalty to listOf(ShellPage.ChannelPoints, ShellPage.Economy, ShellPage.Games, ShellPage.Giveaways),
            NavGroup.MusicVoice to listOf(ShellPage.Music, ShellPage.Tts, ShellPage.MediaShare),
            NavGroup.Connect to listOf(ShellPage.Discord, ShellPage.CustomEvents, ShellPage.Supporters),
            NavGroup.Setup to listOf(ShellPage.Settings),
        )

    @Test
    fun broadcaster_sees_all_eight_groups_in_order_with_every_page_as_one_row() {
        val sections: List<NavSection> = ShellNav.sidebarFor(ManagementRole.Broadcaster)

        assertEquals(
            listOf(
                NavGroup.Home,
                NavGroup.Live,
                NavGroup.Build,
                NavGroup.People,
                NavGroup.Loyalty,
                NavGroup.MusicVoice,
                NavGroup.Connect,
                NavGroup.Setup,
            ),
            sections.map { it.group },
        )
        val byGroup: Map<NavGroup, List<ShellPage>> = rowsByGroup(ManagementRole.Broadcaster)
        assertEquals(
            listOf(ShellPage.Chat, ShellPage.Stream, ShellPage.Obs, ShellPage.VtubeStudio, ShellPage.Analytics),
            byGroup.getValue(NavGroup.Live),
        )
        assertEquals(
            listOf(
                ShellPage.Discord,
                ShellPage.Webhooks,
                ShellPage.CustomEvents,
                ShellPage.Supporters,
                ShellPage.Federation,
                ShellPage.ApiDevices,
            ),
            byGroup.getValue(NavGroup.Connect),
        )
        assertEquals(
            listOf(ShellPage.Integrations, ShellPage.Features, ShellPage.Settings),
            byGroup.getValue(NavGroup.Setup),
        )
        // People is one row; its six tabs are not rows.
        assertEquals(listOf(ShellPage.People), byGroup.getValue(NavGroup.People))
        // A page is never listed twice, even when it has several tab routes.
        val allRows: List<ShellPage> = sections.flatMap { s -> s.rows.map { it.page } }
        assertEquals(allRows.size, allRows.toSet().size)
    }

    @Test
    fun moderator_sees_the_groups_and_rows_its_floors_clear() {
        assertEquals(moderatorRows, rowsByGroup(ManagementRole.Moderator))
    }

    @Test
    fun lead_moderator_sees_the_same_rows_as_a_moderator_and_may_manage_discord() {
        assertEquals(moderatorRows, rowsByGroup(ManagementRole.LeadModerator))
        assertTrue(ShellNav.canManage(ManagementRole.LeadModerator, ShellRoute.Discord))
        assertFalse(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.Discord))
    }

    @Test
    fun a_viewer_without_a_role_or_held_keys_has_no_operator_sidebar() {
        assertTrue(ShellNav.sidebarFor(null).isEmpty())
        assertFalse(ShellNav.hasManagementAccess(null, emptySet()))
    }

    @Test
    fun a_viewer_holding_one_lowered_read_key_sees_only_that_row_in_its_group() {
        val sections: List<NavSection> = ShellNav.sidebarFor(null, setOf("commands:read"))

        assertEquals(listOf(NavGroup.Build), sections.map { it.group })
        assertEquals(listOf(ShellPage.Commands), sections.single().rows.map { it.page })
    }

    @Test
    fun a_row_opens_the_first_tab_this_role_may_read() {
        val people: NavRow =
            ShellNav.sidebarFor(ManagementRole.Moderator).flatMap { it.rows }.single { it.page == ShellPage.People }

        assertEquals(ShellRoute.PeopleOverview, people.route)
    }

    @Test
    fun my_data_and_admin_are_not_sidebar_rows_for_any_role() {
        for (role: ManagementRole in ManagementRole.entries) {
            val pages: Set<ShellPage> = ShellNav.sidebarFor(role).flatMap { s -> s.rows.map { it.page } }.toSet()
            assertFalse(ShellPage.MyData in pages, "My data is in the profile menu, not the sidebar ($role)")
            assertFalse(ShellPage.Admin in pages, "Admin is in the profile menu, not the sidebar ($role)")
        }
    }

    @Test
    fun the_profile_menu_pages_are_not_part_of_the_gated_page_list() {
        assertTrue(ShellNav.isProfileMenuRoute(ShellRoute.MyData))
        assertTrue(ShellNav.isProfileMenuRoute(ShellRoute.Admin))
        assertFalse(ShellNav.isProfileMenuRoute(ShellRoute.Home))
        assertFalse(ShellNav.pages.any { ShellNav.isProfileMenuRoute(it.route) })
    }

    @Test
    fun every_gated_route_is_reachable_through_a_row_of_its_page() {
        val rowPages: Set<ShellPage> =
            ShellNav.sidebarFor(ManagementRole.Broadcaster).flatMap { s -> s.rows.map { it.page } }.toSet()
        val gatedPages: Set<ShellPage> = ShellNav.pages.map { it.route.page }.toSet()

        assertEquals(emptySet(), gatedPages - rowPages)
    }
}
