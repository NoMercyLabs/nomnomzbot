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

// Proves the per-ACTION write floors (frontend-ia.md §3 sub-page rows) that the page-level manage floor can't
// express: Rewards create/delete and Economy payout rules rise to Broadcaster ABOVE their Editor page floor,
// while Song-Request queue moderation drops to Moderator BELOW it. These are the decisions a screen feeds into
// `ManageGate`, so testing the model proves which controls a given role sees enabled vs disabled-with-reason.
class ManageActionFloorTest {

    // ── Rewards: create/delete = Broadcaster, edit/toggle = the page's Editor floor ───────────────────────────

    @Test
    fun reward_lifecycle_needs_broadcaster_even_though_the_page_manages_at_editor() {
        // The page floor is Editor (an Editor can edit/toggle a reward)...
        assertEquals(ManagementRole.Editor, ShellNav.manageFloorFor(ShellRoute.Rewards, ManageAction.Default))
        // ...but creating/deleting a reward is a Broadcaster-only lifecycle action.
        assertEquals(
            ManagementRole.Broadcaster,
            ShellNav.manageFloorFor(ShellRoute.Rewards, ManageAction.RewardLifecycle),
        )

        // An Editor may edit/toggle but NOT create/delete; a Broadcaster may do both.
        assertTrue(ShellNav.canManage(ManagementRole.Editor, ShellRoute.Rewards, ManageAction.Default))
        assertFalse(ShellNav.canManage(ManagementRole.Editor, ShellRoute.Rewards, ManageAction.RewardLifecycle))
        assertTrue(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Rewards, ManageAction.RewardLifecycle))
    }

    // ── Economy: payout/earn rules = Broadcaster, the rest of the editor = Editor ────────────────────────────

    @Test
    fun economy_payout_rules_need_broadcaster_above_the_editor_page_floor() {
        assertEquals(ManagementRole.Editor, ShellNav.manageFloorFor(ShellRoute.Economy, ManageAction.Default))
        assertEquals(
            ManagementRole.Broadcaster,
            ShellNav.manageFloorFor(ShellRoute.Economy, ManageAction.EconomyPayoutRules),
        )

        // An Editor configures the currency but cannot touch payout/earn rules; a Broadcaster can.
        assertTrue(ShellNav.canManage(ManagementRole.Editor, ShellRoute.Economy, ManageAction.Default))
        assertFalse(ShellNav.canManage(ManagementRole.Editor, ShellRoute.Economy, ManageAction.EconomyPayoutRules))
        assertTrue(
            ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Economy, ManageAction.EconomyPayoutRules),
        )
    }

    // ── Song Requests: queue moderation = Moderator, BELOW the Editor page floor ─────────────────────────────

    @Test
    fun song_queue_moderation_drops_to_moderator_below_the_editor_page_floor() {
        assertEquals(ManagementRole.Editor, ShellNav.manageFloorFor(ShellRoute.SongRequests, ManageAction.Default))
        assertEquals(
            ManagementRole.Moderator,
            ShellNav.manageFloorFor(ShellRoute.SongRequests, ManageAction.SongQueueModeration),
        )

        // A Mod cannot reach the page's Editor-floored config, but CAN moderate the live queue (skip/remove).
        assertFalse(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.SongRequests, ManageAction.Default))
        assertTrue(
            ShellNav.canManage(ManagementRole.Moderator, ShellRoute.SongRequests, ManageAction.SongQueueModeration),
        )
    }

    // ── Default action == the page's own manage floor (no special-casing the common path) ────────────────────

    @Test
    fun the_default_action_floor_equals_the_pages_own_manage_floor_for_every_page() {
        ShellNav.pages.forEach { page ->
            assertEquals(
                page.manageFloor,
                ShellNav.manageFloorFor(page.route, ManageAction.Default),
                "default action floor must equal the page manage floor for ${page.route}",
            )
        }
    }

    @Test
    fun a_viewer_can_manage_no_action_on_any_page() {
        // A null role (viewer) clears no floor, default or override — nothing is manageable.
        ShellNav.pages.forEach { page ->
            assertFalse(ShellNav.canManage(null, page.route, ManageAction.Default))
        }
        assertFalse(ShellNav.canManage(null, ShellRoute.Rewards, ManageAction.RewardLifecycle))
        assertFalse(ShellNav.canManage(null, ShellRoute.SongRequests, ManageAction.SongQueueModeration))
    }
}
