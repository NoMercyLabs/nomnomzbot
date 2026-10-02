// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

// Owner 2026-10-02: a "first message this stream" pipeline for one viewer needs an If on {user}. The server's
// condition is "comparison"; the editor's typed form was keyed "var_compare", a type the server never sends,
// so the real condition fell back to a raw key/value editor with no Left / Operator / Right fields.
class PipelineConditionPaletteTest {

    @Test
    fun the_server_comparison_condition_gets_the_typed_left_operator_right_form() {
        val palette: RuntimePalette =
            PipelineCatalogue.buildPalette(
                PipelineCatalogueRemote(conditions = listOf(PipelineConditionDescriptor(type = "comparison"))),
            )

        val comparison: PaletteBlock = assertNotNull(palette.condition("comparison"))
        assertTrue(comparison.hasHints)
        assertEquals(listOf("left", "operator", "right"), comparison.fields.map { it.key })
        assertEquals(listOf(true, true, false), comparison.fields.map { it.required })
    }
}
