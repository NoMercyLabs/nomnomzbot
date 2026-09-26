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
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.connection.ImpersonationInfo
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import kotlinx.coroutines.delay
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_banner_ending
import nomnomzbot.composeapp.generated.resources.shell_impersonation_banner_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_banner_remaining
import nomnomzbot.composeapp.generated.resources.shell_impersonation_exit
import org.jetbrains.compose.resources.stringResource

// The admin act-as banner: the ONE operator trace while acting as someone. It sits in the frame above the shell
// (App.kt), on every page and above the splash, so Exit is always reachable. It hangs off the raw
// [SessionStore.impersonating] flag, never [SessionUser.isAdmin] (acting as a non-admin flips isAdmin false, yet
// the operator must still be able to exit). A one-second tick keeps the remaining time live; once the time-boxed
// session has run out the banner stays, says so, and keeps Exit — act-as never silently lingers without a way out.
@Composable
fun ImpersonationBanner(
    sessionStore: SessionStore,
    onExit: () -> Unit,
    modifier: Modifier = Modifier,
) {
    // Re-read [sessionStore.impersonating] so the tick below also reacts to a fresh begin/end, not just the clock.
    val raw: ImpersonationInfo? by sessionStore.impersonating.collectAsStateWithLifecycle()
    val now: Instant by produceState(initialValue = Clock.System.now(), raw) {
        while (true) {
            value = Clock.System.now()
            delay(TICK_MS)
        }
    }

    AnimatedVisibility(visible = raw != null, modifier = modifier) {
        val tokens = LocalTokens.current
        val spacing = LocalSpacing.current
        val typography = LocalTypography.current
        // Held during the brief collapse animation after impersonation ends (already null then).
        val name: String = raw?.displayName ?: ""
        val expired: Boolean = raw?.isExpired(now) ?: false
        val minutesRemaining: Long = raw?.let { (it.expiresAt - now).inWholeMinutes } ?: 0L
        val message: String = when {
            expired -> stringResource(Res.string.shell_impersonation_banner_expired, name)
            minutesRemaining >= 1 -> stringResource(Res.string.shell_impersonation_banner_remaining, name, minutesRemaining)
            else -> stringResource(Res.string.shell_impersonation_banner_ending, name)
        }

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(tokens.accent)
                .padding(horizontal = spacing.s4, vertical = spacing.s1_5),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = message,
                style = typography.xs,
                fontWeight = FontWeight.Medium,
                color = tokens.accentForeground,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onExit) {
                Text(
                    text = stringResource(Res.string.shell_impersonation_exit),
                    style = typography.xs,
                    fontWeight = FontWeight.SemiBold,
                    color = tokens.accentForeground,
                )
            }
        }
    }
}

private const val TICK_MS: Long = 1_000L
