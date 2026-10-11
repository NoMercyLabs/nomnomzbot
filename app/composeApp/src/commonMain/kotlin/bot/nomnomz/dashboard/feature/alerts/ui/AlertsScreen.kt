// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.alerts.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AlertQueueEntryDto
import bot.nomnomz.dashboard.feature.alerts.state.AlertQueueUiState
import bot.nomnomz.dashboard.feature.alerts.state.AlertsController
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.alerts_queue_connected
import nomnomzbot.composeapp.generated.resources.alerts_queue_empty
import nomnomzbot.composeapp.generated.resources.alerts_queue_not_connected
import nomnomzbot.composeapp.generated.resources.alerts_queue_status_delivered
import nomnomzbot.composeapp.generated.resources.alerts_queue_status_queued
import nomnomzbot.composeapp.generated.resources.alerts_queue_title
import nomnomzbot.composeapp.generated.resources.alerts_title
import org.jetbrains.compose.resources.stringResource

// The alert-queue tab of Event Responses (frontend-ia.md): the channel's ONE cross-platform alert queue, read
// live from [AlertsController]. The screen is a pure projection of the controller's state; it loads on first
// composition. The event-response rows are managed on the Event Responses page itself, so this tab is read-only.
@Composable
fun AlertsScreen(controller: AlertsController) {
    val queueState: AlertQueueUiState by controller.queueState.collectAsStateWithLifecycle()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.alerts_title))
        AlertQueueSection(queueState)
    }
}

// The live alert queue (widgets-overlays.md §1.2): the ONE cross-platform queue, rendered above the
// event-response config list on the same owner page. [AlertQueueUiState.Ready.overlayConnected] drives the
// connection line honestly — read live from the backend's presence registry, never implied by the entries
// list being non-empty. This section uses only neutral/semantic tokens (success/destructive), never the
// brand accent.
@Composable
private fun AlertQueueSection(queueState: AlertQueueUiState) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val ready: AlertQueueUiState.Ready = queueState as? AlertQueueUiState.Ready ?: return

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(Res.string.alerts_queue_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                )
                Text(
                    text =
                        stringResource(
                            if (ready.overlayConnected) Res.string.alerts_queue_connected
                            else Res.string.alerts_queue_not_connected
                        ),
                    style = typography.sm,
                    color = if (ready.overlayConnected) tokens.success else tokens.destructive,
                )
            }

            if (ready.entries.isEmpty()) {
                Text(
                    text = stringResource(Res.string.alerts_queue_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    // Most-recent-first, capped so a busy channel's queue card stays a fixed size rather
                    // than growing without bound.
                    ready.entries.take(QueueVisibleEntryCount).forEach { entry ->
                        AlertQueueRow(entry)
                    }
                }
            }
        }
    }
}

private const val QueueVisibleEntryCount: Int = 5

@Composable
private fun AlertQueueRow(entry: AlertQueueEntryDto) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val delivered: Boolean = entry.status == "delivered"
    val statusLabel: String =
        stringResource(
            if (delivered) Res.string.alerts_queue_status_delivered
            else Res.string.alerts_queue_status_queued
        )

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // The cross-platform attribution (widgets-overlays.md §1.2) — every entry names its own origin,
        // never merged into one undifferentiated "alert" label.
        Badge(variant = BadgeVariant.Outline) { Text(text = entry.provider) }

        // Secondary attribute next to the provider badge (the row's real identity) — never clipped, so a
        // long event kind is still fully readable rather than silently truncated (TruncatedPrimaryLabelGuardTest).
        Text(
            text = entry.kind,
            style = typography.sm,
            color = tokens.cardForeground,
            modifier = Modifier.weight(1f),
        )

        Text(
            text = statusLabel,
            style = typography.xs,
            color = if (delivered) tokens.success else tokens.mutedForeground,
        )
    }
}
