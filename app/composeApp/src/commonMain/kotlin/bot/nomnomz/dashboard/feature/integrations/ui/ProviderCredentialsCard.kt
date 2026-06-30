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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.provider_credentials_clientId_label
import nomnomzbot.composeapp.generated.resources.provider_credentials_clientSecret_help
import nomnomzbot.composeapp.generated.resources.provider_credentials_clientSecret_label
import nomnomzbot.composeapp.generated.resources.provider_credentials_description
import nomnomzbot.composeapp.generated.resources.provider_credentials_missing_client_id
import nomnomzbot.composeapp.generated.resources.provider_credentials_redirect_label
import nomnomzbot.composeapp.generated.resources.provider_credentials_save
import nomnomzbot.composeapp.generated.resources.provider_credentials_saving
import nomnomzbot.composeapp.generated.resources.setup_copy_action
import nomnomzbot.composeapp.generated.resources.setup_copy_done
import nomnomzbot.composeapp.generated.resources.setup_secret_hide
import nomnomzbot.composeapp.generated.resources.setup_secret_show
import org.jetbrains.compose.resources.stringResource

// The generic per-provider BYOC app-credential form — the register-client step of the register-then-login
// flow. Spotify / YouTube / Discord ship with NO shared client (unlike Twitch's shared `aajly3` default), so
// the FIRST time a user connects one, the operator must register their OWN app: this card collects the Client
// ID + (optional) Secret, shows the exact OAuth redirect URL to paste into the provider's console, and saves
// through the wizard's per-provider credential endpoint (IntegrationsController.saveProviderCredentials →
// PUT …/setup/credentials/{provider}). On a successful save the host proceeds straight to OAuth.
//
// This is the dashboard-side generalization of the Twitch settings BYOC card (TwitchAppCredentialsCard): the
// same structure (state line is implicit here — the card only shows while UNregistered — guide + redirect chip
// + id/secret fields + save), parameterized by provider rather than Twitch-specific. It is purely
// presentational: it owns no API; the host wires [onSave] to the controller and re-checks registration.
@Composable
fun ProviderCredentialsCard(
    providerDisplayName: String,
    redirectUrl: String?,
    saving: Boolean,
    missingClientId: Boolean,
    onSave: (clientId: String, clientSecret: String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val spacing = LocalSpacing.current

    var clientId: String by remember { mutableStateOf("") }
    var clientSecret: String by remember { mutableStateOf("") }

    Column(
        modifier = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Guide(providerDisplayName = providerDisplayName, redirectUrl = redirectUrl)

        ClientIdField(
            value = clientId,
            onValueChange = { clientId = it },
            invalid = missingClientId,
            enabled = !saving,
        )
        ClientSecretField(
            value = clientSecret,
            onValueChange = { clientSecret = it },
            providerDisplayName = providerDisplayName,
            enabled = !saving,
        )

        SaveBar(saving = saving, onSave = { onSave(clientId, clientSecret) })
    }
}

// The provider-specific guidance: a one-line description naming the provider, then the exact OAuth redirect URL
// the operator must register on the provider's developer console, as a copy chip (rooted at the active backend
// so the address is correct for THIS connection). The redirect chip hides when no backend base URL is active.
@Composable
private fun Guide(providerDisplayName: String, redirectUrl: String?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.provider_credentials_description, providerDisplayName),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        if (redirectUrl != null) {
            Text(
                text = stringResource(Res.string.provider_credentials_redirect_label),
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
        label = stringResource(Res.string.provider_credentials_clientId_label),
        errorText = stringResource(Res.string.provider_credentials_missing_client_id),
    )
}

// The client secret, masked by default with a Show/Hide reveal so it can't leak on camera.
@Composable
private fun ClientSecretField(
    value: String,
    onValueChange: (String) -> Unit,
    providerDisplayName: String,
    enabled: Boolean,
) {
    var revealed: Boolean by remember { mutableStateOf(false) }

    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
        label = stringResource(Res.string.provider_credentials_clientSecret_label),
        supportingText = stringResource(Res.string.provider_credentials_clientSecret_help, providerDisplayName),
        visualTransformation =
            if (revealed) VisualTransformation.None else PasswordVisualTransformation(),
        trailingIcon = {
            TextButton(onClick = { revealed = !revealed }, enabled = enabled) {
                Text(
                    stringResource(
                        if (revealed) Res.string.setup_secret_hide else Res.string.setup_secret_show
                    )
                )
            }
        },
    )
}

@Composable
private fun SaveBar(saving: Boolean, onSave: () -> Unit) {
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.End,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (saving) {
            val savingLabel: String = stringResource(Res.string.provider_credentials_saving)
            CircularProgressIndicator(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = savingLabel },
            )
        } else {
            Button(onClick = onSave, modifier = Modifier.wrapContentWidth()) {
                Text(stringResource(Res.string.provider_credentials_save))
            }
        }
    }
}
