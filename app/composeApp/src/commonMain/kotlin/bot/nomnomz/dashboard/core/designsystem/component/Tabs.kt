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
import androidx.compose.foundation.focusable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.LocalTextStyle
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import bot.nomnomz.dashboard.core.designsystem.theme.ControlPalette
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

private val TrackHeight: Dp = 52.dp
private val TrackPadding: Dp = 6.dp
private val TrackRadius: Dp = 53.dp
private val SegmentHeight: Dp = 40.dp
private val SegmentRadius: Dp = 32.dp
private val SegmentFocusWidth: Dp = 2.dp
private const val SegmentTransitionMillis: Int = 120

private data class SegmentEntry(
    val focusRequester: FocusRequester,
    val select: () -> Unit,
    val enabled: () -> Boolean,
)

private class SegmentedControlState {
    private val entries = mutableListOf<SegmentEntry>()

    fun register(entry: SegmentEntry) { entries += entry }
    fun unregister(entry: SegmentEntry) { entries -= entry }

    fun moveFrom(entry: SegmentEntry, direction: Int): Boolean {
        if (entries.size < 2) return false
        val current = entries.indexOf(entry)
        if (current < 0) return false
        for (offset in 1 until entries.size) {
            val target = entries[(current + direction * offset).mod(entries.size)]
            if (target.enabled()) {
                target.select()
                target.focusRequester.requestFocus()
                return true
            }
        }
        return false
    }
}

private val LocalSegmentedControlState = staticCompositionLocalOf<SegmentedControlState?> { null }

/** Figma node 363:2202 — neutral 52px pill track with 6px inset segments. */
@Composable
fun TabsList(modifier: Modifier = Modifier, content: @Composable RowScope.() -> Unit) {
    val state = remember { SegmentedControlState() }
    val scrollState = rememberScrollState()
    val shape = RoundedCornerShape(TrackRadius)
    CompositionLocalProvider(LocalSegmentedControlState provides state) {
        Row(
            modifier =
                modifier
                    .defaultMinSize(minHeight = TrackHeight)
                    .selectableGroup()
                    .border(1.dp, ControlPalette.Border, shape)
                    .clip(shape)
                    .background(ControlPalette.Surface)
                    // Preserve the single concentric segmented-control track while allowing long
                    // or localized option sets to remain usable below the shell's compact breakpoint.
                    .horizontalScroll(scrollState)
                    .padding(TrackPadding),
            horizontalArrangement = Arrangement.Start,
            verticalAlignment = Alignment.CenterVertically,
            content = content,
        )
    }
}

/** One content-sized segment inside [TabsList]; callers can size the list responsively. */
@Composable
fun RowScope.TabsTrigger(
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit,
) {
    val typography = LocalTypography.current
    val segmentedState = LocalSegmentedControlState.current
    val interactionSource = remember { MutableInteractionSource() }
    val focusRequester = remember { FocusRequester() }
    val currentOnClick = rememberUpdatedState(onClick)
    val currentEnabled = rememberUpdatedState(enabled)
    val entry = remember(focusRequester) {
        SegmentEntry(
            focusRequester = focusRequester,
            select = { currentOnClick.value() },
            enabled = { currentEnabled.value },
        )
    }
    DisposableEffect(segmentedState, entry) {
        segmentedState?.register(entry)
        onDispose { segmentedState?.unregister(entry) }
    }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val contentTarget =
        when {
            !enabled -> ControlPalette.Inactive.copy(alpha = 0.48f)
            selected -> ControlPalette.White
            hovered -> ControlPalette.LilacWhite.copy(alpha = 0.72f)
            else -> ControlPalette.Inactive
        }
    val contentColor: Color by
        animateColorAsState(contentTarget, tween(SegmentTransitionMillis), label = "segmentContent")
    val shape = RoundedCornerShape(SegmentRadius)

    CompositionLocalProvider(
        LocalTextStyle provides
            typography.base.copy(
                color = contentColor,
                fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                fontSize = 16.sp,
                lineHeight = 20.sp,
                letterSpacing = 0.48.sp,
            ),
        LocalContentColor provides contentColor,
    ) {
        Box(
            modifier =
                modifier
                    .height(SegmentHeight)
                    .focusRequester(focusRequester)
                    .onPreviewKeyEvent { event ->
                        if (event.type != KeyEventType.KeyDown) return@onPreviewKeyEvent false
                        when (event.key) {
                            Key.DirectionRight, Key.DirectionDown -> segmentedState?.moveFrom(entry, 1) == true
                            Key.DirectionLeft, Key.DirectionUp -> segmentedState?.moveFrom(entry, -1) == true
                            else -> false
                        }
                    }
                    .clip(shape)
                    .then(
                        if (selected) {
                            Modifier
                                .background(
                                    Brush.verticalGradient(
                                        listOf(Color(0xFF2A2A2A), ControlPalette.SurfaceRaised)
                                    )
                                )
                                .border(1.dp, ControlPalette.White.copy(alpha = 0.08f), shape)
                        } else {
                            Modifier.background(Color.Transparent)
                        }
                    )
                    .then(
                        if (focused) Modifier.border(SegmentFocusWidth, ControlPalette.Focus, shape)
                        else Modifier
                    )
                    .hoverable(interactionSource, enabled = enabled)
                    .focusable(enabled = enabled, interactionSource = interactionSource)
                    .selectable(
                        selected = selected,
                        interactionSource = interactionSource,
                        indication = null,
                        enabled = enabled,
                        role = Role.Tab,
                        onClick = onClick,
                    )
                    .pointerHoverIcon(if (enabled) PointerIcon.Hand else PointerIcon.Default),
            contentAlignment = Alignment.Center,
        ) {
            Row(
                modifier = Modifier.padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically,
                content = content,
            )
        }
    }
}
