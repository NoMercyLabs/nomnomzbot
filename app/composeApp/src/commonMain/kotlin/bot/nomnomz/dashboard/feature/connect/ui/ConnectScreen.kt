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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.connect.state.ConnectController
import bot.nomnomz.dashboard.feature.connect.state.ConnectError
import bot.nomnomz.dashboard.feature.connect.state.ConnectStatus
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.connect_action_twitch
import nomnomzbot.composeapp.generated.resources.connect_connecting
import nomnomzbot.composeapp.generated.resources.connect_error_auth
import nomnomzbot.composeapp.generated.resources.connect_error_invalid_url
import nomnomzbot.composeapp.generated.resources.connect_subtitle
import nomnomzbot.composeapp.generated.resources.connect_title
import nomnomzbot.composeapp.generated.resources.connect_url_label
import nomnomzbot.composeapp.generated.resources.connect_url_placeholder
import org.jetbrains.compose.resources.stringResource

// The real direct-connect gate (frontend.md §5/§6). The streamer types a backend URL, hits
// "Connect with Twitch", and the controller runs the live OAuth dance (desktop loopback / web
// redirect) → captures the JWT → validates it via /me → the App gate flips to the shell. No mock.
@Composable
fun ConnectScreen(controller: ConnectController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    val baseUrl: String by controller.baseUrl.collectAsStateWithLifecycle()
    val status: ConnectStatus by controller.status.collectAsStateWithLifecycle()
    val connecting: Boolean = status is ConnectStatus.Connecting

    Box(
        modifier = Modifier.fillMaxSize().background(tokens.background),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier.widthIn(max = spacing.s24 * 4),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            Text(
                text = stringResource(Res.string.connect_title),
                style = typography.xl2,
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(Res.string.connect_subtitle),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )

            OutlinedTextField(
                value = baseUrl,
                onValueChange = controller::onBaseUrlChange,
                enabled = !connecting,
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(Res.string.connect_url_label)) },
                placeholder = { Text(stringResource(Res.string.connect_url_placeholder)) },
            )

            Button(
                onClick = { scope.launch { controller.connect() } },
                enabled = !connecting,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(text = stringResource(Res.string.connect_action_twitch))
            }

            ConnectStatusRow(status = status)
        }
    }
}

@Composable
private fun ConnectStatusRow(status: ConnectStatus) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    when (status) {
        is ConnectStatus.Connecting ->
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
                Text(
                    text = stringResource(Res.string.connect_connecting),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    textAlign = TextAlign.Center,
                )
            }

        is ConnectStatus.Error -> {
            val message: String =
                when (status.error) {
                    is ConnectError.InvalidUrl -> stringResource(Res.string.connect_error_invalid_url)
                    is ConnectError.Auth -> stringResource(Res.string.connect_error_auth)
                }
            Text(
                text = message,
                style = typography.sm,
                color = tokens.destructive,
                textAlign = TextAlign.Center,
            )
        }

        ConnectStatus.Idle -> Unit
    }
}
