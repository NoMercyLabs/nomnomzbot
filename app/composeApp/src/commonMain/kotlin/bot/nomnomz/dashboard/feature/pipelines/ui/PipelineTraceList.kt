// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PipelineTraceStep
import bot.nomnomz.dashboard.core.network.PipelineTraceVariableChange
import bot.nomnomz.dashboard.core.network.RuntimePalette
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_branch
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_change
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_empty
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_error
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_iterations
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_not_set
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_trace_output
import org.jetbrains.compose.resources.stringResource

/**
 * What a pipeline test run did, one row per executed step, in run order (S-PIPE-TEST-TRACE): the step, the arm an
 * `if` took, how often a `loop` ran, each variable change as before -> after, the step's output, and a failed
 * step's error on its own row. Rows are separated by a [Separator] — no nested rounded containers.
 */
@Composable
fun PipelineTraceList(trace: List<PipelineTraceStep>, palette: RuntimePalette) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    if (trace.isEmpty()) {
        Text(
            text = stringResource(Res.string.pipelines_testrun_trace_empty),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        trace.forEachIndexed { index: Int, step: PipelineTraceStep ->
            if (index > 0) Separator()
            TraceRow(step, palette)
        }
    }
}

@Composable
private fun TraceRow(step: PipelineTraceStep, palette: RuntimePalette) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = blockDisplayName(palette.action(step.stepType) ?: palette.condition(step.stepType), step.stepType),
                style = typography.sm, color = tokens.foreground)
            step.branch?.takeIf { it.isNotBlank() }?.let { branch: String ->
                Badge(variant = BadgeVariant.Outline) {
                    Text(text = stringResource(Res.string.pipelines_testrun_trace_branch, branch))
                }
            }
        }
        step.iterations?.let { count: Int ->
            Text(
                text = stringResource(Res.string.pipelines_testrun_trace_iterations, count),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        step.variableChanges.forEach { change: PipelineTraceVariableChange ->
            val notSet: String = stringResource(Res.string.pipelines_testrun_trace_not_set)
            Text(
                text =
                    stringResource(
                        Res.string.pipelines_testrun_trace_change,
                        change.key,
                        change.before ?: notSet,
                        change.after ?: notSet,
                    ),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        step.output?.takeIf { it.isNotBlank() }?.let { output: String ->
            Text(
                text = stringResource(Res.string.pipelines_testrun_trace_output, output),
                style = typography.xs,
                color = tokens.foreground,
            )
        }
        step.error?.takeIf { it.isNotBlank() }?.let { error: String ->
            Text(
                text = stringResource(Res.string.pipelines_testrun_trace_error, error),
                style = typography.xs,
                color = tokens.destructive,
            )
        }
    }
}
