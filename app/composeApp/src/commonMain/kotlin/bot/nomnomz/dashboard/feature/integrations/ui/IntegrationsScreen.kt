// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalUriHandler
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.EventSubSubscription
import bot.nomnomz.dashboard.core.network.MissingScope
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.connect.ui.ConnectModal
import bot.nomnomz.dashboard.feature.connect.ui.ConnectProvider
import bot.nomnomz.dashboard.feature.connect.ui.ConnectProviders
import bot.nomnomz.dashboard.feature.integrations.state.BotDeviceState
import bot.nomnomz.dashboard.feature.integrations.state.BusyTarget
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsController
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsState
import bot.nomnomz.dashboard.feature.integrations.state.ProviderConnection
import bot.nomnomz.dashboard.feature.integrations.state.RegrantState
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import bot.nomnomz.dashboard.feature.settings.ui.TwitchAppCredentialsCard
import kotlinx.coroutines.launch
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.integrations_action_connect
import nomnomzbot.composeapp.generated.resources.integrations_action_disconnect
import nomnomzbot.composeapp.generated.resources.integrations_action_reauth
import nomnomzbot.composeapp.generated.resources.integrations_action_retry
import nomnomzbot.composeapp.generated.resources.integrations_bot_device_cancel
import nomnomzbot.composeapp.generated.resources.integrations_eventsub_empty
import nomnomzbot.composeapp.generated.resources.integrations_eventsub_reconcile
import nomnomzbot.composeapp.generated.resources.integrations_eventsub_status_disabled
import nomnomzbot.composeapp.generated.resources.integrations_eventsub_status_enabled
import nomnomzbot.composeapp.generated.resources.integrations_eventsub_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_eventsub_title
import nomnomzbot.composeapp.generated.resources.integrations_bot_device_instruction
import nomnomzbot.composeapp.generated.resources.integrations_bot_device_open
import nomnomzbot.composeapp.generated.resources.integrations_bot_device_title
import nomnomzbot.composeapp.generated.resources.integrations_bot_device_waiting
import nomnomzbot.composeapp.generated.resources.integrations_bot_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_bot_title
import nomnomzbot.composeapp.generated.resources.integrations_discord_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_discord_title
import nomnomzbot.composeapp.generated.resources.integrations_provider_connected_as
import nomnomzbot.composeapp.generated.resources.integrations_spotify_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_spotify_title
import nomnomzbot.composeapp.generated.resources.integrations_status_connected
import nomnomzbot.composeapp.generated.resources.integrations_status_not_connected
import nomnomzbot.composeapp.generated.resources.integrations_subtitle
import nomnomzbot.composeapp.generated.resources.shell_nav_integrations
import nomnomzbot.composeapp.generated.resources.integrations_youtube_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_youtube_title
import nomnomzbot.composeapp.generated.resources.permissions_banner_action
import nomnomzbot.composeapp.generated.resources.permissions_banner_body
import nomnomzbot.composeapp.generated.resources.permissions_banner_body_generic
import nomnomzbot.composeapp.generated.resources.permissions_banner_title
import nomnomzbot.composeapp.generated.resources.permissions_feature_fallback
import nomnomzbot.composeapp.generated.resources.permissions_regrant_cancel
import nomnomzbot.composeapp.generated.resources.permissions_regrant_instruction
import nomnomzbot.composeapp.generated.resources.permissions_regrant_open
import nomnomzbot.composeapp.generated.resources.permissions_regrant_title
import nomnomzbot.composeapp.generated.resources.permissions_regrant_waiting

// The integrations / onboarding screen (frontend.md §5). Lists the bot account + the three
// providers with live connection status read from the backend, and runs the REAL connect/disconnect
// flows through the injected controller. No mocked "connected" state — every row reflects the
// authoritative backend status after each action.
//
// The provider connect scope-sets are the broadest manageable set per provider (external-API full
// management coverage): Spotify playback control, YouTube channel manage.
private const val SPOTIFY: String = "spotify"
private const val YOUTUBE: String = "youtube"
private const val DISCORD: String = "discord"
private const val SPOTIFY_SCOPE_SET: String = "spotify.playback"
private const val YOUTUBE_SCOPE_SET: String = "youtube.manage"

// Which provider's branded connect modal is currently open over the integrations list (null = none). Only
// the three brand-described providers route through the modal; the bot account connects inline.
private enum class ConnectModalProvider { Spotify, YouTube, Discord }

// The open connect modal's stage: the branded intro (tap "Continue with X") or, when the provider's app
// client is not yet registered, the BYOC credential step (register the client, then proceed to OAuth).
private sealed interface ConnectStage {
    data object Intro : ConnectStage

    data class Credentials(val saving: Boolean = false, val missingClientId: Boolean = false) : ConnectStage
}

// The stable provider key (snake-free lowercase, matching the backend) for a modal provider.
private fun ConnectModalProvider.providerKey(): String =
    when (this) {
        ConnectModalProvider.Spotify -> SPOTIFY
        ConnectModalProvider.YouTube -> YOUTUBE
        ConnectModalProvider.Discord -> DISCORD
    }

// The human, brand-cased display name woven into the BYOC credential copy ("Register your Spotify app").
// These are proper nouns (brand names), identical across locales, so they are NOT translated — deriving the
// name here keeps it correct regardless of how a locale phrases the logo's accessibility label.
private fun ConnectModalProvider.displayName(): String =
    when (this) {
        ConnectModalProvider.Spotify -> "Spotify"
        ConnectModalProvider.YouTube -> "YouTube"
        ConnectModalProvider.Discord -> "Discord"
    }

@Composable
fun IntegrationsScreen(
    controller: IntegrationsController,
    twitchAppController: TwitchAppCredentialsController,
    role: ManagementRole?,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    // Integrations gates at its Broadcaster floor — token custody is owner-level (frontend-ia.md §3 Pinned).
    // The page's read floor is also Broadcaster, so normally only a Broadcaster reaches it; the gate still
    // defends the connect/disconnect/grant writes for any break-glass case (§7). Backend re-checks every write.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Integrations)

    val state: IntegrationsState by controller.state.collectAsStateWithLifecycle()

    LaunchedEffect(Unit) { controller.load() }

    // The open per-provider connect modal, if any, and its stage. Tapping a provider row's Connect opens it
    // at the branded Intro; the brand CTA then either proceeds straight to OAuth (client already registered)
    // or advances to the BYOC credential step (client not registered yet) before OAuth. Back/dismiss closes it.
    var openModal: ConnectModalProvider? by remember { mutableStateOf(null) }
    var stage: ConnectStage by remember { mutableStateOf(ConnectStage.Intro) }

    // Launch the real OAuth connect for the modal's provider, then close the modal. The controller re-reads the
    // authoritative status; nothing is faked here.
    val launchOAuth: (ConnectModalProvider) -> Unit = { which: ConnectModalProvider ->
        openModal = null
        stage = ConnectStage.Intro
        scope.launch {
            when (which) {
                ConnectModalProvider.Spotify -> controller.connectProvider(SPOTIFY, SPOTIFY_SCOPE_SET)
                ConnectModalProvider.YouTube -> controller.connectProvider(YOUTUBE, YOUTUBE_SCOPE_SET)
                ConnectModalProvider.Discord -> controller.connectDiscord()
            }
        }
    }

    Box(modifier = Modifier.fillMaxSize()) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .verticalScroll(rememberScrollState())
            .background(tokens.background)
            .padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_integrations),
            subtitle = stringResource(Res.string.integrations_subtitle),
        )

        when (val current: IntegrationsState = state) {
            IntegrationsState.Loading ->
                Box(modifier = Modifier.fillMaxWidth().padding(spacing.s8), contentAlignment = Alignment.Center) {
                    Spinner(modifier = Modifier.size(spacing.s6))
                }

            is IntegrationsState.Error ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Text(text = current.detail, style = typography.sm, color = tokens.destructive)
                    TextButton(onClick = { scope.launch { controller.load() } }) {
                        Text(stringResource(Res.string.integrations_action_retry))
                    }
                }

            is IntegrationsState.Ready ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                    // The streamer-token scope health surface: a re-grant panel while one is in flight, else a
                    // persistent banner whenever a feature needs a Twitch permission the token is missing.
                    current.regrant?.let { regrant: RegrantState ->
                        RegrantPanel(
                            regrant = regrant,
                            onCancel = { controller.cancelRegrant() },
                        )
                    }
                    if (current.regrant == null && current.missingScopes.isNotEmpty()) {
                        MissingScopesBanner(
                            missing = current.missingScopes,
                            manage = manage,
                            onGrant = { scope.launch { controller.regrantScopes() } },
                        )
                    }

                    // The Twitch application is an integration like any other (it's the credential the whole
                    // platform OAuth runs on), so it lives here with the bot account — not in Settings.
                    TwitchAppCredentialsCard(controller = twitchAppController, manage = manage)

                    // The secret-free bot device login panel while one is awaiting approval at twitch.tv/activate.
                    current.botDevice?.let { device: BotDeviceState ->
                        BotDevicePanel(
                            device = device,
                            onCancel = { controller.cancelBotDevice() },
                        )
                    }
                    BotRow(
                        connected = current.bot.connected,
                        accountName = current.bot.accountName,
                        busy = current.busy is BusyTarget.Bot || current.botDevice != null,
                        manage = manage,
                        onConnect = { scope.launch { controller.connectBot() } },
                        onDisconnect = { scope.launch { controller.disconnectBot() } },
                    )
                    ProviderRow(
                        title = Res.string.integrations_spotify_title,
                        subtitle = Res.string.integrations_spotify_subtitle,
                        connection = current.providers.forProvider(SPOTIFY),
                        busy = current.busy.isProvider(SPOTIFY),
                        manage = manage,
                        // Open the branded connect modal (at the Intro stage) rather than connecting inline.
                        onConnect = { openModal = ConnectModalProvider.Spotify; stage = ConnectStage.Intro },
                        onDisconnect = { scope.launch { controller.disconnect(SPOTIFY) } },
                    )
                    ProviderRow(
                        title = Res.string.integrations_youtube_title,
                        subtitle = Res.string.integrations_youtube_subtitle,
                        connection = current.providers.forProvider(YOUTUBE),
                        busy = current.busy.isProvider(YOUTUBE),
                        manage = manage,
                        onConnect = { openModal = ConnectModalProvider.YouTube; stage = ConnectStage.Intro },
                        onDisconnect = { scope.launch { controller.disconnect(YOUTUBE) } },
                    )
                    ProviderRow(
                        title = Res.string.integrations_discord_title,
                        subtitle = Res.string.integrations_discord_subtitle,
                        connection = current.providers.forProvider(DISCORD),
                        busy = current.busy.isProvider(DISCORD),
                        manage = manage,
                        onConnect = { openModal = ConnectModalProvider.Discord; stage = ConnectStage.Intro },
                        onDisconnect = { scope.launch { controller.disconnect(DISCORD) } },
                    )
                    EventSubSubscriptionsSection(
                        subscriptions = current.eventSubSubscriptions,
                        manage = manage,
                        onReconcile = { scope.launch { controller.reconcileEventSub() } },
                    )
                }
        }
    }

        // The per-provider branded connect modal, overlaid over the list when a provider's Connect is tapped.
        // The register-then-login flow: the brand CTA first ensures the provider's app client is registered —
        // if it is, it proceeds straight to OAuth; if not (Spotify/Discord with no shared client, or YouTube
        // whose client status is unknown), it advances to the in-modal BYOC credential card, which saves the
        // operator's own client and THEN proceeds to OAuth. Back/dismiss closes the modal without connecting.
        openModal?.let { which: ConnectModalProvider ->
            val descriptor: ConnectProvider =
                when (which) {
                    ConnectModalProvider.Spotify -> ConnectProviders.Spotify
                    ConnectModalProvider.YouTube -> ConnectProviders.YouTube
                    ConnectModalProvider.Discord -> ConnectProviders.Discord
                }
            val providerKey: String = which.providerKey()
            val displayName: String = which.displayName()

            when (val current: ConnectStage = stage) {
                ConnectStage.Intro ->
                    ConnectModal(
                        provider = descriptor,
                        // The CTA branches on client registration: registered → OAuth now; not registered (or
                        // unknown) → advance to the credential step in the SAME card.
                        onCta = {
                            if (controller.clientRegistered(providerKey) == true) {
                                launchOAuth(which)
                            } else {
                                stage = ConnectStage.Credentials()
                            }
                        },
                        onBack = { openModal = null; stage = ConnectStage.Intro },
                    )

                is ConnectStage.Credentials ->
                    ConnectModal(
                        provider = descriptor,
                        // No brand CTA on the credential step — the card's own "Save and continue" drives it.
                        onCta = null,
                        onBack = { stage = ConnectStage.Intro },
                    ) {
                        ProviderCredentialsCard(
                            providerDisplayName = displayName,
                            redirectUrl = controller.integrationRedirectUrl(providerKey),
                            saving = current.saving,
                            missingClientId = current.missingClientId,
                            onSave = { clientId: String, clientSecret: String ->
                                if (clientId.trim().isEmpty()) {
                                    stage = ConnectStage.Credentials(missingClientId = true)
                                } else {
                                    stage = ConnectStage.Credentials(saving = true)
                                    scope.launch {
                                        val saved: Boolean =
                                            controller.saveProviderCredentials(
                                                provider = providerKey,
                                                clientId = clientId,
                                                clientSecret = clientSecret,
                                            )
                                        // On success the client is registered server-side → proceed to OAuth;
                                        // on failure stay on the step (the feedback host carries the detail).
                                        if (saved) launchOAuth(which)
                                        else stage = ConnectStage.Credentials()
                                    }
                                }
                            },
                        )
                    }
            }
        }
    }
}

@Composable
private fun BotRow(
    connected: Boolean,
    accountName: String?,
    busy: Boolean,
    manage: ManageDecision,
    onConnect: () -> Unit,
    onDisconnect: () -> Unit,
) {
    IntegrationCard(
        title = stringResource(Res.string.integrations_bot_title),
        subtitle = stringResource(Res.string.integrations_bot_subtitle),
        connected = connected,
        accountName = accountName,
        needsReauth = false,
        busy = busy,
        manage = manage,
        onConnect = onConnect,
        // Disconnect is admin-gated server-side (the ManageGate hides it for non-admins). Disconnecting then
        // connecting a different account is how the operator CHANGES the bot.
        onDisconnect = if (connected) onDisconnect else null,
    )
}

@Composable
private fun ProviderRow(
    title: StringResource,
    subtitle: StringResource,
    connection: ProviderConnection?,
    busy: Boolean,
    manage: ManageDecision,
    onConnect: () -> Unit,
    onDisconnect: () -> Unit,
) {
    IntegrationCard(
        title = stringResource(title),
        subtitle = stringResource(subtitle),
        connected = connection?.connected == true,
        accountName = connection?.accountName,
        needsReauth = connection?.needsReauth == true,
        busy = busy,
        manage = manage,
        onConnect = onConnect,
        onDisconnect = if (connection?.connected == true) onDisconnect else null,
    )
}

@Composable
private fun IntegrationCard(
    title: String,
    subtitle: String,
    connected: Boolean,
    accountName: String?,
    needsReauth: Boolean,
    busy: Boolean,
    manage: ManageDecision,
    onConnect: () -> Unit,
    onDisconnect: (() -> Unit)?,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card, RoundedCornerShape(tokens.radius.lg))
            .border(width = spacing.s0_5 / 2, color = tokens.border, shape = RoundedCornerShape(tokens.radius.lg))
            .padding(horizontal = spacing.s4, vertical = spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(modifier = Modifier.weight(1f).padding(end = spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
            Text(text = title, style = typography.base, color = tokens.cardForeground)
            Text(text = subtitle, style = typography.sm, color = tokens.mutedForeground)
            Text(
                text =
                    if (connected) {
                        accountName?.let { stringResource(Res.string.integrations_provider_connected_as, it) }
                            ?: stringResource(Res.string.integrations_status_connected)
                    } else {
                        stringResource(Res.string.integrations_status_not_connected)
                    },
                style = typography.xs,
                color = if (connected) tokens.primary else tokens.mutedForeground,
            )
        }

        if (busy) {
            Spinner(modifier = Modifier.size(spacing.s6))
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                if (connected && onDisconnect != null) {
                    ManageGate(decision = manage) { enabled ->
                        OutlinedButton(onClick = onDisconnect, enabled = enabled) {
                            Text(stringResource(Res.string.integrations_action_disconnect), maxLines = 1)
                        }
                    }
                }
                if (!connected || needsReauth) {
                    ManageGate(decision = manage) { enabled ->
                        Button(
                            onClick = onConnect,
                            enabled = enabled,
                        ) {
                            Text(
                                text =
                                    stringResource(
                                        if (needsReauth) Res.string.integrations_action_reauth
                                        else Res.string.integrations_action_connect
                                    ),
                                maxLines = 1,
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MissingScopesBanner(missing: List<MissingScope>, manage: ManageDecision, onGrant: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // One scope → name it + what it unlocks; several → a clear count. Either way the action is one click.
    val body: String =
        if (missing.size == 1) {
            val only: MissingScope = missing.first()
            val feature: String =
                only.features.firstOrNull() ?: stringResource(Res.string.permissions_feature_fallback)
            stringResource(Res.string.permissions_banner_body, only.scope, feature)
        } else {
            stringResource(Res.string.permissions_banner_body_generic, missing.size)
        }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card, RoundedCornerShape(tokens.radius.lg))
            .border(width = spacing.s0_5 / 2, color = tokens.primary, shape = RoundedCornerShape(tokens.radius.lg))
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.permissions_banner_title),
            style = typography.base,
            color = tokens.cardForeground,
        )
        Text(text = body, style = typography.sm, color = tokens.mutedForeground)
        ManageGate(decision = manage, modifier = Modifier.align(Alignment.Start)) { enabled ->
            Button(
                onClick = onGrant,
                enabled = enabled,
            ) {
                Text(stringResource(Res.string.permissions_banner_action), maxLines = 1)
            }
        }
    }
}

@Composable
private fun RegrantPanel(regrant: RegrantState, onCancel: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val uriHandler = LocalUriHandler.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card, RoundedCornerShape(tokens.radius.lg))
            .border(width = spacing.s0_5 / 2, color = tokens.primary, shape = RoundedCornerShape(tokens.radius.lg))
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.permissions_regrant_title),
            style = typography.base,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.permissions_regrant_instruction),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = regrant.userCode,
            style = typography.xl2,
            color = tokens.primary,
        )
        Text(
            text = stringResource(Res.string.permissions_regrant_waiting),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Button(onClick = { uriHandler.openUri(regrant.verificationUri) }) {
                Text(stringResource(Res.string.permissions_regrant_open), maxLines = 1)
            }
            TextButton(onClick = onCancel) {
                Text(stringResource(Res.string.permissions_regrant_cancel), maxLines = 1)
            }
        }
    }
}

// The secret-free bot device-login panel: show the user code + the link to twitch.tv/activate while the
// controller polls for approval. Mirrors [RegrantPanel] — the same shadcn card/primitives, its own copy.
@Composable
private fun BotDevicePanel(device: BotDeviceState, onCancel: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val uriHandler = LocalUriHandler.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card, RoundedCornerShape(tokens.radius.lg))
            .border(width = spacing.s0_5 / 2, color = tokens.primary, shape = RoundedCornerShape(tokens.radius.lg))
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.integrations_bot_device_title),
            style = typography.base,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.integrations_bot_device_instruction),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = device.userCode,
            style = typography.xl2,
            color = tokens.primary,
        )
        Text(
            text = stringResource(Res.string.integrations_bot_device_waiting),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Button(onClick = { uriHandler.openUri(device.verificationUri) }) {
                Text(stringResource(Res.string.integrations_bot_device_open), maxLines = 1)
            }
            TextButton(onClick = onCancel) {
                Text(stringResource(Res.string.integrations_bot_device_cancel), maxLines = 1)
            }
        }
    }
}

// A card showing all active Twitch EventSub subscriptions for the channel, with a one-click reconcile
// that repairs any drift between the bot's subscription database and the live Twitch state.
@Composable
private fun EventSubSubscriptionsSection(
    subscriptions: List<EventSubSubscription>,
    manage: ManageDecision,
    onReconcile: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier =
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.lg))
                .background(tokens.card)
                .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(Res.string.integrations_eventsub_title),
                    style = typography.base,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(Res.string.integrations_eventsub_subtitle),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            ManageGate(decision = manage) { enabled: Boolean ->
                OutlinedButton(onClick = onReconcile, enabled = enabled) {
                    Text(stringResource(Res.string.integrations_eventsub_reconcile), maxLines = 1)
                }
            }
        }

        if (subscriptions.isEmpty()) {
            Text(
                text = stringResource(Res.string.integrations_eventsub_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .heightIn(max = spacing.s16 * 4)
                        .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
            ) {
                subscriptions.forEach { sub: EventSubSubscription ->
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s0_5),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            text = sub.eventType,
                            style = typography.xs,
                            color = tokens.foreground,
                            modifier = Modifier.weight(1f),
                        )
                        Text(
                            text =
                                stringResource(
                                    if (sub.enabled) Res.string.integrations_eventsub_status_enabled
                                    else Res.string.integrations_eventsub_status_disabled
                                ),
                            style = typography.xs,
                            color = if (sub.enabled) tokens.primary else tokens.mutedForeground,
                        )
                    }
                }
            }
        }
    }
}

private fun List<ProviderConnection>.forProvider(provider: String): ProviderConnection? =
    firstOrNull { it.provider.equals(provider, ignoreCase = true) }

private fun BusyTarget?.isProvider(provider: String): Boolean =
    this is BusyTarget.Provider && this.provider.equals(provider, ignoreCase = true)
