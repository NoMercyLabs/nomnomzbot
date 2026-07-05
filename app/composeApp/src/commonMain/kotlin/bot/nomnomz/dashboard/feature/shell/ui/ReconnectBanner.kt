// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.connect.state.ConnectController
import bot.nomnomz.dashboard.feature.connect.state.ConnectStatus
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_reconnect_awaiting
import nomnomzbot.composeapp.generated.resources.shell_reconnect_close
import nomnomzbot.composeapp.generated.resources.shell_reconnect_failed
import nomnomzbot.composeapp.generated.resources.shell_reconnect_retry
import nomnomzbot.composeapp.generated.resources.shell_reconnect_starting
import org.jetbrains.compose.resources.stringResource

// The dead-token recovery bar (never-logout-for-scope-or-schema-changes). When the operator triggers a Twitch
// reconnect (profile menu), or a reconnect is otherwise in flight, this bar surfaces the device-code state at the
// top of the shell — the user code + verification link to approve at twitch.tv/activate — reusing the exact
// [ConnectController] device-code state machine the Connect screen renders. It re-vaults a fresh token in place;
// no logout, the session is kept. Idle = hidden (nothing to show). See [ConnectController.reconnect].
@Composable
fun ReconnectBanner(
    controller: ConnectController,
    onDismiss: () -> Unit,
    onRetry: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val status: ConnectStatus by controller.status.collectAsStateWithLifecycle()

    // Render only while a reconnect is actually happening or has failed — a successful reconnect returns the
    // status to Idle, which auto-hides the bar (its whole purpose is done, chat is restored on the next call).
    val active: Boolean =
        status is ConnectStatus.Connecting ||
            status is ConnectStatus.AwaitingApproval ||
            status is ConnectStatus.Error

    AnimatedVisibility(visible = active, modifier = modifier) {
        val tokens = LocalTokens.current
        val spacing = LocalSpacing.current
        val typography = LocalTypography.current
        val isError: Boolean = status is ConnectStatus.Error

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(if (isError) tokens.destructive else tokens.primary)
                .padding(horizontal = spacing.s4, vertical = spacing.s3),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = bannerText(status),
                style = typography.sm,
                fontWeight = FontWeight.Medium,
                color = if (isError) tokens.destructiveForeground else tokens.primaryForeground,
                modifier = Modifier.weight(1f),
            )
            if (isError) {
                BannerAction(
                    label = stringResource(Res.string.shell_reconnect_retry),
                    color = tokens.destructiveForeground,
                    onClick = onRetry,
                )
            }
            BannerAction(
                label = stringResource(Res.string.shell_reconnect_close),
                color = if (isError) tokens.destructiveForeground else tokens.primaryForeground,
                onClick = onDismiss,
            )
        }
    }
}

@Composable
private fun bannerText(status: ConnectStatus): String =
    when (status) {
        is ConnectStatus.AwaitingApproval ->
            stringResource(Res.string.shell_reconnect_awaiting, status.verificationUri, status.userCode)
        is ConnectStatus.Error -> stringResource(Res.string.shell_reconnect_failed)
        else -> stringResource(Res.string.shell_reconnect_starting)
    }

@Composable
private fun BannerAction(label: String, color: androidx.compose.ui.graphics.Color, onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = label,
        style = typography.sm,
        fontWeight = FontWeight.SemiBold,
        color = color,
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .clickable(onClick = onClick)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    )
}
