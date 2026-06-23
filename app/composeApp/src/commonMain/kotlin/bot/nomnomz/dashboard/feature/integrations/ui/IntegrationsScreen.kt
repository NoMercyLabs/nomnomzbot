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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.integrations.state.BusyTarget
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsController
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsState
import bot.nomnomz.dashboard.feature.integrations.state.ProviderConnection
import kotlinx.coroutines.launch
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.integrations_action_connect
import nomnomzbot.composeapp.generated.resources.integrations_action_disconnect
import nomnomzbot.composeapp.generated.resources.integrations_action_reauth
import nomnomzbot.composeapp.generated.resources.integrations_action_retry
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
import nomnomzbot.composeapp.generated.resources.integrations_title
import nomnomzbot.composeapp.generated.resources.integrations_youtube_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_youtube_title

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

@Composable
fun IntegrationsScreen(controller: IntegrationsController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    val state: IntegrationsState by controller.state.collectAsStateWithLifecycle()

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.integrations_title),
            style = typography.xl2,
            color = tokens.foreground,
        )
        Text(
            text = stringResource(Res.string.integrations_subtitle),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        when (val current: IntegrationsState = state) {
            IntegrationsState.Loading ->
                Box(modifier = Modifier.fillMaxWidth().padding(spacing.s8), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
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
                    BotRow(
                        connected = current.bot.connected,
                        accountName = current.bot.accountName,
                        busy = current.busy is BusyTarget.Bot,
                        onConnect = { scope.launch { controller.connectBot() } },
                    )
                    ProviderRow(
                        title = Res.string.integrations_spotify_title,
                        subtitle = Res.string.integrations_spotify_subtitle,
                        connection = current.providers.forProvider(SPOTIFY),
                        busy = current.busy.isProvider(SPOTIFY),
                        onConnect = { scope.launch { controller.connectProvider(SPOTIFY, SPOTIFY_SCOPE_SET) } },
                        onDisconnect = { scope.launch { controller.disconnect(SPOTIFY) } },
                    )
                    ProviderRow(
                        title = Res.string.integrations_youtube_title,
                        subtitle = Res.string.integrations_youtube_subtitle,
                        connection = current.providers.forProvider(YOUTUBE),
                        busy = current.busy.isProvider(YOUTUBE),
                        onConnect = { scope.launch { controller.connectProvider(YOUTUBE, YOUTUBE_SCOPE_SET) } },
                        onDisconnect = { scope.launch { controller.disconnect(YOUTUBE) } },
                    )
                    ProviderRow(
                        title = Res.string.integrations_discord_title,
                        subtitle = Res.string.integrations_discord_subtitle,
                        connection = current.providers.forProvider(DISCORD),
                        busy = current.busy.isProvider(DISCORD),
                        onConnect = { scope.launch { controller.connectDiscord() } },
                        onDisconnect = { scope.launch { controller.disconnect(DISCORD) } },
                    )
                }
        }
    }
}

@Composable
private fun BotRow(
    connected: Boolean,
    accountName: String?,
    busy: Boolean,
    onConnect: () -> Unit,
) {
    IntegrationCard(
        title = stringResource(Res.string.integrations_bot_title),
        subtitle = stringResource(Res.string.integrations_bot_subtitle),
        connected = connected,
        accountName = accountName,
        needsReauth = false,
        busy = busy,
        // The bot is connected via the platform OAuth; there is no app-side disconnect on this screen.
        onConnect = onConnect,
        onDisconnect = null,
    )
}

@Composable
private fun ProviderRow(
    title: StringResource,
    subtitle: StringResource,
    connection: ProviderConnection?,
    busy: Boolean,
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
        Column(modifier = Modifier.padding(end = spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
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
            CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                if (connected && onDisconnect != null) {
                    OutlinedButton(onClick = onDisconnect) {
                        Text(stringResource(Res.string.integrations_action_disconnect))
                    }
                }
                if (!connected || needsReauth) {
                    Button(onClick = onConnect) {
                        Text(
                            stringResource(
                                if (needsReauth) Res.string.integrations_action_reauth
                                else Res.string.integrations_action_connect
                            )
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
