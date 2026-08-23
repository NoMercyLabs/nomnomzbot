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
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.connection.ImpersonationInfo
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import kotlinx.coroutines.delay
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_banner
import nomnomzbot.composeapp.generated.resources.shell_impersonation_exit
import nomnomzbot.composeapp.generated.resources.shell_impersonation_expires
import nomnomzbot.composeapp.generated.resources.shell_impersonation_expires_soon
import org.jetbrains.compose.resources.stringResource

// The admin act-as banner. Shows at the top of the shell for the WHOLE time the operator is impersonating another
// user, on EVERY page — it hangs off [SessionStore.activeImpersonation], never [SessionUser.isAdmin]
// (impersonating a non-admin flips isAdmin false, yet the operator must still be able to exit) AND never off the
// raw [SessionStore.impersonating] flag alone — that one does not re-check expiry, so it could still read
// non-null a tick after the time-boxed support session ran out. A one-second tick keeps the remaining-time
// readout live and re-evaluates expiry without any extra wiring from the caller. "Stop impersonating" restores
// the operator's own token and re-resolves identity/access/hubs back to them. Idle or expired = hidden.
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
    val active: ImpersonationInfo? = raw?.takeUnless { it.isExpired(now) }

    AnimatedVisibility(visible = active != null, modifier = modifier) {
        val tokens = LocalTokens.current
        val spacing = LocalSpacing.current
        val typography = LocalTypography.current
        // Held during the brief collapse animation after impersonation ends/expires (already null then).
        val name: String = active?.displayName ?: ""
        val minutesRemaining: Long = active?.let { (it.expiresAt - now).inWholeMinutes } ?: 0L

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(tokens.accent)
                .padding(horizontal = spacing.s4, vertical = spacing.s3),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(Res.string.shell_impersonation_banner, name),
                    style = typography.sm,
                    fontWeight = FontWeight.Medium,
                    color = tokens.accentForeground,
                )
                Text(
                    text = if (minutesRemaining >= 1) {
                        stringResource(Res.string.shell_impersonation_expires, minutesRemaining)
                    } else {
                        stringResource(Res.string.shell_impersonation_expires_soon)
                    },
                    style = typography.xs,
                    color = tokens.accentForeground,
                )
            }
            Text(
                text = stringResource(Res.string.shell_impersonation_exit),
                style = typography.sm,
                fontWeight = FontWeight.SemiBold,
                color = tokens.accentForeground,
                modifier = Modifier
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .clickable(onClick = onExit)
                    .padding(horizontal = spacing.s2, vertical = spacing.s1),
            )
        }
    }
}

private const val TICK_MS: Long = 1_000L
