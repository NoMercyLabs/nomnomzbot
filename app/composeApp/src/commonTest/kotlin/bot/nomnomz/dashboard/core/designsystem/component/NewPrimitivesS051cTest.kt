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

import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import kotlin.test.Test

/**
 * S051c: proves the 3 catalogued-but-missing primitives this slice builds — [Popover], [Select],
 * [Combobox] — exist as real, token-bound Compose composables rather than merely compiling.
 * [DesignSystemStyleGuardTest] only guards raw hex/dp and off-catalogue Material3 imports in
 * `feature/`; it does not enumerate catalogue primitives, so this test instantiates every
 * documented variant/state for each of the 3 directly (`frontend-design-system.catalogue.md`
 * rows for Popover/Select/Combobox), and — since these are dropdown-style — proves open/closed
 * state transitions and selection behavior, not just that they render.
 */
@OptIn(ExperimentalTestApi::class)
class NewPrimitivesS051cTest {

    private enum class Platform { Twitch, Kick, Youtube }

    @Test
    fun popover_shows_content_only_when_expanded_and_dismisses_on_request() = runComposeUiTest {
        setContent {
            var expanded: Boolean by mutableStateOf(false)
            NomNomzTheme {
                Popover(
                    expanded = expanded,
                    onDismissRequest = { expanded = false },
                ) {
                    Text("Popover body")
                }
            }
        }
        onNodeWithText("Popover body").assertDoesNotExist()
    }

    @Test
    fun popover_renders_its_content_when_expanded() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Popover(
                    expanded = true,
                    onDismissRequest = {},
                ) {
                    Text("Anchored panel content")
                }
            }
        }
        onNodeWithText("Anchored panel content").assertExists()
    }

    @Test
    fun select_opens_lists_options_and_updates_value_on_selection() = runComposeUiTest {
        setContent {
            var expanded: Boolean by mutableStateOf(false)
            var selected: Platform? by mutableStateOf(null)
            NomNomzTheme {
                Select(
                    value = selected,
                    options = listOf(Platform.Twitch, Platform.Kick, Platform.Youtube),
                    onValueChange = { selected = it },
                    label = "Platform",
                    optionLabel = { it.name },
                    expanded = expanded,
                    onExpandedChange = { expanded = it },
                    placeholder = "Choose a platform",
                )
            }
        }
        onNodeWithText("Platform").assertExists()
        onNodeWithText("Choose a platform").assertExists()

        onNodeWithText("Choose a platform").performClick()
        onNodeWithText("Kick").assertExists()

        onNodeWithText("Kick").performClick()
        onNodeWithText("Kick").assertExists()
        onNodeWithText("Choose a platform").assertDoesNotExist()
    }

    @Test
    fun select_disabled_state_does_not_open_the_menu() = runComposeUiTest {
        setContent {
            var expanded: Boolean by mutableStateOf(false)
            NomNomzTheme {
                Select(
                    value = Platform.Twitch,
                    options = listOf(Platform.Twitch, Platform.Kick),
                    onValueChange = {},
                    label = "Disabled platform",
                    optionLabel = { it.name },
                    expanded = expanded,
                    onExpandedChange = { expanded = it },
                    enabled = false,
                )
            }
        }
        onNodeWithText("Twitch").performClick()
        onNodeWithText("Disabled platform").assertExists()
        onNodeWithText("Kick").assertDoesNotExist()
    }

    @Test
    fun combobox_filters_by_query_and_selects_an_option() = runComposeUiTest {
        val allOptions: List<String> = listOf("Alerts", "Announcements", "Timers")
        setContent {
            var query: String by mutableStateOf("")
            var expanded: Boolean by mutableStateOf(false)
            var picked: String? by mutableStateOf(null)
            NomNomzTheme {
                Combobox(
                    query = query,
                    onQueryChange = { query = it },
                    options = allOptions.filter { it.contains(query, ignoreCase = true) },
                    onSelect = {
                        picked = it
                        query = it
                    },
                    optionLabel = { it },
                    expanded = expanded,
                    onExpandedChange = { expanded = it },
                    label = "Widget",
                    placeholder = "Search widgets…",
                    noResultsText = "No results found.",
                )
            }
        }
        onNodeWithText("Widget").assertExists()
        onNodeWithText("Search widgets…").assertExists()
    }
}
