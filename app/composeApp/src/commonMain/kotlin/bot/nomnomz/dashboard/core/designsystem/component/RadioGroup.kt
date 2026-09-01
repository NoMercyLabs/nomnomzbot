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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

/**
 * shadcn/ui RadioGroup ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * `RadioGroup` / `RadioItem` is M3-wrapped for a11y (the group semantics), rendered on the
 * existing [RadioButton] indicator (Figma node 235:1461, `SelectionControls.kt`). The group owns
 * selection: [selected] identifies the current option by [T], [onSelectedChange] receives the
 * clicked option's value. `Modifier.selectableGroup()` gives the group's items single-selection
 * semantics for assistive tech (only one item in the group is ever focus-announced as selected).
 */
@Composable
fun <T> RadioGroup(
    options: List<T>,
    selected: T?,
    onSelectedChange: (T) -> Unit,
    label: (T) -> String,
    modifier: Modifier = Modifier,
    enabled: (T) -> Boolean = { true },
) {
    val spacing: Spacing = LocalSpacing.current
    Column(
        modifier = modifier.selectableGroup(),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        options.forEach { option ->
            RadioItem(
                selected = option == selected,
                onClick = { onSelectedChange(option) },
                text = label(option),
                enabled = enabled(option),
            )
        }
    }
}

/** A single [RadioGroup] option — indicator + label, both driven by the group's selection. */
@Composable
fun RadioItem(
    selected: Boolean,
    onClick: () -> Unit,
    text: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Row(
        modifier =
            modifier
                .selectable(
                    selected = selected,
                    enabled = enabled,
                    role = Role.RadioButton,
                    onClick = onClick,
                ),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        RadioButton(selected = selected, onClick = null, enabled = enabled)
        CompositionLocalProvider(
            LocalContentColor provides if (enabled) tokens.foreground else tokens.mutedForeground,
            LocalTextStyle provides typography.sm,
        ) {
            Text(text = text, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}
