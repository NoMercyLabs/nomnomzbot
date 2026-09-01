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
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// 1dp hairline row/caption border — not a layout spacing value.
private val TableBorderWidth: Dp = 1.dp

/**
 * shadcn/ui Table ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based — a plain [Column] scaffold (Compose has no native `<table>` layout
 * primitive), composed from [TableHeader] / [TableBody] / [TableRow] / [TableHead] /
 * [TableCell] / [TableCaption] parts exactly matching shadcn's part list. [TableRow] carries
 * the row states (`default` · `hovered` · `selected`) via [selected] plus live hover tracking.
 */
@Composable
fun Table(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    Column(modifier = modifier.fillMaxWidth(), content = content)
}

/** [Table] header part — wraps the [TableRow] of [TableHead] cells. */
@Composable
fun TableHeader(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    Column(
        modifier = modifier.fillMaxWidth().border(width = TableBorderWidth, color = tokens.border),
        content = content,
    )
}

/** [Table] body part — wraps the data [TableRow]s. */
@Composable
fun TableBody(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    Column(modifier = modifier.fillMaxWidth(), content = content)
}

/**
 * [Table] row part. [selected] renders the `selected` row state ([Tokens.accent] fill); when
 * not selected, a live pointer-hover ([collectIsHoveredAsState]) renders the `hovered` state
 * with a lighter [Tokens.muted] fill — otherwise the row shows the plain `default` state.
 * [onClick] is optional so header rows and non-interactive tables stay unclickable.
 */
@Composable
fun TableRow(
    modifier: Modifier = Modifier,
    selected: Boolean = false,
    onClick: (() -> Unit)? = null,
    content: @Composable RowScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val interactionSource = remember { MutableInteractionSource() }
    val isHovered: Boolean by interactionSource.collectIsHoveredAsState()

    val rowBackground =
        when {
            selected -> tokens.accent
            isHovered -> tokens.muted
            else -> tokens.background
        }

    val clickableModifier: Modifier =
        if (onClick != null) {
            Modifier.clickable(interactionSource = interactionSource, indication = null, onClick = onClick)
        } else {
            Modifier.hoverable(interactionSource)
        }

    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .border(width = TableBorderWidth, color = tokens.border)
                .background(rowBackground)
                .then(clickableModifier),
        content = content,
    )
}

/** [Table] header cell part — bold [Tokens.mutedForeground] label, left-aligned like shadcn. */
@Composable
fun TableHead(
    text: String,
    modifier: Modifier = Modifier,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    androidx.compose.material3.Text(
        text = text,
        modifier = modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
        style = typography.sm.copy(fontWeight = FontWeight.Medium),
        color = tokens.mutedForeground,
    )
}

/** [Table] data cell part. */
@Composable
fun TableCell(
    text: String,
    modifier: Modifier = Modifier,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    androidx.compose.material3.Text(
        text = text,
        modifier = modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
        style = typography.sm,
        color = tokens.foreground,
    )
}

/** [Table] caption part — muted summary line rendered below the table body. */
@Composable
fun TableCaption(
    text: String,
    modifier: Modifier = Modifier,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    androidx.compose.material3.Text(
        text = text,
        modifier = modifier.fillMaxWidth().padding(top = spacing.s3),
        style = typography.sm,
        color = tokens.mutedForeground,
    )
}
