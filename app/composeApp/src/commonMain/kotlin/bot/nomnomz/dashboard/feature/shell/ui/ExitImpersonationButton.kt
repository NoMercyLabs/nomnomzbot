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

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.connection.ImpersonationInfo
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_exit
import org.jetbrains.compose.resources.stringResource

// The ONE operator trace while acting as someone: a single "Exit impersonation" button, floated over the frame by
// App.kt so it is reachable on every page and above every splash. Everything else on screen is the target's — no
// operator name, no "acting as" text, no timer (an expired session exits by itself). It hangs off the raw
// [SessionStore.impersonating] flag, never [SessionUser.isAdmin]: acting as a non-admin flips isAdmin false, yet the
// operator must still be able to leave. It is the primary action of its (one-member) group. Once pressed it shows
// the pending state until the exit reloads the app, so a second press cannot queue a second exit.
@Composable
fun ExitImpersonationButton(
    sessionStore: SessionStore,
    onExit: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val actingAs: ImpersonationInfo? by sessionStore.impersonating.collectAsStateWithLifecycle()
    var exiting: Boolean by remember(actingAs) { mutableStateOf(false) }
    if (actingAs == null) return
    Button(
        onClick = {
            exiting = true
            onExit()
        },
        modifier = modifier,
        variant = ButtonVariant.Default,
        loading = exiting,
    ) {
        Text(text = stringResource(Res.string.shell_impersonation_exit))
    }
}
