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
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

// The variable picker lists the variables earlier steps declare. The server marks the declaring field
// (set_variable "name", pick_from_list "variable"); the palette must carry that flag onto the typed field.
class PipelineDeclaredVariablePaletteTest {

    @Test
    fun the_palette_carries_declaresVariable_and_default_onto_the_matching_typed_field() {
        val palette: RuntimePalette =
            PipelineCatalogue.buildPalette(
                PipelineCatalogueRemote(
                    actions =
                        listOf(
                            PipelineActionDescriptor(
                                type = "set_variable",
                                fields =
                                    listOf(
                                        PipelineActionFieldRemote(name = "name", declaresVariable = true),
                                        PipelineActionFieldRemote(name = "value"),
                                    ),
                            ),
                            PipelineActionDescriptor(
                                type = "pick_from_list",
                                fields =
                                    listOf(
                                        PipelineActionFieldRemote(name = "list"),
                                        PipelineActionFieldRemote(
                                            name = "variable",
                                            declaresVariable = true,
                                            declaredVariableDefault = "pick",
                                        ),
                                    ),
                            ),
                        ),
                ),
            )

        val setVariable: PaletteBlock = assertNotNull(palette.action("set_variable"))
        assertTrue(setVariable.fields.single { it.key == "name" }.declaresVariable)
        assertFalse(setVariable.fields.single { it.key == "value" }.declaresVariable)

        val pick: PaletteBlock = assertNotNull(palette.action("pick_from_list"))
        val variable: BlockField = pick.fields.single { it.key == "variable" }
        assertTrue(variable.declaresVariable)
        assertEquals("pick", variable.declaredVariableDefault)
        assertFalse(pick.fields.single { it.key == "list" }.declaresVariable)
    }
}
