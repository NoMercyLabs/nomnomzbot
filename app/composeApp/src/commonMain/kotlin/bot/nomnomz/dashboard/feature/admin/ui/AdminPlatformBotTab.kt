// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PlatformBotAdminState
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_confirm_message
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_confirm_title
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_confirm_cancel
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_confirm_confirm
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_device_cancel
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_device_notice
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_device_user_code
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_justification_label
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_never_connected
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_notice
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_preview_button
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_preview_summary
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_reconnect_button
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_section_title
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_status_connected
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_status_unusable
import nomnomzbot.composeapp.generated.resources.admin_platform_bot_status_unusable_notice
import org.jetbrains.compose.resources.stringResource

/**
 * The shared platform bot's admin surface (S-BOT-PLATFORM-UI). Once first-run setup is done, this is the
 * ONLY route left to re-connect or replace it — the channel Integrations screen deliberately polls the
 * channel-scoped endpoint instead (the fix for the 2026-09-04 takeover), so this stays the platform-wide
 * one. Reconnecting/replacing it is destructive (every channel with no bot of its own resolves to the new
 * account from then on), so it gets the network-block treatment: a quiet preview step, then a single
 * full-chroma destructive action gated on a real, freshly-counted blast radius shown in the confirm dialog.
 */
@Composable
internal fun PlatformBotTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    val status = state.platformBotStatus
    val preview = state.platformBotReconnectPreview
    val device = state.platformBotReconnectDevice
    val canPreview: Boolean = state.platformBotJustification.isNotBlank() && device == null

    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.admin_platform_bot_section_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = stringResource(Res.string.admin_platform_bot_notice),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        when {
            state.platformBotStatusLoading -> Spinner(color = tokens.primary)
            status == null -> Unit
            status.state == PlatformBotAdminState.NEVER_CONNECTED ->
                Text(
                    text = stringResource(Res.string.admin_platform_bot_never_connected),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            status.state == PlatformBotAdminState.CONNECTED_TOKEN_UNUSABLE ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        Text(
                            text = stringResource(
                                Res.string.admin_platform_bot_status_unusable,
                                status.botUsername.orEmpty(),
                            ),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                        Text(
                            text = stringResource(Res.string.admin_platform_bot_status_unusable_notice),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    }
                }
            else ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Text(
                        text = stringResource(
                            Res.string.admin_platform_bot_status_connected,
                            status.botUsername.orEmpty(),
                        ),
                        style = typography.sm,
                    )
                }
        }
        state.platformBotStatusError?.let { ActionErrorBanner(message = it) }

        AppTextField(
            value = state.platformBotJustification,
            onValueChange = { controller.setPlatformBotJustification(it) },
            label = stringResource(Res.string.admin_platform_bot_justification_label),
            modifier = Modifier.fillMaxWidth(),
        )

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            // The quiet, neutral step every reconnect must go through first.
            Button(
                onClick = { scope.launch { controller.previewPlatformBotReconnect() } },
                enabled = canPreview && !state.platformBotReconnectPreviewLoading,
                variant = ButtonVariant.Outline,
            ) {
                Text(text = stringResource(Res.string.admin_platform_bot_preview_button), maxLines = 1)
            }
            // The one full-chroma destructive action on this section — unreachable without a real preview.
            Button(
                onClick = { controller.requestPlatformBotReconnect() },
                enabled = preview != null && device == null,
                variant = ButtonVariant.Destructive,
            ) {
                Text(text = stringResource(Res.string.admin_platform_bot_reconnect_button), maxLines = 1)
            }
        }

        if (state.platformBotReconnectPreviewLoading) Spinner(color = tokens.primary)
        state.platformBotReconnectPreviewError?.let { ActionErrorBanner(message = it) }
        state.platformBotReconnectError?.let { ActionErrorBanner(message = it) }

        preview?.let {
            Text(
                text = stringResource(
                    Res.string.admin_platform_bot_preview_summary,
                    it.affectedChannelCount,
                ),
                style = typography.xs,
                color = tokens.destructive,
            )
        }

        device?.let {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Text(
                        text = stringResource(Res.string.admin_platform_bot_device_notice, it.verificationUri),
                        style = typography.sm,
                    )
                    Text(
                        text = stringResource(Res.string.admin_platform_bot_device_user_code, it.userCode),
                        style = typography.sm,
                        color = tokens.primary,
                    )
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Spinner(color = tokens.primary)
                        Button(
                            onClick = { controller.cancelPlatformBotReconnect() },
                            variant = ButtonVariant.Ghost,
                        ) {
                            Text(text = stringResource(Res.string.admin_platform_bot_device_cancel), maxLines = 1)
                        }
                    }
                }
            }
        }
    }

    if (state.platformBotReconnectConfirmOpen && preview != null) {
        ConfirmDialog(
            title = stringResource(Res.string.admin_platform_bot_confirm_title),
            message = stringResource(
                Res.string.admin_platform_bot_confirm_message,
                preview.affectedChannelCount,
            ),
            confirmLabel = stringResource(Res.string.admin_platform_bot_confirm_confirm),
            dismissLabel = stringResource(Res.string.admin_platform_bot_confirm_cancel),
            destructive = true,
            onConfirm = { scope.launch { controller.confirmPlatformBotReconnect() } },
            onDismiss = { controller.dismissPlatformBotReconnectRequest() },
        )
    }
}
