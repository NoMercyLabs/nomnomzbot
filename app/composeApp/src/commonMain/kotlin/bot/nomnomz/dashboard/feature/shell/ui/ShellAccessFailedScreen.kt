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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcon
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcons
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.connect.ui.NomNomzMarkGlyph
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.connect_logo_cd_app
import nomnomzbot.composeapp.generated.resources.shell_access_failed_code
import nomnomzbot.composeapp.generated.resources.shell_access_failed_message
import nomnomzbot.composeapp.generated.resources.shell_access_failed_retry
import nomnomzbot.composeapp.generated.resources.shell_access_failed_title
import nomnomzbot.composeapp.generated.resources.shell_profile_logout
import org.jetbrains.compose.resources.stringResource

// DL3 — shown when the caller's own access could not be read (a definitive 401/403/404/…). It replaces the old
// silent downgrade to a role-less viewer: the user sees that something failed, can retry, or sign out. It renders
// no management or participant surface, so gating stays fail-closed. One primary action (Retry); Sign out is quiet.
// The brand sits above the card so a full-page failure still reads as NomNomzBot, not a bare system error.
@Composable
fun ShellAccessFailedScreen(reason: String, onRetry: () -> Unit, onSignOut: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Box(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s8),
            modifier = Modifier.widthIn(max = spacing.s24 * 4f + spacing.s16).fillMaxWidth(),
        ) {
            BrandHeader()
            Card(modifier = Modifier.fillMaxWidth()) {
                FailureBody(reason = reason, onRetry = onRetry, onSignOut = onSignOut)
            }
        }
    }
}

@Composable
private fun BrandHeader() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Icon(
            imageVector = NomNomzMarkGlyph,
            contentDescription = stringResource(Res.string.connect_logo_cd_app),
            tint = tokens.primary,
            modifier = Modifier.size(spacing.s12),
        )
        Text(text = stringResource(Res.string.app_name), style = typography.xl2, color = tokens.foreground)
    }
}

@Composable
private fun FailureBody(reason: String, onRetry: () -> Unit, onSignOut: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
        modifier = Modifier.fillMaxWidth().padding(spacing.s8),
    ) {
        Box(
            modifier = Modifier.size(spacing.s16).background(tokens.muted, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            AppIcon(icon = AppIcons.Lock, contentDescription = null, tint = tokens.foreground, size = spacing.s8)
        }
        Text(
            text = stringResource(Res.string.shell_access_failed_title),
            style = typography.xl2,
            color = tokens.foreground,
            textAlign = TextAlign.Center,
        )
        Text(
            text = stringResource(Res.string.shell_access_failed_message),
            style = typography.base,
            color = tokens.mutedForeground,
            textAlign = TextAlign.Center,
        )
        Column(
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
            modifier = Modifier.fillMaxWidth().padding(top = spacing.s2),
        ) {
            Button(onClick = onRetry, variant = ButtonVariant.Default, size = ButtonSize.Lg, modifier = Modifier.fillMaxWidth()) {
                Text(text = stringResource(Res.string.shell_access_failed_retry))
            }
            Button(onClick = onSignOut, variant = ButtonVariant.Ghost, size = ButtonSize.Lg, modifier = Modifier.fillMaxWidth()) {
                Text(text = stringResource(Res.string.shell_profile_logout))
            }
        }
        Separator()
        Text(
            text = stringResource(Res.string.shell_access_failed_code, reason),
            style = typography.xs,
            color = tokens.mutedForeground,
            textAlign = TextAlign.Center,
        )
    }
}
