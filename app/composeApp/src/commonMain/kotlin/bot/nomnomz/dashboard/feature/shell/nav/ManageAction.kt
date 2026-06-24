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

// A write affordance on a page whose floor differs from the page's own manage floor (frontend-ia.md §3). Most
// write controls gate at their page's [NavPage.manageFloor]; a few are called out with a higher OR lower floor:
//
//   • Rewards  — create/delete a reward needs Broadcaster, while edit/toggle stay at the page's Editor floor.
//   • Economy  — payout/earn rules need Broadcaster, while the rest of the editor stays at Editor.
//   • SongReqs — queue moderation (skip/pause/remove) drops to Moderator, below the page's Editor floor.
//
// A control with no special floor passes [Default] and gates at the page's manage floor — that is the common
// path, so a screen names a [ManageAction] only for an exception. The enum is the single closed list of those
// exceptions; [ShellNav.manageFloorFor] maps each to its floor so the rule lives in the nav model, never in a
// screen.
enum class ManageAction {
    /** No page-specific override: the control gates at the page's own [NavPage.manageFloor]. */
    Default,

    /** Rewards: creating or deleting a channel-point reward (Broadcaster — above the page's Editor floor). */
    RewardLifecycle,

    /** Economy: editing payout / earn rules (Broadcaster — above the page's Editor floor). */
    EconomyPayoutRules,

    /** Song Requests: moderating the live queue — skip/pause/resume/remove (Moderator — below the Editor floor). */
    SongQueueModeration,
}
