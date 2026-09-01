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
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// A readability cap on the toast width — a layout constraint (like Button's CompactButtonHeight),
// not a spacing-scale token — so a long message doesn't stretch edge-to-edge on a wide window.
private val MaxToastWidth: Dp = 480.dp

/** shadcn Toast variants (frontend-design-system.md §4, catalogue row — modeled on Sonner). */
enum class ToastVariant {
    Default,
    Destructive,
}

private data class ToastColors(val container: Color, val content: Color)

private fun resolveToastColors(variant: ToastVariant, tokens: Tokens): ToastColors =
    when (variant) {
        ToastVariant.Default -> ToastColors(tokens.primary, tokens.primaryForeground)
        ToastVariant.Destructive -> ToastColors(tokens.destructive, tokens.destructiveForeground)
    }

/**
 * shadcn/ui Toast ported to Compose (frontend-design-system.md §4, catalogue row) — Foundation
 * (`Popup`-hosted by the caller), modeled on shadcn's Sonner since no JS toast lib is ported. This
 * primitive renders one toast's body; enter/visible/exit is the caller's `AnimatedVisibility`
 * transition (see [FeedbackHost][bot.nomnomz.dashboard.core.feedback.FeedbackHost], the app-shell
 * host that queues + auto-dismisses toasts built on this primitive) — a static primitive has no
 * animation state of its own to assert on.
 *
 * @param text the toast message body.
 * @param dismissLabel accessible label + visible text for the dismiss action.
 * @param onDismiss invoked when the dismiss control is activated.
 */
@Composable
fun Toast(
    text: String,
    dismissLabel: String,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier,
    variant: ToastVariant = ToastVariant.Default,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current
    val colors: ToastColors = resolveToastColors(variant, tokens)

    Row(
        modifier =
            modifier
                .widthIn(max = MaxToastWidth)
                .clip(RoundedCornerShape(tokens.radius.lg))
                .background(colors.container)
                // Assertive so the outcome is announced immediately, regardless of which page is
                // focused — a toast is out-of-band feedback, not part of the reading order.
                .semantics { liveRegion = LiveRegionMode.Assertive }
                .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = text,
            style = typography.sm,
            color = colors.content,
            maxLines = 3,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(end = spacing.s1),
        )
        Text(
            text = dismissLabel,
            style = typography.sm,
            color = colors.content,
            maxLines = 1,
            modifier =
                Modifier
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .clickable(onClick = onDismiss)
                    .semantics { contentDescription = dismissLabel }
                    .padding(horizontal = spacing.s2, vertical = spacing.s1),
        )
    }
}
