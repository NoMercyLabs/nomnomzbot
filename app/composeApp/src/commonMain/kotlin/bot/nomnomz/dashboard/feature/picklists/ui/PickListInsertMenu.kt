// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.picklists.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.editor_insert_random_response

// A small insert helper for the command / response / timer text editors: a "+ Random response" button that opens a
// dropdown of the channel's random-response lists (the pick-lists) and inserts the matching `{list.pick.<name>}`
// template into the field — so the bot substitutes a random line from that list at runtime. It exists so the
// feature is discoverable where it is USED (the editors), not only where lists are managed (roles-permissions.md /
// pick-lists UX rework). Renders nothing when the channel has no lists, so an editor stays clean until there is
// something to insert. [onInsert] receives the full `{list.pick.<name>}` token to append at the caret / field end.
@Composable
fun PickListInsertMenu(
    names: List<String>,
    onInsert: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    if (names.isEmpty()) return
    val tokens = LocalTokens.current
    var expanded: Boolean by remember { mutableStateOf(false) }

    Box(modifier = modifier) {
        TextButton(onClick = { expanded = true }) {
            Text(text = stringResource(Res.string.editor_insert_random_response), color = tokens.primary)
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            names.forEach { name ->
                DropdownMenuItem(
                    text = { Text("{list.pick.$name}", color = tokens.cardForeground) },
                    onClick = {
                        onInsert("{list.pick.$name}")
                        expanded = false
                    },
                )
            }
        }
    }
}
