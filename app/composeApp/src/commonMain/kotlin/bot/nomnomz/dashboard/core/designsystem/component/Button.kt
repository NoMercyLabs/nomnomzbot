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
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// 1dp is a border stroke constant — not a layout spacing value and intentionally not in Space.*.
private val StrokeWidth: Dp = 1.dp

// ─── Variant / size enums ─────────────────────────────────────────────────────

/** shadcn Button variants (frontend-design-system.md §4, catalogue row). */
enum class ButtonVariant {
    Default,
    Destructive,
    Outline,
    Secondary,
    Ghost,
    Link,
}

/** shadcn Button sizes (frontend-design-system.md §4, catalogue row). */
enum class ButtonSize {
    Sm,
    Default,
    Lg,
    Icon,
}

// ─── Style resolution ─────────────────────────────────────────────────────────

private data class ButtonColors(
    val container: Color,
    val content: Color,
    val border: Color,
)

private fun resolveButtonColors(
    variant: ButtonVariant,
    enabled: Boolean,
    hovered: Boolean,
    pressed: Boolean,
    tokens: Tokens,
): ButtonColors {
    if (!enabled) {
        return ButtonColors(
            container = tokens.muted,
            content = tokens.mutedForeground,
            border = Color.Transparent,
        )
    }
    // Hover/press darken the container slightly by blending with black (light theme) or
    // white (dark theme). Since we have no "is dark scheme" param here, we use alpha: a
    // semi-transparent black overlay at 8 % (hover) / 14 % (press) reads correctly on
    // both schemes because the tokens already encode the right luminance.
    val overlay: Float =
        when {
            pressed -> 0.14f
            hovered -> 0.08f
            else -> 0f
        }

    return when (variant) {
        ButtonVariant.Default ->
            ButtonColors(
                container = tokens.primary.blend(Color.Black, overlay),
                content = tokens.primaryForeground,
                border = Color.Transparent,
            )
        ButtonVariant.Destructive ->
            ButtonColors(
                container = tokens.destructive.blend(Color.Black, overlay),
                content = tokens.destructiveForeground,
                border = Color.Transparent,
            )
        ButtonVariant.Outline ->
            ButtonColors(
                container = if (hovered || pressed) tokens.accent else Color.Transparent,
                content = if (hovered || pressed) tokens.accentForeground else tokens.foreground,
                border = tokens.border,
            )
        ButtonVariant.Secondary ->
            ButtonColors(
                container = tokens.secondary.blend(Color.Black, overlay),
                content = tokens.secondaryForeground,
                border = Color.Transparent,
            )
        ButtonVariant.Ghost ->
            ButtonColors(
                container = if (hovered || pressed) tokens.accent else Color.Transparent,
                content = if (hovered || pressed) tokens.accentForeground else tokens.foreground,
                border = Color.Transparent,
            )
        ButtonVariant.Link ->
            ButtonColors(
                container = Color.Transparent,
                content = tokens.primary,
                border = Color.Transparent,
            )
    }
}

private data class ButtonDimensions(
    val paddingH: Dp,
    val paddingV: Dp,
    val minWidth: Dp,
    val minHeight: Dp,
    val fixedSize: Dp?,
)

private fun resolveButtonDimensions(size: ButtonSize, spacing: Spacing): ButtonDimensions =
    when (size) {
        // sm  h-8=32dp  px-3=12dp  py=~6dp
        ButtonSize.Sm ->
            ButtonDimensions(
                paddingH = spacing.s3,
                paddingV = spacing.s1_5,
                minWidth = spacing.s0,
                minHeight = spacing.s8,
                fixedSize = null,
            )
        // default  h-9≈36dp  px-4=16dp  py-2=8dp
        ButtonSize.Default ->
            ButtonDimensions(
                paddingH = spacing.s4,
                paddingV = spacing.s2,
                minWidth = spacing.s0,
                minHeight = spacing.s8,
                fixedSize = null,
            )
        // lg  h-10≈40dp  px-8=32dp
        ButtonSize.Lg ->
            ButtonDimensions(
                paddingH = spacing.s8,
                paddingV = spacing.s2,
                minWidth = spacing.s0,
                minHeight = spacing.s8,
                fixedSize = null,
            )
        // icon  h-9 w-9 ≈ 32dp square, no text padding
        ButtonSize.Icon ->
            ButtonDimensions(
                paddingH = spacing.s0,
                paddingV = spacing.s0,
                minWidth = spacing.s0,
                minHeight = spacing.s0,
                fixedSize = spacing.s8,
            )
    }

// ─── Primary composable ───────────────────────────────────────────────────────

/**
 * shadcn/ui Button ported to Compose (frontend-design-system.md §4).
 *
 * Foundation-based (no Material ripple, no elevation). Reads all colors and sizes from the
 * [LocalTokens] / [LocalSpacing] / [LocalTypography] providers. Use [ButtonVariant] and
 * [ButtonSize] to match shadcn's exact variant/size surface — never override inline.
 */
@Composable
fun Button(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    variant: ButtonVariant = ButtonVariant.Default,
    size: ButtonSize = ButtonSize.Default,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val pressed: Boolean by interactionSource.collectIsPressedAsState()

    val colors: ButtonColors = resolveButtonColors(variant, enabled, hovered, pressed, tokens)
    val dims: ButtonDimensions = resolveButtonDimensions(size, spacing)

    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)

    // Build a text style that callers' Text() composables will inherit via M3's LocalTextStyle.
    val textStyle =
        typography.sm.copy(
            fontWeight = FontWeight.Medium,
            color = colors.content,
            textDecoration =
                if (variant == ButtonVariant.Link && hovered) TextDecoration.Underline
                else TextDecoration.None,
        )

    val sizeModifier: Modifier =
        if (dims.fixedSize != null) Modifier.size(dims.fixedSize)
        else Modifier.defaultMinSize(minHeight = dims.minHeight)

    // Provide both LocalTextStyle (consumed by Text) and LocalContentColor (consumed by Icon)
    // so the button content inherits the correct foreground color without any inline styling.
    CompositionLocalProvider(
        LocalTextStyle provides textStyle,
        LocalContentColor provides colors.content,
    ) {
        Row(
            modifier =
                modifier
                    .then(sizeModifier)
                    .then(
                        if (colors.border != Color.Transparent)
                            Modifier.border(StrokeWidth, colors.border, shape)
                        else Modifier
                    )
                    .clip(shape)
                    .background(colors.container)
                    .hoverable(interactionSource)
                    .clickable(
                        interactionSource = interactionSource,
                        indication = null,
                        enabled = enabled,
                        onClick = onClick,
                    )
                    .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default)
                    .then(
                        if (dims.fixedSize == null)
                            Modifier.padding(
                                horizontal = dims.paddingH,
                                vertical = dims.paddingV,
                            )
                        else Modifier
                    ),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
            content = content,
        )
    }
}

// ─── Shorthands — same API as Material3's TextButton / OutlinedButton ─────────

/**
 * Ghost-variant [Button] with the same call signature as Material3's `TextButton` so call sites
 * only need an import swap.
 */
@Composable
fun TextButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit,
) =
    Button(
        onClick = onClick,
        modifier = modifier,
        variant = ButtonVariant.Ghost,
        enabled = enabled,
        content = content,
    )

/**
 * Outline-variant [Button] with the same call signature as Material3's `OutlinedButton` so call
 * sites only need an import swap.
 */
@Composable
fun OutlinedButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit,
) =
    Button(
        onClick = onClick,
        modifier = modifier,
        variant = ButtonVariant.Outline,
        enabled = enabled,
        content = content,
    )

// ─── Internal helpers ─────────────────────────────────────────────────────────

/** Linearly blends [this] color toward [other] by [fraction] (0 = no change, 1 = full other). */
private fun Color.blend(other: Color, fraction: Float): Color {
    if (fraction == 0f) return this
    return Color(
        red = red + (other.red - red) * fraction,
        green = green + (other.green - green) * fraction,
        blue = blue + (other.blue - blue) * fraction,
        alpha = alpha,
    )
}
