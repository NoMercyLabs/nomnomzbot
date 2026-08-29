// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.connect.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_unreachable_message
import nomnomzbot.composeapp.generated.resources.shell_unreachable_retry
import nomnomzbot.composeapp.generated.resources.shell_unreachable_title
import org.jetbrains.compose.resources.stringResource

// S050 — "remembered-session vs unreachable distinction": a returning operator whose backend cannot currently
// be reached must NEVER see the same screen a first-time or logged-out visitor sees (Landing/Connect) — that
// silently implies they need to sign in again, when in truth their session is intact and only the network hop
// failed. [ConnectController.restoreUnreachable] gates the App gate onto this screen instead; the caller keeps
// retrying [ConnectController.restoreSession] automatically (see App.kt), [onRetry] is the manual escape hatch
// for an operator who does not want to wait out the next scheduled attempt.
@Composable
fun UnreachableScreen(onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(tokens.background)
            .padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
            modifier = Modifier.width(spacing.s24 * 4f),
        ) {
            Spinner(modifier = Modifier.size(spacing.s8), color = tokens.mutedForeground)
            Text(
                text = stringResource(Res.string.shell_unreachable_title),
                style = typography.lg,
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(Res.string.shell_unreachable_message),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            Button(onClick = onRetry, variant = ButtonVariant.Secondary) {
                Text(text = stringResource(Res.string.shell_unreachable_retry), style = typography.sm)
            }
        }
    }
}
