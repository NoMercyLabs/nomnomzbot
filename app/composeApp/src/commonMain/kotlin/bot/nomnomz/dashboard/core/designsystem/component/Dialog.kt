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
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog as WindowDialog
import androidx.compose.ui.window.DialogProperties
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// 1dp border stroke — not a layout spacing value.
private val DialogBorderWidth: Dp = 1.dp

// shadcn dialog default max width (sm:max-w-lg ≈ 512dp).
private val DialogMaxWidth: Dp = 512.dp

/**
 * shadcn/ui Dialog ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based modal: a scrimmed, centred [Tokens.popover] card built on Compose's window
 * `Dialog` primitive. Pair with [DialogTitle] / [DialogDescription] / [DialogFooter] for the shadcn
 * part structure. For the common confirm/notice shape prefer [AlertDialog], which matches the
 * Material3 signature for a drop-in swap.
 */
@Composable
fun Dialog(
    onDismissRequest: () -> Unit,
    modifier: Modifier = Modifier,
    dismissOnBackPress: Boolean = true,
    dismissOnClickOutside: Boolean = true,
    content: @Composable ColumnScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.lg)

    WindowDialog(
        onDismissRequest = onDismissRequest,
        properties =
            DialogProperties(
                dismissOnBackPress = dismissOnBackPress,
                dismissOnClickOutside = dismissOnClickOutside,
                usePlatformDefaultWidth = false,
            ),
    ) {
        CompositionLocalProvider(LocalContentColor provides tokens.popoverForeground) {
            Column(
                modifier =
                    modifier
                        .widthIn(max = DialogMaxWidth)
                        .border(DialogBorderWidth, tokens.border, shape)
                        .clip(shape)
                        .background(tokens.popover)
                        .padding(spacing.s6),
                verticalArrangement = Arrangement.spacedBy(spacing.s4),
                content = content,
            )
        }
    }
}

/** shadcn `DialogTitle` — the leading heading of a [Dialog]. */
@Composable
fun DialogTitle(text: String, modifier: Modifier = Modifier) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    Text(
        text = text,
        modifier = modifier,
        style = typography.lg.copy(fontWeight = FontWeight.SemiBold),
        color = tokens.popoverForeground,
    )
}

/** shadcn `DialogDescription` — the muted supporting line under a [DialogTitle]. */
@Composable
fun DialogDescription(text: String, modifier: Modifier = Modifier) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    Text(text = text, modifier = modifier, style = typography.sm, color = tokens.mutedForeground)
}

/** shadcn `DialogFooter` — right-aligned action row (Cancel · Confirm). */
@Composable
fun DialogFooter(modifier: Modifier = Modifier, content: @Composable RowScope.() -> Unit) {
    val spacing: Spacing = LocalSpacing.current
    Row(
        modifier = modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2, Alignment.End),
        verticalAlignment = Alignment.CenterVertically,
        content = content,
    )
}

/**
 * shadcn confirm/notice dialog with the **same call signature as Material3's `AlertDialog`**
 * (`onDismissRequest` · `confirmButton` · `dismissButton` · `title` · `text`), rendered on the DS
 * [Dialog] card. Call sites migrating off `androidx.compose.material3.AlertDialog` only need an
 * import swap. For destructive confirms prefer the higher-level [ConfirmDialog].
 */
@Composable
fun AlertDialog(
    onDismissRequest: () -> Unit,
    confirmButton: @Composable () -> Unit,
    modifier: Modifier = Modifier,
    dismissButton: (@Composable () -> Unit)? = null,
    title: (@Composable () -> Unit)? = null,
    text: (@Composable () -> Unit)? = null,
) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    Dialog(onDismissRequest = onDismissRequest, modifier = modifier) {
        if (title != null) {
            CompositionLocalProvider(
                LocalTextStyle provides typography.lg.copy(fontWeight = FontWeight.SemiBold),
                LocalContentColor provides tokens.popoverForeground,
            ) {
                title()
            }
        }
        if (text != null) {
            CompositionLocalProvider(
                LocalTextStyle provides typography.sm,
                LocalContentColor provides tokens.mutedForeground,
            ) {
                text()
            }
        }
        DialogFooter {
            if (dismissButton != null) dismissButton()
            confirmButton()
        }
    }
}
