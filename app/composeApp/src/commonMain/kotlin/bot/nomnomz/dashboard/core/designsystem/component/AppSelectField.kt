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

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronDownGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// 1dp border stroke — not a layout spacing value (matches AppTextField's field border).
private val SelectBorderWidth: Dp = 1.dp

/**
 * The shadcn/ui Select ported to Compose — the field-styled DROPDOWN TRIGGER (frontend-design-system.md §4).
 *
 * Visually an [AppTextField] (label above, bordered box, trailing chevron), but its trigger is a plain,
 * non-editable, fully-clickable [Row] — NOT a [androidx.compose.foundation.text.BasicTextField]. That is the
 * whole point: an interactive text field steals the tap for focus, so wrapping one in `.clickable` to open a
 * menu only ever opened from the label/padding (the field body was dead). A select must open from anywhere on
 * its surface, so this renders the current [value] as inert [Text] and puts the click on the whole field.
 *
 * The caller owns [expanded] and supplies the [menu] items (the same `@Composable ColumnScope.() -> Unit`
 * [DropdownMenu] takes), so this composable owns only the trigger chrome + anchoring — never the option data.
 */
@Composable
fun AppSelectField(
    value: String,
    label: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    placeholder: String? = null,
    supportingText: String? = null,
    menu: @Composable ColumnScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)
    val borderColor: Color =
        when {
            !enabled -> tokens.border.copy(alpha = 0.5f)
            expanded -> tokens.ring
            else -> tokens.border
        }
    val hasValue: Boolean = value.isNotEmpty()
    val displayColor: Color =
        when {
            !enabled -> tokens.mutedForeground
            hasValue -> tokens.foreground
            else -> tokens.mutedForeground
        }

    Column(modifier = modifier) {
        if (label.isNotEmpty()) {
            CompositionLocalProvider(LocalTextStyle provides typography.sm.copy(color = tokens.foreground)) {
                Text(text = label, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Spacer(modifier = Modifier.height(spacing.s1_5))
        }

        Box {
            Row(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .clip(shape)
                        .border(width = SelectBorderWidth, color = borderColor, shape = shape)
                        .background(color = tokens.background)
                        .then(
                            if (enabled) {
                                Modifier.clickable { onExpandedChange(!expanded) }
                            } else {
                                Modifier
                            }
                        )
                        .padding(horizontal = spacing.s3, vertical = spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(modifier = Modifier.weight(1f)) {
                    Text(
                        text = value.ifEmpty { placeholder ?: "" },
                        style = typography.sm.copy(color = displayColor),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Icon(
                    imageVector = ChevronDownGlyph,
                    contentDescription = null,
                    tint = tokens.mutedForeground,
                    modifier = Modifier.size(spacing.s4),
                )
            }

            DropdownMenu(
                expanded = expanded,
                onDismissRequest = { onExpandedChange(false) },
                content = menu,
            )
        }

        if (!supportingText.isNullOrEmpty()) {
            Spacer(modifier = Modifier.height(spacing.s1))
            CompositionLocalProvider(
                LocalTextStyle provides typography.xs.copy(color = tokens.mutedForeground)
            ) {
                Text(text = supportingText, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}
