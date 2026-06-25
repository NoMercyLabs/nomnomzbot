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
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves, per representative screen, exactly which write controls each role sees enabled vs disabled-with-reason
// (frontend-ia.md §7). `rememberManageDecision` is a thin @Composable wrapper that turns these same
// `ShellNav.canManage` outcomes into Allowed / Denied(reason); asserting the outcome here proves the gate every
// screen feeds. Each case mirrors a real control on a real page, so a regression in the floor model surfaces as
// the wrong control state on that page.
class ScreenWriteGateTest {

    // ── Commands (Chat group, Editor manage floor) — the brief's worked example ──────────────────────────────

    @Test
    fun a_moderator_on_commands_sees_every_write_control_disabled() {
        // A Mod can READ Commands (it floors at Moderator) but the page manages at Editor — so New / Edit /
        // Delete / toggle are all below the floor and must render disabled-with-reason.
        assertTrue(ShellNav.visiblePagesFor(ManagementRole.Moderator).any { it.route == ShellRoute.Commands })
        assertFalse(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.Commands))
    }

    @Test
    fun an_editor_on_commands_can_use_every_write_control() {
        assertTrue(ShellNav.canManage(ManagementRole.Editor, ShellRoute.Commands))
    }

    // ── Rewards (Loyalty group) — edit at Editor, create/delete at Broadcaster (sub-page floor) ──────────────

    @Test
    fun an_editor_on_rewards_can_edit_and_toggle_but_not_create_or_delete() {
        // The exact split the brief calls out: edit = Editor, create/delete = Broadcaster.
        assertTrue(
            ShellNav.canManage(ManagementRole.Editor, ShellRoute.Rewards, ManageAction.Default),
            "an Editor must be able to edit/toggle a reward",
        )
        assertFalse(
            ShellNav.canManage(ManagementRole.Editor, ShellRoute.Rewards, ManageAction.RewardLifecycle),
            "create/delete a reward is Broadcaster-only — disabled for an Editor",
        )
    }

    @Test
    fun a_broadcaster_on_rewards_has_every_write_control_enabled() {
        assertTrue(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Rewards, ManageAction.Default))
        assertTrue(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Rewards, ManageAction.RewardLifecycle))
    }

    // ── Song Requests (Music group) — config at Editor, queue moderation drops to Moderator ──────────────────

    @Test
    fun a_moderator_on_song_requests_can_moderate_the_queue_but_not_edit_config() {
        assertFalse(
            ShellNav.canManage(ManagementRole.Moderator, ShellRoute.SongRequests, ManageAction.Default),
            "the page's Editor-floored config is disabled for a Mod",
        )
        assertTrue(
            ShellNav.canManage(ManagementRole.Moderator, ShellRoute.SongRequests, ManageAction.SongQueueModeration),
            "queue moderation (skip/pause/remove) drops to Moderator — enabled for a Mod",
        )
    }

    // ── Moderation (Chat group, Moderator manage floor) — every manager role can act ─────────────────────────

    @Test
    fun every_management_role_can_act_on_moderation_since_it_floors_at_moderator() {
        ManagementRole.entries.forEach { role ->
            assertTrue(
                ShellNav.canManage(role, ShellRoute.Moderation),
                "$role should be able to act on the Moderator-floored Moderation page",
            )
        }
    }

    // ── A read-only page never enables a write control for anyone ────────────────────────────────────────────

    @Test
    fun a_broadcaster_manages_nothing_on_the_read_only_dashboard_and_analytics_pages() {
        assertFalse(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Dashboard))
        assertFalse(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Analytics))
    }
}
