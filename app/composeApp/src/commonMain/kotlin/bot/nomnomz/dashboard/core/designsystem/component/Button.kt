// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.focusable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import bot.nomnomz.dashboard.core.designsystem.theme.ControlPalette
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

private val ButtonStroke: Dp = 1.dp
private val ButtonFocusStroke: Dp = 3.dp
private val ButtonRadius: Dp = 45.dp
private val CompactButtonHeight: Dp = 44.dp

/** Figma pill height: 24px glyph row + 12px vertical padding top & bottom. */
private val FigmaButtonHeight: Dp = 48.dp
private const val ButtonTransitionMillis: Int = 120

enum class ButtonVariant {
    /** Figma node 308:1607 — light primary pill. */
    Default,
    /** Figma node 391:1787 — solid destructive pill. */
    Destructive,
    Outline,
    /** Figma node 310:1526 — dark secondary pill. */
    Secondary,
    /** Figma node 391:1892 — quiet destructive pill. */
    DestructiveSecondary,
    Ghost,
    /** Ghost sibling for destructive icon actions: transparent at rest, red tint on hover/press (shadcn `hover:bg-destructive/10`). */
    DestructiveGhost,
    Link,
}

enum class ButtonSize {
    Sm,
    Default,
    Lg,
    Icon,
}

internal val DefaultButtonVariant: ButtonVariant = ButtonVariant.Default

private data class ButtonVisuals(
    val gradientTop: Color,
    val gradientBottom: Color,
    val content: Color,
    val border: Color,
    val focus: Color,
    val weight: FontWeight,
)

private fun buttonVisuals(
    variant: ButtonVariant,
    hovered: Boolean,
    pressed: Boolean,
    tokens: Tokens,
): ButtonVisuals =
    when (variant) {
        ButtonVariant.Default -> {
            val top: Color
            val bottom: Color
            when {
                pressed -> {
                    top = Color(0xFFE3E0FF)
                    bottom = ControlPalette.White
                }
                hovered -> {
                    top = ControlPalette.White.copy(alpha = 0.88f)
                    bottom = ControlPalette.LilacWhite.copy(alpha = 0.88f)
                }
                else -> {
                    top = ControlPalette.White
                    bottom = ControlPalette.LilacWhite
                }
            }
            ButtonVisuals(
                gradientTop = top,
                gradientBottom = bottom,
                content = ControlPalette.Ink,
                border = Color(0x331A2E22),
                focus = ControlPalette.PrimaryFocus,
                weight = FontWeight.ExtraBold,
            )
        }
        ButtonVariant.Secondary -> {
            val fill = when {
                pressed -> Color.Black
                hovered -> ControlPalette.SurfaceHover
                else -> ControlPalette.SurfaceRaised
            }
            ButtonVisuals(
                gradientTop = fill,
                gradientBottom = fill,
                content = ControlPalette.LilacWhite,
                border = if (pressed) ControlPalette.SurfaceRaised else Color(0x33000000),
                focus = ControlPalette.LilacWhite.copy(alpha = 0.64f),
                weight = FontWeight.SemiBold,
            )
        }
        ButtonVariant.Destructive -> {
            val overlay = when {
                pressed -> 0.32f
                hovered -> 0f
                else -> 0.16f
            }
            val fill = ControlPalette.Destructive.blend(Color.Black, overlay)
            ButtonVisuals(
                gradientTop = fill,
                gradientBottom = fill,
                content = ControlPalette.LilacWhite,
                border = Color(0x331B1B1B),
                focus = ControlPalette.LilacWhite,
                weight = FontWeight.ExtraBold,
            )
        }
        ButtonVariant.DestructiveSecondary -> {
            val opacity = when {
                pressed -> 0.26f
                hovered -> 0.20f
                else -> 0.12f
            }
            val fill = ControlPalette.DestructiveTint.copy(alpha = opacity)
            ButtonVisuals(
                gradientTop = fill,
                gradientBottom = fill,
                content = ControlPalette.DestructiveContent,
                border = if (pressed) ControlPalette.SurfaceRaised else Color(0x33000000),
                focus = ControlPalette.DestructiveContent.copy(alpha = 0.64f),
                weight = FontWeight.SemiBold,
            )
        }
        ButtonVariant.Outline -> {
            val fill = if (hovered || pressed) tokens.accent else Color.Transparent
            ButtonVisuals(
                gradientTop = fill,
                gradientBottom = fill,
                content = if (hovered || pressed) tokens.accentForeground else tokens.foreground,
                border = tokens.border,
                focus = tokens.ring,
                weight = FontWeight.SemiBold,
            )
        }
        ButtonVariant.Ghost -> {
            val fill =
                when {
                    pressed -> ControlPalette.White.copy(alpha = 0.16f)
                    hovered -> ControlPalette.White.copy(alpha = 0.10f)
                    else -> Color.Transparent
                }
            ButtonVisuals(
                gradientTop = fill,
                gradientBottom = fill,
                content = if (hovered || pressed) ControlPalette.White else tokens.foreground,
                border = Color.Transparent,
                focus = ControlPalette.LilacWhite.copy(alpha = 0.64f),
                weight = FontWeight.SemiBold,
            )
        }
        ButtonVariant.DestructiveGhost -> {
            // Transparent at rest like Ghost, but the hover/press wash is the destructive red (shadcn
            // `hover:bg-destructive/10`) so a delete action reads as destructive before it's clicked.
            val fill =
                when {
                    pressed -> ControlPalette.DestructiveTint.copy(alpha = 0.20f)
                    hovered -> ControlPalette.DestructiveTint.copy(alpha = 0.12f)
                    else -> Color.Transparent
                }
            ButtonVisuals(
                gradientTop = fill,
                gradientBottom = fill,
                content = ControlPalette.DestructiveContent,
                border = Color.Transparent,
                focus = ControlPalette.DestructiveContent.copy(alpha = 0.64f),
                weight = FontWeight.SemiBold,
            )
        }
        ButtonVariant.Link ->
            ButtonVisuals(
                gradientTop = Color.Transparent,
                gradientBottom = Color.Transparent,
                content = tokens.primary,
                border = Color.Transparent,
                focus = tokens.ring,
                weight = FontWeight.SemiBold,
            )
    }

private data class ButtonDimensions(
    /** Vertical padding = the icon's square inset (Sleak `--pad-y`); icon-side horizontal matches it. */
    val padSquare: Dp,
    /** Open/text-end horizontal inset ≈ 2× [padSquare] so the label breathes (Sleak uneven rule). */
    val padText: Dp,
    val minHeight: Dp,
)

// Sleak button padding (references/components.md §Buttons): the icon side equals the vertical
// padding (framing the glyph in a square), the open text end takes ~2× that. The icon-only size is
// the Figma pill (`px-[24px] py-[12px]`) — symmetric, so [padText] is used on both horizontal sides.
private fun buttonDimensions(size: ButtonSize, spacing: Spacing): ButtonDimensions =
    when (size) {
        ButtonSize.Sm -> ButtonDimensions(spacing.s2, spacing.s4, CompactButtonHeight)
        ButtonSize.Default -> ButtonDimensions(spacing.s3, spacing.s6, FigmaButtonHeight)
        ButtonSize.Lg -> ButtonDimensions(spacing.s3, spacing.s6, FigmaButtonHeight)
        ButtonSize.Icon -> ButtonDimensions(spacing.s3, spacing.s6, FigmaButtonHeight)
    }

/**
 * Resolves the two horizontal insets from icon presence per the Sleak rule:
 * a side that carries an icon collapses to the square inset; an open text end takes [padText].
 * The icon-only size keeps symmetric [padText] on both sides (the Figma pill, not a square).
 */
private fun ButtonDimensions.horizontalInsets(
    iconOnly: Boolean,
    hasLeftIcon: Boolean,
    hasRightIcon: Boolean,
): Pair<Dp, Dp> {
    if (iconOnly) return padText to padText
    val start: Dp = if (hasLeftIcon) padSquare else padText
    val end: Dp = if (hasRightIcon) padSquare else padText
    return start to end
}

/**
 * Stateful implementation of the four Figma button families. Hover, press, keyboard focus,
 * loading, icon-only, and leading/trailing-icon states are all represented by the same API.
 */
@Composable
fun Button(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    variant: ButtonVariant = DefaultButtonVariant,
    size: ButtonSize = ButtonSize.Default,
    enabled: Boolean = true,
    loading: Boolean = false,
    leftIcon: @Composable (() -> Unit)? = null,
    rightIcon: @Composable (() -> Unit)? = null,
    content: @Composable RowScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val interactionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val pressed: Boolean by interactionSource.collectIsPressedAsState()
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val interactive = enabled && !loading
    val target = buttonVisuals(variant, hovered && interactive, pressed && interactive, tokens)
    val top: Color by animateColorAsState(target.gradientTop, tween(ButtonTransitionMillis), label = "buttonTop")
    val bottom: Color by animateColorAsState(target.gradientBottom, tween(ButtonTransitionMillis), label = "buttonBottom")
    val contentColor: Color by animateColorAsState(target.content, tween(ButtonTransitionMillis), label = "buttonContent")
    val borderColor: Color by animateColorAsState(
        if (focused) target.focus else target.border,
        tween(ButtonTransitionMillis),
        label = "buttonBorder",
    )
    val dimensions = buttonDimensions(size, spacing)
    val (startPad: Dp, endPad: Dp) =
        dimensions.horizontalInsets(
            iconOnly = size == ButtonSize.Icon,
            hasLeftIcon = leftIcon != null,
            hasRightIcon = rightIcon != null,
        )
    val shape = RoundedCornerShape(ButtonRadius)
    val textStyle =
        typography.base.copy(
            color = contentColor,
            fontWeight = target.weight,
            fontSize = 17.sp,
            lineHeight = 22.sp,
            letterSpacing = 0.51.sp,
            textDecoration =
                if (variant == ButtonVariant.Link && hovered) TextDecoration.Underline
                else TextDecoration.None,
        )
    val sizeModifier = Modifier.defaultMinSize(minHeight = dimensions.minHeight)

    CompositionLocalProvider(
        LocalTextStyle provides textStyle,
        LocalContentColor provides contentColor,
    ) {
        Row(
            modifier =
                modifier
                    .then(sizeModifier)
                    .then(if (!enabled) Modifier.alpha(0.48f) else Modifier)
                    .border(
                        width = if (focused) ButtonFocusStroke else ButtonStroke,
                        color = borderColor,
                        shape = shape,
                    )
                    .clip(shape)
                    .background(Brush.verticalGradient(listOf(top, bottom)))
                    .hoverable(interactionSource, enabled = interactive)
                    .focusable(enabled = interactive, interactionSource = interactionSource)
                    .clickable(
                        interactionSource = interactionSource,
                        indication = null,
                        enabled = interactive,
                        onClick = onClick,
                    )
                    .pointerHoverIcon(if (interactive) PointerIcon.Hand else PointerIcon.Default)
                    .padding(
                        start = startPad,
                        end = endPad,
                        top = dimensions.padSquare,
                        bottom = dimensions.padSquare,
                    ),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2, Alignment.CenterHorizontally),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (loading) {
                Spinner(size = SpinnerSize.Lg, color = contentColor)
            } else {
                leftIcon?.let { ButtonIconSlot(it) }
                content()
                rightIcon?.let { ButtonIconSlot(it) }
            }
        }
    }
}

@Composable
private fun ButtonIconSlot(content: @Composable () -> Unit) {
    Box(
        modifier = Modifier.size(24.dp),
        contentAlignment = Alignment.Center,
    ) {
        content()
    }
}

@Composable
fun TextButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    loading: Boolean = false,
    content: @Composable RowScope.() -> Unit,
) =
    Button(
        onClick = onClick,
        modifier = modifier,
        variant = ButtonVariant.Ghost,
        size = ButtonSize.Sm,
        enabled = enabled,
        loading = loading,
        content = content,
    )

@Composable
fun OutlinedButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    loading: Boolean = false,
    content: @Composable RowScope.() -> Unit,
) =
    Button(
        onClick = onClick,
        modifier = modifier,
        variant = ButtonVariant.Outline,
        size = ButtonSize.Sm,
        enabled = enabled,
        loading = loading,
        content = content,
    )

private fun Color.blend(other: Color, fraction: Float): Color =
    Color(
        red = red + (other.red - red) * fraction,
        green = green + (other.green - green) * fraction,
        blue = blue + (other.blue - blue) * fraction,
        alpha = alpha + (other.alpha - alpha) * fraction,
    )
