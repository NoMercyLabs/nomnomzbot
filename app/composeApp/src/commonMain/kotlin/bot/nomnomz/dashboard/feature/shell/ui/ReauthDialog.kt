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

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.feature.connect.state.ConnectController
import bot.nomnomz.dashboard.feature.connect.state.ConnectStatus
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.reauth_dialog_confirm
import nomnomzbot.composeapp.generated.resources.reauth_dialog_dismiss
import nomnomzbot.composeapp.generated.resources.reauth_dialog_message
import nomnomzbot.composeapp.generated.resources.reauth_dialog_title
import org.jetbrains.compose.resources.stringResource

// The dead-token recovery WARNING as a proper modal (never-logout-for-scope-or-schema-changes) — not a passive top
// bar. It renders only when the backend health probe reports the operator's Twitch token is dead
// ([ConnectController.reauthRequired]) AND no reconnect is already in flight (status Idle). The primary action runs
// the redirect re-auth; "Later" dismisses it until the next load.
//
// It self-heals: while the warning is up it re-polls Twitch health every few seconds, so the dialog AUTO-DISMISSES
// the instant the token is restored — after the reconnect returns, or if the connection recovers elsewhere — with
// no manual reload and no stale prompt left hanging (the bug this replaces: the old bar only ever appeared, it
// never re-checked to clear itself once the operator reconnected).
@Composable
fun ReauthDialog(controller: ConnectController, onReconnect: () -> Unit) {
    val reauthRequired: Boolean by controller.reauthRequired.collectAsStateWithLifecycle()
    val status: ConnectStatus by controller.status.collectAsStateWithLifecycle()

    // Re-probe health on an interval WHILE the warning is up; a restored token flips reauthRequired false, which
    // re-keys (and cancels) this effect, stopping the poll. A healthy/absent connection never starts it.
    LaunchedEffect(reauthRequired) {
        if (!reauthRequired) return@LaunchedEffect
        while (isActive) {
            delay(POLL_INTERVAL_MS)
            controller.checkTwitchHealth()
        }
    }

    if (reauthRequired && status is ConnectStatus.Idle) {
        ConfirmDialog(
            title = stringResource(Res.string.reauth_dialog_title),
            message = stringResource(Res.string.reauth_dialog_message),
            confirmLabel = stringResource(Res.string.reauth_dialog_confirm),
            dismissLabel = stringResource(Res.string.reauth_dialog_dismiss),
            onConfirm = onReconnect,
            onDismiss = { controller.dismissReauthPrompt() },
        )
    }
}

private const val POLL_INTERVAL_MS: Long = 8_000
