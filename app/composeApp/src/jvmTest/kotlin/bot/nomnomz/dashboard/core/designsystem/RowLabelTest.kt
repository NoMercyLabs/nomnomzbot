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
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

// Owner report: "some ui rows have no name but do have values and actions." A row a user cannot
// identify but CAN delete/disable is a safety defect. resolveRowLabel is the one shared mechanism —
// these tests prove it never renders empty and that two blank-named items never collide.
class RowLabelTest {

    @Test
    fun uses_the_primary_name_when_present() {
        val label: String =
            resolveRowLabel(primary = "Hype Train", typeLabel = "Pick list", discriminatorSource = "id-1")
        assertEquals("Hype Train", label)
    }

    @Test
    fun null_primary_falls_back_to_secondary_identity() {
        val label: String =
            resolveRowLabel(
                primary = null,
                secondary = "!hype",
                typeLabel = "Command",
                discriminatorSource = "id-2",
            )
        assertEquals("!hype", label)
    }

    @Test
    fun blank_primary_falls_back_to_secondary_identity() {
        val label: String =
            resolveRowLabel(
                primary = "   ",
                secondary = "spotify_now_playing",
                typeLabel = "Widget",
                discriminatorSource = "id-3",
            )
        assertEquals("spotify_now_playing", label)
    }

    @Test
    fun blank_primary_and_blank_secondary_falls_back_to_typed_placeholder() {
        val label: String =
            resolveRowLabel(primary = "", secondary = "", typeLabel = "Reward", discriminatorSource = "id-4")
        assertTrue(label.startsWith("Reward #"), "expected a typed placeholder, got '$label'")
    }

    @Test
    fun typed_placeholder_never_renders_the_raw_id() {
        val rawId = "01J8ZK3Q7X9YABCDEF0123456789"
        val label: String =
            resolveRowLabel(primary = null, typeLabel = "Giveaway", discriminatorSource = rawId)
        assertFalse(label.contains(rawId), "fallback must not expose the raw id/ULID verbatim: '$label'")
    }

    @Test
    fun the_label_is_never_blank_for_any_combination_of_blank_inputs() {
        val blanks: List<String?> = listOf(null, "", "   ", "\t")
        for (primary in blanks) {
            for (secondary in blanks) {
                val label: String =
                    resolveRowLabel(
                        primary = primary,
                        secondary = secondary,
                        typeLabel = "Pipeline",
                        discriminatorSource = "stable-id",
                    )
                assertTrue(label.isNotBlank(), "row label rendered blank for primary=$primary secondary=$secondary")
            }
        }
    }

    @Test
    fun two_blank_named_items_resolve_to_different_discriminating_labels() {
        val labelA: String = resolveRowLabel(primary = null, typeLabel = "Reward", discriminatorSource = "reward-aaa")
        val labelB: String = resolveRowLabel(primary = null, typeLabel = "Reward", discriminatorSource = "reward-bbb")
        assertNotEquals(labelA, labelB, "two different blank-named items must not render the same fallback label")
    }
}
