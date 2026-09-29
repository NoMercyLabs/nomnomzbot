// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformdefaults.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PlatformDefaultChange
import bot.nomnomz.dashboard.core.network.PlatformDefaultPreview
import bot.nomnomz.dashboard.feature.platformdefaults.state.RestoreDefaultController
import bot.nomnomz.dashboard.feature.platformdefaults.state.RestoreDefaultState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.restore_default_cancel
import nomnomzbot.composeapp.generated.resources.restore_default_change
import nomnomzbot.composeapp.generated.resources.restore_default_confirm
import nomnomzbot.composeapp.generated.resources.restore_default_field_enabled
import nomnomzbot.composeapp.generated.resources.restore_default_field_fire_once
import nomnomzbot.composeapp.generated.resources.restore_default_field_interval
import nomnomzbot.composeapp.generated.resources.restore_default_field_messages
import nomnomzbot.composeapp.generated.resources.restore_default_field_min_chat_activity
import nomnomzbot.composeapp.generated.resources.restore_default_field_name
import nomnomzbot.composeapp.generated.resources.restore_default_field_step_settings
import nomnomzbot.composeapp.generated.resources.restore_default_field_steps
import nomnomzbot.composeapp.generated.resources.restore_default_intro
import nomnomzbot.composeapp.generated.resources.restore_default_loading
import nomnomzbot.composeapp.generated.resources.restore_default_nothing
import nomnomzbot.composeapp.generated.resources.restore_default_off
import nomnomzbot.composeapp.generated.resources.restore_default_on
import nomnomzbot.composeapp.generated.resources.restore_default_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

/**
 * Confirms "Restore default" for one seeded pipeline or template-installed timer. Before anything is written it
 * names every part the restore replaces, with the channel's current value beside the default. Restore is the
 * dialog's one primary action, in the destructive treatment; it stays disabled until the consequence is known
 * and when there is nothing to restore.
 */
@Composable
fun RestoreDefaultDialog(
    name: String,
    controller: RestoreDefaultController,
    onRestored: () -> Unit,
    onDismiss: () -> Unit,
) {
    val state: RestoreDefaultState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    LaunchedEffect(controller) { controller.open() }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.restore_default_title, name))
        when (val current: RestoreDefaultState = state) {
            is RestoreDefaultState.Loading ->
                DialogDescription(text = stringResource(Res.string.restore_default_loading))
            is RestoreDefaultState.Failed -> InlineError(message = current.detail)
            is RestoreDefaultState.Ready -> {
                val preview: PlatformDefaultPreview = current.preview
                if (preview.changes.isEmpty()) {
                    DialogDescription(text = stringResource(Res.string.restore_default_nothing))
                } else {
                    DialogDescription(text = stringResource(Res.string.restore_default_intro, preview.defaultVersion))
                    Column(
                        modifier = Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(spacing.s2),
                    ) {
                        preview.changes.forEach { change ->
                            Text(
                                text = changeLine(change),
                                style = typography.sm,
                                color = tokens.popoverForeground,
                                maxLines = 3,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }
            }
        }
        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Outline) {
                Text(text = stringResource(Res.string.restore_default_cancel), maxLines = 1)
            }
            val ready: RestoreDefaultState.Ready? = state as? RestoreDefaultState.Ready
            Button(
                onClick = { scope.launch { if (controller.confirm()) onRestored() } },
                variant = ButtonVariant.Destructive,
                enabled = ready != null && ready.preview.changes.isNotEmpty() && !ready.restoring,
                modifier = Modifier.testTag("restore-default-confirm"),
            ) {
                Text(text = stringResource(Res.string.restore_default_confirm), maxLines = 1)
            }
        }
    }
}

/** "Label: current → default"; the step-settings change has no values worth showing, only its label. */
@Composable
private fun changeLine(change: PlatformDefaultChange): String {
    val label: String = stringResource(fieldLabel(change.field))
    if (change.field == "step_settings") return label
    return stringResource(Res.string.restore_default_change, label, value(change.current), value(change.default))
}

@Composable
private fun value(raw: String): String =
    when (raw) {
        "true" -> stringResource(Res.string.restore_default_on)
        "false" -> stringResource(Res.string.restore_default_off)
        else -> raw.replace('\n', ' ')
    }

private fun fieldLabel(field: String): StringResource =
    when (field) {
        "name" -> Res.string.restore_default_field_name
        "messages" -> Res.string.restore_default_field_messages
        "interval" -> Res.string.restore_default_field_interval
        "min_chat_activity" -> Res.string.restore_default_field_min_chat_activity
        "enabled" -> Res.string.restore_default_field_enabled
        "fire_once" -> Res.string.restore_default_field_fire_once
        "steps" -> Res.string.restore_default_field_steps
        else -> Res.string.restore_default_field_step_settings
    }
