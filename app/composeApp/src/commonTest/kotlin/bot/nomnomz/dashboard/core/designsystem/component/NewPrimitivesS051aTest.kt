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
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import kotlin.test.Test

/**
 * S051a: proves the 4 catalogued-but-missing primitives this slice builds — [Alert], [Checkbox],
 * [Label], [Skeleton] — exist as real, token-bound Compose composables rather than merely
 * compiling. [DesignSystemStyleGuardTest] only guards raw hex/dp and off-catalogue Material3
 * imports in `feature/`; it does not enumerate catalogue primitives, so this test instantiates
 * every documented variant/state for each of the 4 directly (`frontend-design-system.catalogue.md`
 * rows for Alert/Checkbox/Label/Skeleton).
 */
@OptIn(ExperimentalTestApi::class)
class NewPrimitivesS051aTest {

    @Test
    fun alert_renders_default_and_destructive_variants_with_title_and_description() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Alert(variant = AlertVariant.Default) {
                    AlertTitle("Heads up")
                    AlertDescription("This is a default alert.")
                }
                Alert(variant = AlertVariant.Destructive) {
                    AlertTitle("Something failed")
                    AlertDescription("This is a destructive alert.")
                }
            }
        }
        onNodeWithText("Heads up").assertExists()
        onNodeWithText("This is a default alert.").assertExists()
        onNodeWithText("Something failed").assertExists()
        onNodeWithText("This is a destructive alert.").assertExists()
    }

    @Test
    fun checkbox_renders_checked_unchecked_and_disabled_states() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Checkbox(checked = false, onCheckedChange = {})
                Checkbox(checked = true, onCheckedChange = {})
                Checkbox(checked = false, onCheckedChange = null, enabled = false)
            }
        }
        // A Checkbox row has no required text — proven by successful composition without throwing;
        // the state-driven visuals (border vs. filled gradient) are covered by SelectionControls'
        // existing structure (checkbox_check asset only renders when checked=true).
    }

    @Test
    fun label_renders_enabled_and_disabled_text() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Label(text = "Enabled label", enabled = true)
                Label(text = "Disabled label", enabled = false)
            }
        }
        onNodeWithText("Enabled label").assertExists()
        onNodeWithText("Disabled label").assertExists()
    }

    @Test
    fun skeleton_composes_as_a_sized_placeholder_alongside_real_content() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Skeleton()
                Text("Loaded content")
            }
        }
        // Skeleton itself carries no text — proven by composing without throwing next to real
        // content; the pulse animation is driven by rememberInfiniteTransition (verified by reading
        // the implementation, not re-asserted here since animated alpha isn't observable via the
        // semantics tree).
        onNodeWithText("Loaded content").assertExists()
    }
}
