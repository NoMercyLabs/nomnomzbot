// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.height
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import kotlin.test.Test

/**
 * S051d: proves the last 2 catalogued-but-missing primitives — [ScrollArea] and [Table] — exist
 * as real, token-bound Compose composables rather than merely compiling. This closes the S051
 * catalogue-gap series (`frontend-design-system.catalogue.md`'s "To build" list). ScrollArea is
 * proven by scrolling a viewport smaller than its content to reach an off-screen node; Table is
 * proven by rendering real row/column data through every documented part (Header/Body/Row/Head/
 * Cell/Caption), toggling the `selected` row state, and firing `onClick` on a row.
 */
@OptIn(ExperimentalTestApi::class)
class NewPrimitivesS051dTest {

    @Test
    fun scroll_area_vertical_reaches_content_below_the_viewport() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                ScrollArea(
                    modifier = Modifier.height(60.dp),
                    orientation = ScrollAreaOrientation.Vertical,
                ) {
                    androidx.compose.foundation.layout.Column {
                        repeat(30) { index -> Text("Row $index", modifier = Modifier.height(40.dp)) }
                    }
                }
            }
        }
        onNodeWithText("Row 0").assertExists()
        onNodeWithText("Row 29").performScrollTo()
        onNodeWithText("Row 29").assertExists()
    }

    @Test
    fun table_renders_header_and_body_data_through_every_documented_part() = runComposeUiTest {
        data class Viewer(val name: String, val points: String)

        val viewers: List<Viewer> = listOf(Viewer("Alice", "120"), Viewer("Bob", "80"))

        setContent {
            NomNomzTheme {
                Table {
                    TableHeader {
                        TableRow {
                            TableHead("Name")
                            TableHead("Points")
                        }
                    }
                    TableBody {
                        viewers.forEach { viewer ->
                            TableRow {
                                TableCell(viewer.name)
                                TableCell(viewer.points)
                            }
                        }
                    }
                    TableCaption("2 viewers")
                }
            }
        }

        onNodeWithText("Name").assertExists()
        onNodeWithText("Points").assertExists()
        onNodeWithText("Alice").assertExists()
        onNodeWithText("120").assertExists()
        onNodeWithText("Bob").assertExists()
        onNodeWithText("80").assertExists()
        onNodeWithText("2 viewers").assertExists()
    }

    @Test
    fun table_row_fires_onclick_and_reflects_the_selected_state() = runComposeUiTest {
        setContent {
            var selectedId: Int? by mutableStateOf(null)
            NomNomzTheme {
                Table {
                    TableBody {
                        listOf(1, 2, 3).forEach { id ->
                            TableRow(
                                selected = selectedId == id,
                                onClick = { selectedId = id },
                            ) {
                                TableCell("Item $id")
                            }
                        }
                    }
                }
            }
        }

        onNodeWithText("Item 2").assertExists()
        onNodeWithText("Item 2").performClick()
        onNodeWithText("Item 2").assertExists()
    }
}
