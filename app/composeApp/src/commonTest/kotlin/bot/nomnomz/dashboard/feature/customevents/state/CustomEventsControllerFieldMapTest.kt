// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.customevents.state

import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * Proves the S100-KEYPICKER-TESTFETCH key-picker state transition: clicking a key-path option from the test-fetch
 * result sets the currently-focused field-map row's path to that key's path string, and leaves every other row —
 * and the case where nothing is focused — untouched.
 */
class CustomEventsControllerFieldMapTest {

    @Test
    fun selecting_a_key_sets_the_focused_rows_path_and_leaves_others_untouched() {
        val rows: List<FieldMapRow> =
            listOf(
                FieldMapRow(key = "bpm", path = ""),
                FieldMapRow(key = "device", path = ""),
            )

        val updated: List<FieldMapRow> = applyKeyPathSelection(rows, focusedIndex = 1, keyPath = "\$.meta.device")

        assertEquals("", updated[0].path)
        assertEquals("bpm", updated[0].key)
        assertEquals("\$.meta.device", updated[1].path)
        assertEquals("device", updated[1].key)
    }

    @Test
    fun selecting_a_key_with_no_row_focused_leaves_all_rows_unchanged() {
        val rows: List<FieldMapRow> = listOf(FieldMapRow(key = "bpm", path = "\$.old"))

        val updated: List<FieldMapRow> = applyKeyPathSelection(rows, focusedIndex = null, keyPath = "\$.new")

        assertEquals(rows, updated)
    }

    @Test
    fun selecting_a_key_with_an_out_of_range_focus_leaves_all_rows_unchanged() {
        val rows: List<FieldMapRow> = listOf(FieldMapRow(key = "bpm", path = "\$.old"))

        val updated: List<FieldMapRow> = applyKeyPathSelection(rows, focusedIndex = 5, keyPath = "\$.new")

        assertEquals(rows, updated)
    }

    @Test
    fun toFieldMap_drops_blank_keys_and_toFieldMapRows_is_its_inverse() {
        val rows: List<FieldMapRow> =
            listOf(
                FieldMapRow(key = "bpm", path = "\$.data.bpm"),
                FieldMapRow(key = "", path = "\$.ignored"),
            )

        val map: Map<String, String> = rows.toFieldMap()

        assertEquals(mapOf("bpm" to "\$.data.bpm"), map)
        assertEquals(listOf(FieldMapRow("bpm", "\$.data.bpm")), map.toFieldMapRows())
    }
}
