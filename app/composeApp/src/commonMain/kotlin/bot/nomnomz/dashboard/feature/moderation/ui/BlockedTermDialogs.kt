// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.moderation.state.TermSweep
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_terms_cancel
import nomnomzbot.composeapp.generated.resources.moderation_terms_channels_loading
import nomnomzbot.composeapp.generated.resources.moderation_terms_channels_unknown
import nomnomzbot.composeapp.generated.resources.moderation_terms_everywhere_confirm
import nomnomzbot.composeapp.generated.resources.moderation_terms_everywhere_message
import nomnomzbot.composeapp.generated.resources.moderation_terms_everywhere_title
import nomnomzbot.composeapp.generated.resources.moderation_terms_own_channel_only
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove_everywhere_message
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove_title
import nomnomzbot.composeapp.generated.resources.moderation_terms_scope_everywhere
import nomnomzbot.composeapp.generated.resources.moderation_terms_scope_here
import nomnomzbot.composeapp.generated.resources.moderation_terms_sweep_added
import nomnomzbot.composeapp.generated.resources.moderation_terms_sweep_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_terms_sweep_failed_row
import nomnomzbot.composeapp.generated.resources.moderation_terms_sweep_removed
import org.jetbrains.compose.resources.stringResource

/**
 * The channels a cross-channel term sweep reaches besides the operator's own: loading, unknown (Twitch could not
 * list them), or known. Shown before the sweep runs, so its reach is never a surprise.
 */
internal sealed interface SweepReach {
    data object Loading : SweepReach

    data object Unknown : SweepReach

    data class Known(val channels: List<String>) : SweepReach
}

@Composable
private fun reachText(reach: SweepReach, known: @Composable (List<String>) -> String): String =
    when (reach) {
        SweepReach.Loading -> stringResource(Res.string.moderation_terms_channels_loading)
        SweepReach.Unknown -> stringResource(Res.string.moderation_terms_channels_unknown)
        is SweepReach.Known ->
            if (reach.channels.isEmpty()) {
                stringResource(Res.string.moderation_terms_own_channel_only)
            } else {
                known(reach.channels)
            }
    }

/** Confirms blocking [term] in the operator's own channel and every channel they moderate. */
@Composable
internal fun BlockEverywhereDialog(
    term: String,
    reach: SweepReach,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    ConfirmDialog(
        title = stringResource(Res.string.moderation_terms_everywhere_title, term),
        message =
            reachText(reach) { channels ->
                stringResource(
                    Res.string.moderation_terms_everywhere_message,
                    channels.size,
                    channels.joinToString(", "),
                )
            },
        confirmLabel = stringResource(Res.string.moderation_terms_everywhere_confirm),
        dismissLabel = stringResource(Res.string.moderation_terms_cancel),
        onConfirm = onConfirm,
        onDismiss = onDismiss,
        confirmEnabled = reach != SweepReach.Loading,
    )
}

/**
 * Confirms removing [term], in this channel or in every channel the operator moderates. The wider scope names the
 * channels it reaches, which load only when it is picked.
 */
@Composable
internal fun RemoveTermDialog(
    term: String,
    reach: SweepReach?,
    onPickEverywhere: () -> Unit,
    onRemoveHere: () -> Unit,
    onRemoveEverywhere: () -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var everywhere: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.moderation_terms_remove_title, term)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Badge(selected = !everywhere, onClick = { everywhere = false }) {
                        Text(stringResource(Res.string.moderation_terms_scope_here), style = typography.sm)
                    }
                    Badge(
                        selected = everywhere,
                        onClick = {
                            everywhere = true
                            onPickEverywhere()
                        },
                    ) {
                        Text(stringResource(Res.string.moderation_terms_scope_everywhere), style = typography.sm)
                    }
                }
                if (everywhere && reach != null) {
                    Text(
                        text =
                            reachText(reach) { channels ->
                                stringResource(
                                    Res.string.moderation_terms_remove_everywhere_message,
                                    channels.size,
                                    channels.joinToString(", "),
                                )
                            },
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = if (everywhere) onRemoveEverywhere else onRemoveHere,
                enabled = !everywhere || (reach != null && reach != SweepReach.Loading),
                variant = ButtonVariant.Destructive,
            ) {
                Text(text = stringResource(Res.string.moderation_terms_remove), maxLines = 1)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.moderation_terms_cancel),
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        },
    )
}

/** The outcome of the last cross-channel sweep: how many channels took it, and why each failure failed. */
@Composable
internal fun TermSweepCard(sweep: TermSweep, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text =
                        stringResource(
                            if (sweep.added) {
                                Res.string.moderation_terms_sweep_added
                            } else {
                                Res.string.moderation_terms_sweep_removed
                            },
                            sweep.term,
                            sweep.result.succeeded,
                            sweep.result.attempted,
                        ),
                    style = typography.sm,
                    color = tokens.cardForeground,
                    modifier = Modifier.weight(1f),
                )
                TextButton(onClick = onDismiss) {
                    Text(
                        text = stringResource(Res.string.moderation_terms_sweep_dismiss),
                        color = tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            sweep.result.channels
                .filter { !it.succeeded }
                .forEach { failed ->
                    Text(
                        text =
                            stringResource(
                                Res.string.moderation_terms_sweep_failed_row,
                                failed.broadcasterLogin,
                                failed.error.orEmpty(),
                            ),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                }
        }
    }
}
