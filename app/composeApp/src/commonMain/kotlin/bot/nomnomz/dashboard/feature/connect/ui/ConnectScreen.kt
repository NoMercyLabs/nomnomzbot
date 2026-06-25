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
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.TextButton
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
import androidx.compose.ui.platform.UriHandler
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
import nomnomzbot.composeapp.generated.resources.connect_modal_heading_first_login
import nomnomzbot.composeapp.generated.resources.connect_url_label
import nomnomzbot.composeapp.generated.resources.connect_url_placeholder
import nomnomzbot.composeapp.generated.resources.connect_use_device_code
import org.jetbrains.compose.resources.stringResource

// The real direct-connect gate (frontend.md §5/§6), restyled as the branded Twitch [ConnectModal] (the
// first-login variant: "Welcome to NomNomzBot", Twitch-purple ambient backdrop + CTA, Terms/Privacy
// footer). Two ways onboard against a backend — UNCHANGED behaviour, purely presentational:
//   1. CLICK a bot mDNS-discovered on the LAN (zero-friction onboarding) — rendered inside the card.
//   2. TYPE a backend URL and hit the brand CTA ("Connect with twitch").
// Either way the controller runs the live OAuth dance (desktop loopback / web redirect) → captures the
// JWT → validates it via /me → the App gate flips to the shell. No mock. The discovered list is empty on
// web (single-origin, served by its own bot), so that section simply renders nothing there. When the
// device-code path is used, the live "enter this code" panel renders inside the SAME card (the modal's
// content slot) and the CTA is suppressed while approval is pending.
@Composable
fun ConnectScreen(controller: ConnectController) {
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

    val awaiting: ConnectStatus.AwaitingApproval? = status as? ConnectStatus.AwaitingApproval

    ConnectModal(
        provider = ConnectProviders.Twitch,
        // First Twitch login → the welcome heading rather than the generic "Link your Twitch account".
        heading = Res.string.connect_modal_heading_first_login,
        // The brand CTA IS the "Connect with twitch" action; suppressed once a device login is awaiting
        // approval (the in-card device panel drives it from there) and while a connect is in flight.
        onCta = if (busy) null else { { scope.launch { controller.connect() } } },
        // First login: no Back (this is the entry point), and the Terms/Privacy footer is shown.
        onBack = null,
        showTerms = true,
    ) {
        val spacing = LocalSpacing.current

        Column(
            modifier = Modifier.fillMaxWidth(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            if (awaiting != null) {
                // The login is live — focus the card on the code to approve at twitch.tv/activate.
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

                // Make the account unambiguous: this is the streamer's OWN account, and the bot is a
                // separate, optional account added later — never forced here.
                AccountHint()

                // Secondary path: force the device-code login even when the redirect flow is available. It needs
                // no registered redirect URL on the Twitch app, so it's the resilient way in when the redirect
                // callback isn't set up yet — the operator approves a code at twitch.tv/activate.
                TextButton(
                    onClick = { scope.launch { controller.connect(forceDevice = true) } },
                    enabled = !busy,
                ) {
                    Text(stringResource(Res.string.connect_use_device_code))
                }
            }

            ConnectStatusRow(status = status)
        }
    }
}

// The streamer-account clarification line, shown under the URL field and in the device panel.
@Composable
private fun AccountHint() {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Text(
        text = stringResource(Res.string.connect_account_hint),
        style = typography.xs,
        color = tokens.mutedForeground,
        textAlign = TextAlign.Center,
    )
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
// tab on web. Rendered inside the modal card (its content slot).
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
            .background(tokens.background)
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
        DeviceOpenButton(verificationUri = verificationUri, uriHandler = uriHandler)
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
            Text(
                text = stringResource(Res.string.connect_device_waiting),
                style = typography.xs,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
        }

        AccountHint()
    }
}

// The "open Twitch to approve" action of the device panel — the Twitch-branded CTA opening the pre-filled
// verification URL in the system browser / a new tab.
@Composable
private fun DeviceOpenButton(verificationUri: String, uriHandler: UriHandler) {
    Button(
        onClick = { uriHandler.openUri(verificationUri) },
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(text = stringResource(Res.string.connect_device_open))
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
