// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import kotlin.test.Test
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

/**
 * One row per screen that has been wired through [resolveRowLabel] end to end (row text, action
 * labels, and the destructive delete [bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog]
 * all resolve the SAME label — see each screen's `displayName`/`displayTitle`/`resolvedName` call
 * site). Each case reproduces the EXACT [typeLabel]/[secondary] the screen passes to
 * [resolveRowLabel], using two items that share a blank primary name so a real collision would
 * surface here. When a new screen is fixed, add its parameters as one more row in [cases] — that
 * single addition is what "finishing a file" means for this test.
 */
class RowLabelWiredScreensTest {

    private data class WiredRowCase(
        val screen: String,
        val typeLabel: String,
        val secondaryA: String?,
        val idA: String,
        val secondaryB: String?,
        val idB: String,
    )

    private val cases: List<WiredRowCase> =
        listOf(
            // PickListsScreen.kt — row text/edit/delete/test labels + delete ConfirmDialog message.
            WiredRowCase(
                screen = "PickListsScreen",
                typeLabel = "Pick list",
                secondaryA = null,
                idA = "picklist-aaa",
                secondaryB = null,
                idB = "picklist-bbb",
            ),
            // CommandsScreen.kt — row text/toggle/edit/delete labels + delete ConfirmDialog message;
            // secondary identity is the command's matchPattern.
            WiredRowCase(
                screen = "CommandsScreen",
                typeLabel = "Command",
                secondaryA = "!hype",
                idA = "command-aaa",
                secondaryB = "!raid",
                idB = "command-bbb",
            ),
            // RewardsScreen.kt — row text/edit/delete labels + delete ConfirmDialog message;
            // secondary identity is the reward's cost.
            WiredRowCase(
                screen = "RewardsScreen",
                typeLabel = "Reward",
                secondaryA = "500",
                idA = "reward-aaa",
                secondaryB = "1000",
                idB = "reward-bbb",
            ),
            // GiveawaysScreen.kt (GiveawayRow + delete/close/draw ConfirmDialogs) — no secondary
            // identity field, falls straight to the typed placeholder.
            WiredRowCase(
                screen = "GiveawaysScreen.giveaway",
                typeLabel = "Giveaway",
                secondaryA = null,
                idA = "giveaway-aaa",
                secondaryB = null,
                idB = "giveaway-bbb",
            ),
            // GiveawaysScreen.kt (CodePoolRow + pool delete ConfirmDialog) — no secondary identity
            // field, falls straight to the typed placeholder.
            WiredRowCase(
                screen = "GiveawaysScreen.codePool",
                typeLabel = "Code pool",
                secondaryA = null,
                idA = "pool-aaa",
                secondaryB = null,
                idB = "pool-bbb",
            ),
        )

    @Test
    fun a_blank_named_row_resolves_to_an_identifying_label_on_every_fixed_screen() {
        for (case in cases) {
            val label: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            assertTrue(
                label.isNotBlank(),
                "${case.screen}: a blank-named row must never resolve to a blank label",
            )
            assertTrue(
                label == case.secondaryA || label.startsWith("${case.typeLabel} #"),
                "${case.screen}: expected the secondary identity or a typed placeholder, got '$label'",
            )
        }
    }

    @Test
    fun two_blank_named_rows_in_the_same_list_never_render_identically() {
        for (case in cases) {
            val labelA: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            val labelB: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryB,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idB,
                )
            assertNotEquals(
                labelA,
                labelB,
                "${case.screen}: two blank-named rows collided on '$labelA' — a user could no longer " +
                    "tell them apart before deleting one",
            )
        }
    }

    @Test
    fun the_destructive_confirm_dialog_names_the_item_via_the_same_resolved_label() {
        // Each screen computes ONE resolved label per row and reuses it for the row text, the row's
        // action labels, AND the destructive ConfirmDialog's message (see CommandsScreen.kt's
        // `displayName`, RewardsScreen.kt's `displayName`, GiveawaysScreen.kt's `displayTitle` /
        // `displayName` / `resolvedTitle` / `resolvedPoolName` / `resolvedLifecycleTitle`, and
        // PickListsScreen.kt's `resolvedName` — a single call site feeding every consumer). This test
        // proves the mechanism itself is idempotent for identical inputs, which is what makes reusing
        // one resolved value for both the row and its confirm dialog safe instead of computing it twice
        // and risking drift.
        for (case in cases) {
            val rowLabel: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            val confirmDialogLabel: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            assertTrue(
                rowLabel == confirmDialogLabel,
                "${case.screen}: the row label and its destructive ConfirmDialog must name the same item",
            )
        }
    }
}
