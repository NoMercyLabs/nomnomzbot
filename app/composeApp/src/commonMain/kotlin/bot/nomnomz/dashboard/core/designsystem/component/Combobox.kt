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

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.combobox_no_results
import org.jetbrains.compose.resources.stringResource

/**
 * shadcn/ui Combobox ported to Compose (frontend-design-system.md §4.1¹, catalogue row).
 *
 * A composite — no 1:1 shadcn primitive — built from [Input] (the filter text field) and
 * [Popover] (the anchored results surface), matching the catalogue's "Popover + Command" note:
 * this composable implements the filtering inline rather than adding a separate `Command`
 * primitive, since the catalogue's "to build" list names only `Combobox`, not `Command`.
 *
 * States per the catalogue row: closed · open · focused. The caller owns [expanded] and
 * [query] (the filter text); [options] is the already-filtered list to render (mirrors
 * [SearchPickerField]'s ownership split — this composable owns only the field + list chrome).
 *
 * @param T the option type; [optionLabel] renders it both as the filter value and each row.
 */
@Composable
fun <T> Combobox(
    query: String,
    onQueryChange: (String) -> Unit,
    options: List<T>,
    onSelect: (T) -> Unit,
    optionLabel: (T) -> String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    label: String = "",
    placeholder: String? = null,
    enabled: Boolean = true,
    noResultsText: String? = null,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Box(modifier = modifier) {
        Input(
            value = query,
            onValueChange = {
                onQueryChange(it)
                onExpandedChange(true)
            },
            label = label,
            enabled = enabled,
            placeholder = placeholder,
        )

        Popover(
            expanded = expanded && enabled,
            onDismissRequest = { onExpandedChange(false) },
        ) {
            Column(modifier = Modifier.widthIn(min = 220.dp)) {
                if (options.isEmpty()) {
                    Text(
                        text = noResultsText ?: stringResource(Res.string.combobox_no_results),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                        modifier = Modifier.fillMaxWidth().padding(spacing.s3),
                    )
                } else {
                    options.forEach { option: T ->
                        Text(
                            text = optionLabel(option),
                            style = typography.sm,
                            color = tokens.popoverForeground,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier =
                                Modifier
                                    .fillMaxWidth()
                                    .clip(RoundedCornerShape(tokens.radius.sm))
                                    .clickable(enabled = enabled) {
                                        onSelect(option)
                                        onExpandedChange(false)
                                    }
                                    .padding(horizontal = spacing.s3, vertical = spacing.s2),
                        )
                    }
                }
            }
        }
    }
}
