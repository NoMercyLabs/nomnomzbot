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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.PlayCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TestRunResult
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipeline_test_action
import nomnomzbot.composeapp.generated.resources.pipeline_test_disabled_reason
import nomnomzbot.composeapp.generated.resources.pipelines_effect_row_type
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_chat_empty
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_chat_heading
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_close
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_effects_empty
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_effects_heading
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_error
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_failed
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_meta
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_ok
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_run
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_running
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_subtitle
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_title
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_vars_label

// The S047 dry-run dialog, extracted (S047-remaining) so every pipeline-binding surface — the Pipelines editor
// itself, commands, event responses, timers — shows the SAME dialog for the SAME backend call
// (`POST pipelines/{id}/test-run`): sample variables (key=value lines) + a Run button. Nothing the pipeline does
// here reaches a real surface — reads/conditions/variable math run for real, side effects don't. Callers own the
// running/result/error state (see [bot.nomnomz.dashboard.feature.pipelines.state.PipelineTestRunController]) and
// the [onRun] wiring to the bound pipeline id; this composable only renders.
/**
 * The Test action every pipeline-binding surface renders next to its bind picker (S047-remaining) — commands,
 * event responses, timers. Disabled (never hidden) until [pipelineId] is actually bound, via the shared
 * [ManageGate] disable-with-reason primitive so the reason reaches assistive tech, not just a dimmed icon.
 * [onClick] opens the caller's [PipelineTestRunDialog] for the bound pipeline.
 */
@Composable
fun PipelineTestAction(pipelineId: String?, onClick: () -> Unit) {
    val decision: ManageDecision =
        if (pipelineId != null) ManageDecision.Allowed
        else ManageDecision.Denied(reason = stringResource(Res.string.pipeline_test_disabled_reason))
    ManageGate(decision = decision) { enabled ->
        GlyphButton(
            icon = PlayCircleGlyph,
            label = stringResource(Res.string.pipeline_test_action),
            enabled = enabled,
            onClick = onClick,
        )
    }
}

@Composable
fun PipelineTestRunDialog(
    running: Boolean,
    result: TestRunResult?,
    error: String?,
    onRun: (variables: Map<String, String>) -> Unit,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    var varsText: String by remember { mutableStateOf("") }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.pipelines_testrun_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Text(
                    text = stringResource(Res.string.pipelines_testrun_subtitle),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Textarea(
                    value = varsText,
                    onValueChange = { varsText = it },
                    label = stringResource(Res.string.pipelines_testrun_vars_label),
                    modifier = Modifier.fillMaxWidth(),
                    monospace = true,
                    minLines = 3,
                )
                error?.let { ActionErrorBanner(message = stringResource(Res.string.pipelines_testrun_error, it)) }
                result?.let { TestRunResultView(it) }
            }
        },
        confirmButton = {
            Button(onClick = { onRun(parsePipelineTestVariables(varsText)) }, enabled = !running) {
                Text(
                    if (running) stringResource(Res.string.pipelines_testrun_running)
                    else stringResource(Res.string.pipelines_testrun_run)
                )
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.pipelines_testrun_close)) } },
    )
}

@Composable
private fun TestRunResultView(result: TestRunResult) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text =
                    if (result.success) stringResource(Res.string.pipelines_testrun_ok)
                    else stringResource(Res.string.pipelines_testrun_failed),
                style = typography.sm,
                color = if (result.success) tokens.primary else tokens.destructive,
            )
            Text(
                text = stringResource(Res.string.pipelines_testrun_meta, result.durationMs, result.hostCallCount),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        result.error?.takeIf { it.isNotBlank() }?.let {
            Text(text = it, style = typography.xs, color = tokens.destructive)
        }

        Separator()

        Text(text = stringResource(Res.string.pipelines_testrun_chat_heading), style = typography.sm, color = tokens.cardForeground)
        if (result.chatOutput.isEmpty()) {
            Text(text = stringResource(Res.string.pipelines_testrun_chat_empty), style = typography.xs, color = tokens.mutedForeground)
        } else {
            result.chatOutput.forEach { line -> Text(text = line, style = typography.sm, color = tokens.foreground) }
        }

        Separator()

        Text(text = stringResource(Res.string.pipelines_testrun_effects_heading), style = typography.sm, color = tokens.cardForeground)
        if (result.capturedEffects.isEmpty()) {
            Text(text = stringResource(Res.string.pipelines_testrun_effects_empty), style = typography.xs, color = tokens.mutedForeground)
        } else {
            result.capturedEffects.forEach { effect ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                    val effectDisplayName: String =
                        resolveRowLabel(
                            primary = effect.name,
                            typeLabel = stringResource(Res.string.pipelines_effect_row_type),
                            discriminatorSource = effect.argsPreview,
                        )
                    Text(text = effectDisplayName, style = typography.sm, color = tokens.foreground)
                    if (effect.argsPreview.isNotBlank()) {
                        Text(
                            text = effect.argsPreview,
                            style = typography.xs,
                            color = tokens.mutedForeground,
                            maxLines = 3,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }
    }
}

// Parse the dry-run dialog's "key=value" lines into a variable map for the test-run request; blank lines and
// lines without an `=` are ignored, and a blank key is dropped.
private fun parsePipelineTestVariables(text: String): Map<String, String> =
    text.lineSequence()
        .mapNotNull { line ->
            val trimmed: String = line.trim()
            if (trimmed.isEmpty() || !trimmed.contains('=')) return@mapNotNull null
            val key: String = trimmed.substringBefore('=').trim()
            val value: String = trimmed.substringAfter('=').trim()
            if (key.isEmpty()) null else key to value
        }
        .toMap()
