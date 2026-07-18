// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

/** A muted single-line empty state inside a card — the admin panel's "nothing here" affordance. */
@Composable
internal fun EmptyLine(text: String) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    Card(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = text,
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.padding(spacing.s4),
        )
    }
}

/**
 * A labelled single-select field: the label above a bordered button that opens a themed [DropdownMenu] of
 * [options] (each an `id to label`). Used across the IAM/tenant dialogs for the role / user / status pickers,
 * so no dialog re-implements a picker by hand.
 */
@Composable
internal fun PickerField(
    label: String,
    selectedLabel: String,
    options: List<Pair<String, String>>,
    onSelect: (id: String, label: String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    var expanded: Boolean by remember { mutableStateOf(false) }

    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.foreground)
        Box {
            OutlinedButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
                Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = selectedLabel.ifBlank { "—" },
                        style = typography.sm,
                        color = if (selectedLabel.isBlank()) tokens.mutedForeground else tokens.foreground,
                    )
                }
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                options.forEach { (id, optionLabel) ->
                    DropdownMenuItem(
                        text = { Text(text = optionLabel, style = typography.sm) },
                        onClick = {
                            onSelect(id, optionLabel)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}
