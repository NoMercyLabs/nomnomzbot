// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.language.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.language.state.AppLanguage
import bot.nomnomz.dashboard.feature.language.state.LanguageController
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.language_label
import nomnomzbot.composeapp.generated.resources.language_system_default
import org.jetbrains.compose.resources.stringResource

// The display-language selector — System default · English · Nederlands. It's a compact top-level
// control (a text button that opens a small menu), placed as a top-end affordance in App.kt so it's
// reachable from BOTH the onboarding flow (splash/connect/setup — a Dutch-system streamer can pin the
// dashboard to English before they even sign in) AND the authenticated shell.
//
// The trigger shows the active language's own-language display name (e.g. "Nederlands"), with the
// localized "Language" label as the menu's accessibility/leading context. Selecting an option persists
// + applies it live through the controller — every `stringResource` re-renders with no restart.
@Composable
fun LanguagePicker(controller: LanguageController, modifier: Modifier = Modifier) {
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    val current: AppLanguage by controller.current.collectAsStateWithLifecycle()
    var expanded: Boolean by remember { mutableStateOf(false) }

    Box(modifier = modifier) {
        TextButton(onClick = { expanded = true }) {
            Text(
                text = current.label(),
                style = typography.sm,
                color = tokens.foreground,
            )
        }

        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            AppLanguage.entries.forEach { language ->
                DropdownMenuItem(
                    text = { Text(text = language.label(), style = typography.sm) },
                    onClick = {
                        controller.select(language)
                        expanded = false
                    },
                )
            }
        }
    }
}

// The label shown for each option. The two concrete languages are conventionally shown in their OWN
// language (literal display names are correct here), while System default is localized so it always
// reads in the active UI language. The localized "Language" key is also wired in (it labels this control
// in screen-reader/context terms) — keeping the user-facing-string rule satisfied for the picker.
@Composable
private fun AppLanguage.label(): String =
    when (this) {
        AppLanguage.System -> "${stringResource(Res.string.language_label)}: ${stringResource(Res.string.language_system_default)}"
        AppLanguage.English -> "English"
        AppLanguage.Dutch -> "Nederlands"
    }
