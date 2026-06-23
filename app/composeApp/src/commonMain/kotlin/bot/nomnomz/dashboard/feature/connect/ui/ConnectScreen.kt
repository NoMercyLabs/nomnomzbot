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

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.connect.state.ConnectController
import bot.nomnomz.dashboard.feature.connect.state.ConnectError
import bot.nomnomz.dashboard.feature.connect.state.ConnectStatus
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.connect_account_hint
import nomnomzbot.composeapp.generated.resources.connect_action_twitch
import nomnomzbot.composeapp.generated.resources.connect_connecting
import nomnomzbot.composeapp.generated.resources.connect_device_instruction
import nomnomzbot.composeapp.generated.resources.connect_device_open
import nomnomzbot.composeapp.generated.resources.connect_device_title
import nomnomzbot.composeapp.generated.resources.connect_device_waiting
import nomnomzbot.composeapp.generated.resources.connect_discovered_manual_label
import nomnomzbot.composeapp.generated.resources.connect_discovered_searching
import nomnomzbot.composeapp.generated.resources.connect_discovered_title
import nomnomzbot.composeapp.generated.resources.connect_error_auth
import nomnomzbot.composeapp.generated.resources.connect_error_invalid_url
import nomnomzbot.composeapp.generated.resources.connect_error_login_denied
import nomnomzbot.composeapp.generated.resources.connect_error_login_expired
import nomnomzbot.composeapp.generated.resources.connect_error_login_failed
import nomnomzbot.composeapp.generated.resources.connect_subtitle
import nomnomzbot.composeapp.generated.resources.connect_title
import nomnomzbot.composeapp.generated.resources.connect_url_label
import nomnomzbot.composeapp.generated.resources.connect_url_placeholder
import org.jetbrains.compose.resources.stringResource

// The real direct-connect gate (frontend.md §5/§6). Two ways onboard against a backend:
//   1. CLICK a bot mDNS-discovered on the LAN (zero-friction onboarding) — rendered above the field.
//   2. TYPE a backend URL and hit "Connect with Twitch".
// Either way the controller runs the live OAuth dance (desktop loopback / web redirect) → captures the
// JWT → validates it via /me → the App gate flips to the shell. No mock. The discovered list is empty on
// web (single-origin, served by its own bot), so that section simply renders nothing there.
@Composable
fun ConnectScreen(controller: ConnectController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    val baseUrl: String by controller.baseUrl.collectAsStateWithLifecycle()
    val status: ConnectStatus by controller.status.collectAsStateWithLifecycle()
    val discovered: List<ConnectionProfile> by controller.discovered.collectAsStateWithLifecycle()
    val busy: Boolean =
        status is ConnectStatus.Connecting || status is ConnectStatus.AwaitingApproval

    // Browse the LAN only while the Connect screen is on-screen, and only where discovery actually works
    // (desktop) — never on web, where it is a no-op; release the browser on dispose.
    if (controller.discoverySupported) {
        DisposableEffect(controller) {
            controller.startDiscovery()
            onDispose { controller.stopDiscovery() }
        }
    }

    Box(
        modifier =
            Modifier.fillMaxSize()
                .background(tokens.background)
                .verticalScroll(rememberScrollState())
                .padding(vertical = spacing.s8),
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

            val awaiting: ConnectStatus.AwaitingApproval? =
                status as? ConnectStatus.AwaitingApproval
            if (awaiting != null) {
                // The login is live — focus the screen on the code to approve at twitch.tv/activate.
                DeviceCodePanel(
                    userCode = awaiting.userCode,
                    verificationUri = awaiting.verificationUri,
                )
            } else {
                // Discovery is desktop-only; the web build is single-origin and can't browse, so hide the
                // whole "found on your network" section there rather than show a forever-"searching" hint.
                if (controller.discoverySupported) {
                    DiscoveredSection(
                        discovered = discovered,
                        enabled = !busy,
                        onConnect = { profile -> scope.launch { controller.connectTo(profile) } },
                    )
                }

                OutlinedTextField(
                    value = baseUrl,
                    onValueChange = controller::onBaseUrlChange,
                    enabled = !busy,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.connect_url_label)) },
                    placeholder = { Text(stringResource(Res.string.connect_url_placeholder)) },
                )

                Button(
                    onClick = { scope.launch { controller.connect() } },
                    enabled = !busy,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text(text = stringResource(Res.string.connect_action_twitch))
                }

                // Make the account unambiguous: this is the streamer's OWN account, and the bot is a
                // separate, optional account added later — never forced here.
                Text(
                    text = stringResource(Res.string.connect_account_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    textAlign = TextAlign.Center,
                )
            }

            ConnectStatusRow(status = status)
        }
    }
}

// The mDNS-discovered backends, each a click-to-connect row, with a subtle searching/empty hint and the
// "or enter a URL" label that introduces the manual field below.
@Composable
private fun DiscoveredSection(
    discovered: List<ConnectionProfile>,
    enabled: Boolean,
    onConnect: (ConnectionProfile) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.connect_discovered_title),
            style = typography.sm,
            color = tokens.foreground,
            modifier = Modifier.fillMaxWidth(),
        )

        if (discovered.isEmpty()) {
            Text(
                text = stringResource(Res.string.connect_discovered_searching),
                style = typography.xs,
                color = tokens.mutedForeground,
                modifier = Modifier.fillMaxWidth(),
            )
        } else {
            discovered.forEach { profile ->
                DiscoveredRow(profile = profile, enabled = enabled, onConnect = onConnect)
            }
        }

        Text(
            text = stringResource(Res.string.connect_discovered_manual_label),
            style = typography.xs,
            color = tokens.mutedForeground,
            modifier = Modifier.fillMaxWidth().padding(top = spacing.s2),
        )
    }
}

// A single discovered backend — a bordered card on the surface token, clickable to onboard against it.
@Composable
private fun DiscoveredRow(
    profile: ConnectionProfile,
    enabled: Boolean,
    onConnect: (ConnectionProfile) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val shape = RoundedCornerShape(tokens.radius.md)
    Column(
        modifier =
            Modifier.fillMaxWidth()
                .clip(shape)
                .background(tokens.card)
                .border(BorderStroke(spacing.s0_5 / 2, tokens.border), shape)
                .clickable(enabled = enabled) { onConnect(profile) }
                .padding(horizontal = spacing.s3, vertical = spacing.s2),
        verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
    ) {
        Text(
            text = profile.displayName,
            style = typography.sm,
            color = tokens.cardForeground,
        )
        Text(
            text = profile.baseUrl,
            style = typography.xs,
            color = tokens.mutedForeground,
        )
    }
}

// The live device-login panel: the user code to enter at twitch.tv/activate, a button that opens Twitch
// (the verification URL comes pre-filled with the code), and a "waiting for approval" indicator while the
// controller polls. Opening the link uses the platform UriHandler — the system browser on desktop, a new
// tab on web.
@Composable
private fun DeviceCodePanel(userCode: String, verificationUri: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val uriHandler = LocalUriHandler.current

    val shape = RoundedCornerShape(tokens.radius.lg)
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(shape)
            .background(tokens.card)
            .border(BorderStroke(spacing.s0_5 / 2, tokens.border), shape)
            .padding(spacing.s4),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.connect_device_title),
            style = typography.lg,
            color = tokens.cardForeground,
            textAlign = TextAlign.Center,
        )
        Text(
            text = stringResource(Res.string.connect_device_instruction),
            style = typography.sm,
            color = tokens.mutedForeground,
            textAlign = TextAlign.Center,
        )
        Text(
            text = userCode,
            style = typography.xl2,
            color = tokens.primary,
            textAlign = TextAlign.Center,
        )
        Button(
            onClick = { uriHandler.openUri(verificationUri) },
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(text = stringResource(Res.string.connect_device_open))
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
            Text(
                text = stringResource(Res.string.connect_device_waiting),
                style = typography.xs,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
        }

        Text(
            text = stringResource(Res.string.connect_account_hint),
            style = typography.xs,
            color = tokens.mutedForeground,
            textAlign = TextAlign.Center,
        )
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

        // The device panel carries its own user code + waiting indicator.
        is ConnectStatus.AwaitingApproval -> Unit

        is ConnectStatus.Error -> {
            val message: String =
                when (status.error) {
                    is ConnectError.InvalidUrl -> stringResource(Res.string.connect_error_invalid_url)
                    is ConnectError.Auth -> stringResource(Res.string.connect_error_auth)
                    is ConnectError.LoginExpired ->
                        stringResource(Res.string.connect_error_login_expired)
                    is ConnectError.LoginDenied ->
                        stringResource(Res.string.connect_error_login_denied)
                    is ConnectError.LoginFailed ->
                        stringResource(Res.string.connect_error_login_failed)
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
