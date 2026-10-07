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

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_access_failed_message
import nomnomzbot.composeapp.generated.resources.shell_access_failed_retry
import nomnomzbot.composeapp.generated.resources.shell_access_failed_title
import nomnomzbot.composeapp.generated.resources.shell_profile_logout
import org.jetbrains.compose.resources.stringResource

// DL3 — shown when the caller's own access could not be read (a definitive 401/403/404/…). It replaces the old
// silent downgrade to a role-less viewer: the user sees that something failed, can retry, or sign out. It renders
// no management or participant surface, so gating stays fail-closed. One primary action (Retry); Sign out is quiet.
@Composable
fun ShellAccessFailedScreen(reason: String, onRetry: () -> Unit, onSignOut: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
            modifier = Modifier.width(spacing.s24 * 4f),
        ) {
            Text(
                text = stringResource(Res.string.shell_access_failed_title),
                style = typography.lg,
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(Res.string.shell_access_failed_message),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            Text(text = reason, style = typography.xs, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Button(onClick = onRetry, variant = ButtonVariant.Default) {
                    Text(text = stringResource(Res.string.shell_access_failed_retry), style = typography.sm)
                }
                Button(onClick = onSignOut, variant = ButtonVariant.Ghost) {
                    Text(text = stringResource(Res.string.shell_profile_logout), style = typography.sm)
                }
            }
        }
    }
}
