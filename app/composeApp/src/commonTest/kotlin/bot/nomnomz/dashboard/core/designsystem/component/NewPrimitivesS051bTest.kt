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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import kotlin.test.Test

/**
 * S051b: proves the 3 catalogued-but-missing primitives this slice builds — [Input],
 * [RadioGroup], [Toast] — exist as real, token-bound Compose composables rather than merely
 * compiling. [DesignSystemStyleGuardTest] only guards raw hex/dp and off-catalogue Material3
 * imports in `feature/`; it does not enumerate catalogue primitives, so this test instantiates
 * every documented variant/state for each of the 3 directly
 * (`frontend-design-system.catalogue.md` rows for Input/RadioGroup/Toast).
 */
@OptIn(ExperimentalTestApi::class)
class NewPrimitivesS051bTest {

    @Test
    fun input_renders_all_sizes_with_error_and_disabled_states() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Input(value = "", onValueChange = {}, label = "Channel name", size = InputSize.Sm)
                Input(value = "abc", onValueChange = {}, label = "Command", size = InputSize.Default)
                Input(value = "", onValueChange = {}, label = "Prefix", size = InputSize.Lg)
                Input(
                    value = "bad",
                    onValueChange = {},
                    label = "Invalid field",
                    isError = true,
                    errorText = "This value is not allowed.",
                )
                Input(value = "", onValueChange = {}, label = "Disabled field", enabled = false)
            }
        }
        onNodeWithText("Channel name").assertExists()
        onNodeWithText("Command").assertExists()
        onNodeWithText("Prefix").assertExists()
        onNodeWithText("Invalid field").assertExists()
        onNodeWithText("This value is not allowed.").assertExists()
        onNodeWithText("Disabled field").assertExists()
    }

    private enum class Platform { Twitch, Kick, Youtube }

    @Test
    fun radio_group_renders_options_with_selection_and_disabled_state() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                RadioGroup(
                    options = listOf(Platform.Twitch, Platform.Kick, Platform.Youtube),
                    selected = Platform.Twitch,
                    onSelectedChange = {},
                    label = { it.name },
                    enabled = { it != Platform.Youtube },
                )
            }
        }
        onNodeWithText("Twitch").assertExists()
        onNodeWithText("Kick").assertExists()
        onNodeWithText("Youtube").assertExists()
    }

    @Test
    fun toast_renders_default_and_destructive_variants_with_dismiss() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                Toast(
                    text = "Saved successfully.",
                    dismissLabel = "Dismiss",
                    onDismiss = {},
                    variant = ToastVariant.Default,
                )
                Toast(
                    text = "Couldn't save the change.",
                    dismissLabel = "Dismiss",
                    onDismiss = {},
                    variant = ToastVariant.Destructive,
                )
            }
        }
        onNodeWithText("Saved successfully.").assertExists()
        onNodeWithText("Couldn't save the change.").assertExists()
    }
}
