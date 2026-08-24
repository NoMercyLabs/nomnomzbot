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

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.LinkedText
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.RevealableSecretField
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.integrations.state.SpotifyChannelCredentialsController
import bot.nomnomz.dashboard.feature.integrations.state.SpotifyChannelCredentialsState
import bot.nomnomz.dashboard.feature.integrations.state.SpotifySaveError
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_copy_action
import nomnomzbot.composeapp.generated.resources.setup_copy_done
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clear
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clear_cancel
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clear_confirm
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clear_message
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clear_title
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clientId_label
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clientSecret_help
import nomnomzbot.composeapp.generated.resources.spotify_credentials_clientSecret_label
import nomnomzbot.composeapp.generated.resources.spotify_credentials_description
import nomnomzbot.composeapp.generated.resources.spotify_credentials_edit
import nomnomzbot.composeapp.generated.resources.spotify_credentials_error
import nomnomzbot.composeapp.generated.resources.spotify_credentials_instruction_1
import nomnomzbot.composeapp.generated.resources.spotify_credentials_instruction_2
import nomnomzbot.composeapp.generated.resources.spotify_credentials_instruction_3
import nomnomzbot.composeapp.generated.resources.spotify_credentials_missing_client_id
import nomnomzbot.composeapp.generated.resources.spotify_credentials_redirect_label
import nomnomzbot.composeapp.generated.resources.spotify_credentials_retry
import nomnomzbot.composeapp.generated.resources.spotify_credentials_save
import nomnomzbot.composeapp.generated.resources.spotify_credentials_save_error
import nomnomzbot.composeapp.generated.resources.spotify_credentials_saving
import nomnomzbot.composeapp.generated.resources.spotify_credentials_secret_configured
import nomnomzbot.composeapp.generated.resources.spotify_credentials_secret_not_configured
import nomnomzbot.composeapp.generated.resources.spotify_credentials_section_description
import nomnomzbot.composeapp.generated.resources.spotify_credentials_section_title
import nomnomzbot.composeapp.generated.resources.spotify_credentials_state_default
import nomnomzbot.composeapp.generated.resources.spotify_credentials_state_own
import org.jetbrains.compose.resources.stringResource

// The channel-scoped Spotify BYOC credential card (Integrations page, S-BYOC-spotify-b). Lets a streamer
// point `!sr` song requests at HER OWN Spotify app instead of the app-level default — mirrors the structure
// of [bot.nomnomz.dashboard.feature.settings.ui.TwitchAppCredentialsCard] (state banner naming which
// credentials are ACTIVE, a collapsible guide + redirect chip + id/secret fields, a gated save bar) rather
// than inventing a second credential-card idiom.
//
// The whole point of the card is the own-vs-default DISTINCTION — [SpotifyChannelCredentialsState.Ready.
// usingOwnCredentials] always renders, never hidden. The secret is write-only: it is masked on entry via
// [RevealableSecretField] and NEVER shown back — only [SpotifyChannelCredentialsState.Ready.hasClientSecret]
// (a boolean) is ever read from the backend.
//
// The card itself does not decide visibility — the caller (IntegrationsScreen) renders it only when the
// caller holds `integration:read`; the write actions here are gated by the passed-in [manage] decision
// (resolved from `integration:write`), which disables-with-reason rather than hiding.
@Composable
fun SpotifyChannelCredentialsCard(controller: SpotifyChannelCredentialsController, manage: ManageDecision) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val state: SpotifyChannelCredentialsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.spotify_credentials_section_title),
            style = typography.xl,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.spotify_credentials_section_description),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        when (val current: SpotifyChannelCredentialsState = state) {
            SpotifyChannelCredentialsState.Loading ->
                Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    Spinner(modifier = Modifier.size(spacing.s6))
                }

            is SpotifyChannelCredentialsState.Error ->
                ErrorRow(detail = current.detail, onRetry = { scope.launch { controller.load() } })

            is SpotifyChannelCredentialsState.Ready ->
                ReadyBody(
                    state = current,
                    manage = manage,
                    onSave = { id, secret -> scope.launch { controller.save(id, secret) } },
                    onClear = { scope.launch { controller.clear() } },
                )
        }
    }
}

@Composable
private fun ReadyBody(
    state: SpotifyChannelCredentialsState.Ready,
    manage: ManageDecision,
    onSave: (clientId: String, clientSecret: String) -> Unit,
    onClear: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // A fresh, empty form on every state swap (e.g. after a save) — the secret is write-only, so the card
    // never echoes a stored value back.
    var clientId: String by remember(state.usingOwnCredentials) { mutableStateOf("") }
    var clientSecret: String by remember(state.usingOwnCredentials) { mutableStateOf("") }
    var confirmClear: Boolean by remember { mutableStateOf(false) }

    // Collapse the form by default once own credentials are set — Edit expands it; a fresh channel (no own
    // credentials yet) starts expanded so the operator can configure immediately.
    var expanded: Boolean by remember(state.usingOwnCredentials) { mutableStateOf(!state.usingOwnCredentials) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            StateBanner(usingOwnCredentials = state.usingOwnCredentials, modifier = Modifier.weight(1f))
            if (state.usingOwnCredentials && !expanded) {
                TextButton(onClick = { expanded = true }) {
                    Text(
                        text = stringResource(Res.string.spotify_credentials_edit),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                }
            }
        }

        if (state.usingOwnCredentials) {
            SecretStatusLine(hasClientSecret = state.hasClientSecret)
        }

        AnimatedVisibility(visible = expanded) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                Guide(redirectUrl = state.redirectUrl)

                val editEnabled: Boolean = !state.saving && manage.isAllowed

                ClientIdField(
                    value = clientId,
                    onValueChange = { clientId = it },
                    invalid = state.saveError is SpotifySaveError.MissingClientId,
                    enabled = editEnabled,
                )
                ClientSecretField(
                    value = clientSecret,
                    onValueChange = { clientSecret = it },
                    enabled = editEnabled,
                )

                SaveBar(
                    state = state,
                    manage = manage,
                    onSave = { onSave(clientId, clientSecret) },
                )
            }
        }

        // Clearing falls back to the app-level default (if any) — only offered once own credentials exist,
        // and it confirms first since it changes which Spotify account song requests run against.
        if (state.usingOwnCredentials && !state.saving) {
            ManageGate(decision = manage, modifier = Modifier.align(Alignment.Start)) { enabled ->
                TextButton(onClick = { confirmClear = true }, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.spotify_credentials_clear),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                }
            }
        }
    }

    if (confirmClear) {
        ConfirmDialog(
            title = stringResource(Res.string.spotify_credentials_clear_title),
            message = stringResource(Res.string.spotify_credentials_clear_message),
            confirmLabel = stringResource(Res.string.spotify_credentials_clear_confirm),
            dismissLabel = stringResource(Res.string.spotify_credentials_clear_cancel),
            destructive = true,
            onConfirm = {
                confirmClear = false
                onClear()
            },
            onDismiss = { confirmClear = false },
        )
    }
}

// The current-source line: whether THIS channel's own Spotify app is active, or it falls back to the
// app-level default — the entire reason this card exists, so it always renders, never collapses away.
@Composable
private fun StateBanner(usingOwnCredentials: Boolean, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String =
        stringResource(
            if (usingOwnCredentials) Res.string.spotify_credentials_state_own
            else Res.string.spotify_credentials_state_default
        )

    Row(
        modifier = modifier
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .padding(spacing.s3)
            .clearAndSetSemantics { contentDescription = label },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Box(
            modifier = Modifier
                .size(spacing.s2)
                .clip(CircleShape)
                .background(if (usingOwnCredentials) tokens.primary else tokens.mutedForeground),
        )
        Text(text = label, style = typography.sm, color = tokens.foreground)
    }
}

// Whether a secret is configured for the channel's own app — the ONLY secret-related signal the backend ever
// returns; the value itself never appears here.
@Composable
private fun SecretStatusLine(hasClientSecret: Boolean) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Text(
        text =
            stringResource(
                if (hasClientSecret) Res.string.spotify_credentials_secret_configured
                else Res.string.spotify_credentials_secret_not_configured
            ),
        style = typography.xs,
        color = tokens.mutedForeground,
    )
}

// The guide: a short description, three numbered instructions, then the exact redirect URI to register — the
// value the app actually knows (derived from the active backend), never a hardcoded address.
@Composable
private fun Guide(redirectUrl: String?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.spotify_credentials_description),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        listOf(
            Res.string.spotify_credentials_instruction_1,
            Res.string.spotify_credentials_instruction_2,
            Res.string.spotify_credentials_instruction_3,
        ).forEach { instruction ->
            LinkedText(text = stringResource(instruction), style = typography.xs, color = tokens.mutedForeground)
        }

        if (redirectUrl != null) {
            Text(
                text = stringResource(Res.string.spotify_credentials_redirect_label),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            CopyValue(
                value = redirectUrl,
                copyLabel = stringResource(Res.string.setup_copy_action),
                copiedLabel = stringResource(Res.string.setup_copy_done),
            )
        }
    }
}

@Composable
private fun ClientIdField(
    value: String,
    onValueChange: (String) -> Unit,
    invalid: Boolean,
    enabled: Boolean,
) {
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        isError = invalid,
        modifier = Modifier.fillMaxWidth(),
        label = stringResource(Res.string.spotify_credentials_clientId_label),
        errorText = stringResource(Res.string.spotify_credentials_missing_client_id),
    )
}

@Composable
private fun ClientSecretField(
    value: String,
    onValueChange: (String) -> Unit,
    enabled: Boolean,
) {
    RevealableSecretField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
        label = stringResource(Res.string.spotify_credentials_clientSecret_label),
        supportingText = stringResource(Res.string.spotify_credentials_clientSecret_help),
    )
}

@Composable
private fun SaveBar(
    state: SpotifyChannelCredentialsState.Ready,
    manage: ManageDecision,
    onSave: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        when (val error: SpotifySaveError? = state.saveError) {
            is SpotifySaveError.Backend ->
                Text(
                    text = stringResource(Res.string.spotify_credentials_save_error, error.detail),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.weight(1f),
                )
            else -> Box(modifier = Modifier.weight(1f))
        }

        if (state.saving) {
            val savingLabel: String = stringResource(Res.string.spotify_credentials_saving)
            Spinner(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = savingLabel },
            )
        } else {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = onSave,
                    enabled = enabled,
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(stringResource(Res.string.spotify_credentials_save))
                }
            }
        }
    }
}

@Composable
private fun ErrorRow(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = stringResource(Res.string.spotify_credentials_error, detail),
            style = typography.sm,
            color = tokens.destructive,
            modifier = Modifier.weight(1f),
        )
        TextButton(onClick = onRetry) { Text(stringResource(Res.string.spotify_credentials_retry)) }
    }
}
