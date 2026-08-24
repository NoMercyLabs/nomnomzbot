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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PipelineExecutionDetail
import bot.nomnomz.dashboard.core.network.PipelineExecutionStatus
import bot.nomnomz.dashboard.core.network.PipelineExecutionStepLog
import bot.nomnomz.dashboard.core.network.PipelineExecutionSummary
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineExecutionHistoryController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineExecutionHistoryState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipeline_history_action
import nomnomzbot.composeapp.generated.resources.pipeline_history_back
import nomnomzbot.composeapp.generated.resources.pipeline_history_detail_back
import nomnomzbot.composeapp.generated.resources.pipeline_history_detail_error
import nomnomzbot.composeapp.generated.resources.pipeline_history_detail_title
import nomnomzbot.composeapp.generated.resources.pipeline_history_duration
import nomnomzbot.composeapp.generated.resources.pipeline_history_empty
import nomnomzbot.composeapp.generated.resources.pipeline_history_error
import nomnomzbot.composeapp.generated.resources.pipeline_history_failures_only
import nomnomzbot.composeapp.generated.resources.pipeline_history_loading
import nomnomzbot.composeapp.generated.resources.pipeline_history_next
import nomnomzbot.composeapp.generated.resources.pipeline_history_page
import nomnomzbot.composeapp.generated.resources.pipeline_history_prev
import nomnomzbot.composeapp.generated.resources.pipeline_history_row_open
import nomnomzbot.composeapp.generated.resources.pipelines_retry
import nomnomzbot.composeapp.generated.resources.pipeline_history_status_failed
import nomnomzbot.composeapp.generated.resources.pipeline_history_status_partially_failed
import nomnomzbot.composeapp.generated.resources.pipeline_history_status_succeeded
import nomnomzbot.composeapp.generated.resources.pipeline_history_step_error
import nomnomzbot.composeapp.generated.resources.pipeline_history_step_failing
import nomnomzbot.composeapp.generated.resources.pipeline_history_step_title
import nomnomzbot.composeapp.generated.resources.pipeline_history_title
import org.jetbrains.compose.resources.stringResource

/** The label for the "History" entry point on the Pipelines list header. */
@Composable
fun pipelineHistoryActionLabel(): String = stringResource(Res.string.pipeline_history_action)

/**
 * The pipeline run-history debugging surface (S008c-read-b): a paged, newest-first list of the channel's real
 * runs with a failures-only filter, and — on opening a run — the ordered step logs with the FAILING step and
 * its error text called out. Read-only; the caller only reaches this composable when
 * [bot.nomnomz.dashboard.feature.pipelines.state.PipelineExecutionHistoryAccess.canRead] holds (§7 hide below
 * the read floor — enforced by the list screen's entry point, not here).
 */
@Composable
fun PipelineHistoryScreen(controller: PipelineExecutionHistoryController, onBack: () -> Unit) {
    val state: PipelineExecutionHistoryState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: PipelineExecutionHistoryState = state) {
            is PipelineExecutionHistoryState.Loading ->
                HistoryCenteredMessage(stringResource(Res.string.pipeline_history_loading))
            is PipelineExecutionHistoryState.Error ->
                HistoryErrorContent(
                    detail = current.detail,
                    onRetry = { scope.launch { controller.load() } },
                )
            is PipelineExecutionHistoryState.List -> HistoryList(current, controller, onBack, scope)
            is PipelineExecutionHistoryState.Detail ->
                RunDetail(current.run, onBack = { scope.launch { controller.closeRun() } })
        }
    }
}

@Composable
private fun HistoryCenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = androidx.compose.ui.Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

@Composable
private fun HistoryErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = androidx.compose.ui.Alignment.Center) {
        Column(
            horizontalAlignment = androidx.compose.ui.Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.pipeline_history_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.pipelines_retry)) }
        }
    }
}

@Composable
private fun HistoryList(
    listState: PipelineExecutionHistoryState.List,
    controller: PipelineExecutionHistoryController,
    onBack: () -> Unit,
    scope: kotlinx.coroutines.CoroutineScope,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        PageHeader(title = stringResource(Res.string.pipeline_history_title)) {
            TextButton(onClick = onBack) { Text(text = stringResource(Res.string.pipeline_history_back)) }
        }

        Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = stringResource(Res.string.pipeline_history_failures_only), style = typography.base)
            Switch(
                checked = listState.failuresOnly,
                onCheckedChange = { enabled -> scope.launch { controller.setFailuresOnly(enabled) } },
            )
        }

        if (listState.runs.isEmpty()) {
            HistoryCenteredMessage(stringResource(Res.string.pipeline_history_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
                LazyColumn(modifier = Modifier.fillMaxWidth()) {
                    itemsIndexed(items = listState.runs, key = { _, run -> run.id }) { index, run ->
                        if (index > 0) Separator()
                        RunRow(run = run, onOpen = { scope.launch { controller.openRun(run.id) } })
                    }
                }
            }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            TextButton(
                onClick = { scope.launch { controller.prevPage() } },
                enabled = listState.hasPrev,
            ) { Text(text = stringResource(Res.string.pipeline_history_prev)) }
            Text(text = stringResource(Res.string.pipeline_history_page, listState.page), style = typography.sm)
            TextButton(
                onClick = { scope.launch { controller.nextPage() } },
                enabled = listState.hasMore,
            ) { Text(text = stringResource(Res.string.pipeline_history_next)) }
        }
    }
}

@Composable
private fun RunRow(run: PipelineExecutionSummary, onOpen: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val openLabel: String = stringResource(Res.string.pipeline_history_row_open, run.id.toString())

    Row(
        modifier = Modifier.fillMaxWidth().padding(spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
    ) {
        Column {
            Text(text = run.triggerKind, style = typography.base)
            Text(text = stringResource(Res.string.pipeline_history_duration, run.durationMs), style = typography.sm)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
            StatusBadge(status = run.status)
            TextButton(onClick = onOpen) { Text(text = openLabel) }
        }
    }
}

@Composable
private fun StatusBadge(status: String) {
    val label: String =
        when (status) {
            PipelineExecutionStatus.Succeeded -> stringResource(Res.string.pipeline_history_status_succeeded)
            PipelineExecutionStatus.PartiallyFailed -> stringResource(Res.string.pipeline_history_status_partially_failed)
            PipelineExecutionStatus.Failed -> stringResource(Res.string.pipeline_history_status_failed)
            else -> status
        }
    val variant: BadgeVariant =
        when (status) {
            PipelineExecutionStatus.Succeeded -> BadgeVariant.Secondary
            else -> BadgeVariant.Destructive
        }
    Badge(variant = variant) { Text(text = label) }
}

@Composable
private fun RunDetail(run: PipelineExecutionDetail, onBack: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val failing: PipelineExecutionStepLog? = run.failingStep

    Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        PageHeader(title = stringResource(Res.string.pipeline_history_detail_title)) {
            TextButton(onClick = onBack) { Text(text = stringResource(Res.string.pipeline_history_detail_back)) }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            StatusBadge(status = run.status)
            Text(text = stringResource(Res.string.pipeline_history_duration, run.durationMs), style = typography.sm)
        }

        run.errorMessage?.let {
            Text(text = stringResource(Res.string.pipeline_history_detail_error, it), style = typography.base)
        }

        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            LazyColumn(modifier = Modifier.fillMaxWidth()) {
                itemsIndexed(items = run.stepLogs, key = { _, step -> step.stepIndex }) { index, step ->
                    if (index > 0) Separator()
                    StepRow(step = step, isFailing = step == failing)
                }
            }
        }
    }
}

@Composable
private fun StepRow(step: PipelineExecutionStepLog, isFailing: Boolean) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(modifier = Modifier.fillMaxWidth().padding(spacing.s3)) {
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
            Text(
                text = stringResource(Res.string.pipeline_history_step_title, step.stepIndex, step.actionType),
                style = typography.base,
            )
            if (isFailing) {
                Badge(variant = BadgeVariant.Destructive) {
                    Text(text = stringResource(Res.string.pipeline_history_step_failing))
                }
            }
        }
        if (isFailing) {
            step.errorMessage?.let {
                Text(
                    text = stringResource(Res.string.pipeline_history_step_error, it),
                    style = typography.sm,
                    color = tokens.destructive,
                )
            }
        }
    }
}
