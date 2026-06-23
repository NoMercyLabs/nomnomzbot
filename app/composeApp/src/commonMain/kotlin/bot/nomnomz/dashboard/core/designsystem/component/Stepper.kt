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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// The `Stepper` design-system component (frontend-design-system.catalogue.md). A pure, token-driven,
// stateless progress indicator for multi-step flows: numbered/labeled indicators joined by a connector
// line, each rendered in its completed / current / upcoming state. The host owns the step model and the
// current index; this component only paints them. Style comes from [resolve] over the tokens — never a
// raw hex/dp.

/** One step shown in the [Stepper]: its short [label] and its [state] relative to the current position. */
data class StepperStep(
    val label: String,
    val state: StepState,
)

/** A step's position relative to where the flow currently is — the closed visual-state set for a step. */
enum class StepState {
    /** A step the user has already passed (a completed indicator). */
    Completed,

    /** The step the user is on right now (the emphasized indicator). */
    Current,

    /** A step the user has not reached yet (a muted indicator). */
    Upcoming,
}

/** Which way the steps are laid out — the catalogue's `orientation` variant. */
enum class StepperOrientation {
    Horizontal,
    Vertical,
}

/** The resolved colors for one step's indicator + label + the connector leading out of it. */
private data class StepperStyle(
    val indicator: Color,
    val indicatorForeground: Color,
    val indicatorBorder: Color,
    val label: Color,
    val connector: Color,
)

/** The catalogue `resolve(variant, size, state, tokens)` lookup — maps a [StepState] to its on-token colors. */
private fun resolve(state: StepState, tokens: Tokens): StepperStyle =
    when (state) {
        StepState.Completed ->
            StepperStyle(
                indicator = tokens.primary,
                indicatorForeground = tokens.primaryForeground,
                indicatorBorder = tokens.primary,
                label = tokens.foreground,
                connector = tokens.primary,
            )

        StepState.Current ->
            StepperStyle(
                indicator = tokens.background,
                indicatorForeground = tokens.primary,
                indicatorBorder = tokens.primary,
                label = tokens.foreground,
                connector = tokens.border,
            )

        StepState.Upcoming ->
            StepperStyle(
                indicator = tokens.muted,
                indicatorForeground = tokens.mutedForeground,
                indicatorBorder = tokens.border,
                label = tokens.mutedForeground,
                connector = tokens.border,
            )
    }

/**
 * Render [steps] as a stepper. [orientation] picks the layout; the indicator shows the 1-based step number
 * (or a check glyph once completed) and the [StepperStep.label] sits beneath (horizontal) or beside
 * (vertical) it. Connectors join adjacent steps and take the colour of the step they lead out of.
 */
@Composable
fun Stepper(
    steps: List<StepperStep>,
    modifier: Modifier = Modifier,
    orientation: StepperOrientation = StepperOrientation.Horizontal,
) {
    when (orientation) {
        StepperOrientation.Horizontal -> HorizontalStepper(steps = steps, modifier = modifier)
        StepperOrientation.Vertical -> VerticalStepper(steps = steps, modifier = modifier)
    }
}

@Composable
private fun HorizontalStepper(steps: List<StepperStep>, modifier: Modifier) {
    val tokens: Tokens = LocalTokens.current

    Row(
        modifier = modifier.fillMaxWidth(),
        verticalAlignment = Alignment.Top,
    ) {
        steps.forEachIndexed { index, step ->
            val style: StepperStyle = resolve(step.state, tokens)
            StepColumn(
                number = index + 1,
                step = step,
                style = style,
                modifier = Modifier.weight(1f),
            )
            if (index < steps.lastIndex) {
                Connector(color = style.connector)
            }
        }
    }
}

@Composable
private fun StepColumn(
    number: Int,
    step: StepperStep,
    style: StepperStyle,
    modifier: Modifier,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = modifier,
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Indicator(number = number, completed = step.state == StepState.Completed, style = style)
        Text(
            text = step.label,
            style = typography.xs,
            color = style.label,
            textAlign = TextAlign.Center,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun VerticalStepper(steps: List<StepperStep>, modifier: Modifier) {
    val tokens: Tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(modifier = modifier) {
        steps.forEachIndexed { index, step ->
            val style: StepperStyle = resolve(step.state, tokens)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Indicator(number = index + 1, completed = step.state == StepState.Completed, style = style)
                Text(
                    text = step.label,
                    style = typography.sm,
                    color = style.label,
                    modifier = Modifier.padding(start = spacing.s2),
                )
            }
        }
    }
}

@Composable
private fun Indicator(number: Int, completed: Boolean, style: StepperStyle) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier =
            Modifier
                .size(spacing.s6)
                .background(style.indicator, CircleShape)
                .border(width = spacing.s0_5 / 2, color = style.indicatorBorder, shape = CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = if (completed) CHECK_GLYPH else number.toString(),
            style = typography.xs,
            color = style.indicatorForeground,
        )
    }
}

@Composable
private fun Connector(color: Color) {
    val spacing = LocalSpacing.current
    // Sits on the indicator's vertical centre (half the indicator height) so the line joins circle-to-circle.
    Box(
        modifier =
            Modifier
                .padding(top = spacing.s3)
                .width(spacing.s6)
                .height(spacing.s0_5 / 2)
                .background(color),
    )
}

// A check mark for a completed step; the icon pack (IconKey/IconSet) wires in with the resources slice, so
// the indicator shows this glyph until then rather than a raw drawable.
private const val CHECK_GLYPH: String = "✓"
