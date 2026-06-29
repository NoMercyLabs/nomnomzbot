// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.ui

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
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.animation.AnimatedVisibility
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
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.LinkedText
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.settings.state.SaveError
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsState
import bot.nomnomz.dashboard.feature.setup.ui.SetupCopy
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_copy_action
import nomnomzbot.composeapp.generated.resources.setup_copy_done
import nomnomzbot.composeapp.generated.resources.setup_secret_hide
import nomnomzbot.composeapp.generated.resources.setup_secret_show
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_description
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_instruction_1
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_instruction_2
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_instruction_3
import nomnomzbot.composeapp.generated.resources.twitch_app_clientId_label
import nomnomzbot.composeapp.generated.resources.twitch_app_clientSecret_help
import nomnomzbot.composeapp.generated.resources.twitch_app_clientSecret_label
import nomnomzbot.composeapp.generated.resources.twitch_app_clientSecret_optional
import nomnomzbot.composeapp.generated.resources.twitch_app_error
import nomnomzbot.composeapp.generated.resources.twitch_app_missing_client_id
import nomnomzbot.composeapp.generated.resources.twitch_app_overwrite_cancel
import nomnomzbot.composeapp.generated.resources.twitch_app_overwrite_confirm
import nomnomzbot.composeapp.generated.resources.twitch_app_overwrite_message
import nomnomzbot.composeapp.generated.resources.twitch_app_overwrite_title
import nomnomzbot.composeapp.generated.resources.twitch_app_redirect_label
import nomnomzbot.composeapp.generated.resources.twitch_app_retry
import nomnomzbot.composeapp.generated.resources.twitch_app_save
import nomnomzbot.composeapp.generated.resources.twitch_app_save_error
import nomnomzbot.composeapp.generated.resources.twitch_app_saving
import nomnomzbot.composeapp.generated.resources.twitch_app_section_description
import nomnomzbot.composeapp.generated.resources.twitch_app_section_title
import nomnomzbot.composeapp.generated.resources.twitch_app_state_configured
import nomnomzbot.composeapp.generated.resources.twitch_app_state_shared
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The dashboard "Twitch application" credential card (Settings page). It REUSES the first-run wizard's copy +
// guide for the `twitch_app` step — the same explanations, the same "how to create a Twitch app" instructions
// (via [SetupCopy]), and the same EXACT OAuth redirect URL to register (as a copy chip) — so a signed-in admin
// can configure or repoint their PERSONAL Twitch client (BYOC) from inside the dashboard rather than by
// hand-editing config. Save writes through the SAME credential endpoint the wizard uses
// (PUT …/setup/credentials/twitch, gated to admins post-setup); the outcome surfaces on the shared feedback host.
//
// The client id is the only required field; the secret is OPTIONAL — the bot signs in with a device code on the
// id alone, and a secret only unlocks the smoother one-tap redirect flow. Editing live OAuth credentials is
// consequential, so overwriting an already-configured app confirms first.
@Composable
fun TwitchAppCredentialsCard(controller: TwitchAppCredentialsController, manage: ManageDecision) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val state: TwitchAppCredentialsState by controller.state.collectAsStateWithLifecycle()
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
            text = stringResource(Res.string.twitch_app_section_title),
            style = typography.xl,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.twitch_app_section_description),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        when (val current: TwitchAppCredentialsState = state) {
            TwitchAppCredentialsState.Loading ->
                Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
                }

            is TwitchAppCredentialsState.Error ->
                ErrorRow(detail = current.detail, onRetry = { scope.launch { controller.load() } })

            is TwitchAppCredentialsState.Ready ->
                ReadyBody(
                    state = current,
                    manage = manage,
                    onSave = { id, secret -> scope.launch { controller.save(id, secret) } },
                )
        }
    }
}

@Composable
private fun ReadyBody(
    state: TwitchAppCredentialsState.Ready,
    manage: ManageDecision,
    onSave: (clientId: String, clientSecret: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Local form, re-seeded whenever a reload swaps the Ready state (e.g. after a save), so it always starts
    // empty for a fresh entry rather than echoing back a stored value. Credentials are write-only here — the
    // backend never returns the secret, so the card collects new values rather than editing existing ones.
    var clientId: String by remember(state.configured) { mutableStateOf("") }
    var clientSecret: String by remember(state.configured) { mutableStateOf("") }
    var confirmOverwrite: Boolean by remember { mutableStateOf(false) }

    // When already configured, collapse the form by default — the user doesn't need to re-enter credentials
    // just to view the integrations page. The Edit button expands it on demand.
    var expanded: Boolean by remember(state.configured) { mutableStateOf(!state.configured) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            StateBanner(configured = state.configured, modifier = Modifier.weight(1f))
            if (state.configured && !expanded) {
                TextButton(onClick = { expanded = true }) {
                    Text(
                        text = "Edit",
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                }
            }
        }

        AnimatedVisibility(visible = expanded) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                Guide(redirectUrl = state.redirectUrl)

                // Editing live OAuth credentials is owner-level (frontend-ia.md §5 — token custody is Broadcaster);
                // below that floor the fields and Save go read-only with reason via the gated SaveBar.
                val editEnabled: Boolean = !state.saving && manage.isAllowed

                ClientIdField(
                    value = clientId,
                    onValueChange = { clientId = it },
                    invalid = state.saveError is SaveError.MissingClientId,
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
                    // Overwriting an already-configured Twitch app touches the live OAuth path, so it confirms first;
                    // a first-time configuration saves straight through.
                    onSave = {
                        if (state.configured) confirmOverwrite = true else onSave(clientId, clientSecret)
                    },
                )
            }
        }
    }

    if (confirmOverwrite) {
        ConfirmDialog(
            title = stringResource(Res.string.twitch_app_overwrite_title),
            message = stringResource(Res.string.twitch_app_overwrite_message),
            confirmLabel = stringResource(Res.string.twitch_app_overwrite_confirm),
            dismissLabel = stringResource(Res.string.twitch_app_overwrite_cancel),
            destructive = true,
            onConfirm = {
                confirmOverwrite = false
                onSave(clientId, clientSecret)
            },
            onDismiss = { confirmOverwrite = false },
        )
    }
}

// The current configuration line: whether a personal Twitch app is set, or the bot runs on the shared client.
@Composable
private fun StateBanner(configured: Boolean, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String =
        stringResource(
            if (configured) Res.string.twitch_app_state_configured else Res.string.twitch_app_state_shared
        )

    Row(
        modifier = modifier
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .padding(spacing.s3)
            // One node for screen readers: the configuration state as a single phrase.
            .clearAndSetSemantics { contentDescription = label },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Box(
            modifier = Modifier
                .size(spacing.s2)
                .clip(CircleShape)
                .background(if (configured) tokens.primary else tokens.mutedForeground),
        )
        Text(text = label, style = typography.sm, color = tokens.foreground)
    }
}

// The wizard's `twitch_app` guide, reused verbatim: the same description, the same numbered instructions (via
// [SetupCopy], so the prose stays DRY with the onboarding flow), and the exact redirect URL as a copy chip.
@Composable
private fun Guide(redirectUrl: String?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = localizedTwitchStep(SetupCopy.stepDescription(STEP_TWITCH), Res.string.setup_step_twitch_app_description),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        instructionLines().forEach { line ->
            LinkedText(text = line, style = typography.xs, color = tokens.mutedForeground)
        }

        if (redirectUrl != null) {
            Text(
                text = stringResource(Res.string.twitch_app_redirect_label),
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

// The three `twitch_app` instruction lines, localized through the wizard's [SetupCopy] (its keys 0..2),
// falling back to the bundled wizard strings — the same copy the onboarding step shows.
@Composable
private fun instructionLines(): List<String> =
    listOf(
        localizedTwitchStep(SetupCopy.instruction(STEP_TWITCH, 0), Res.string.setup_step_twitch_app_instruction_1),
        localizedTwitchStep(SetupCopy.instruction(STEP_TWITCH, 1), Res.string.setup_step_twitch_app_instruction_2),
        localizedTwitchStep(SetupCopy.instruction(STEP_TWITCH, 2), Res.string.setup_step_twitch_app_instruction_3),
    )

@Composable
private fun ClientIdField(
    value: String,
    onValueChange: (String) -> Unit,
    invalid: Boolean,
    enabled: Boolean,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        isError = invalid,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(stringResource(Res.string.twitch_app_clientId_label)) },
        supportingText =
            if (invalid) {
                { Text(stringResource(Res.string.twitch_app_missing_client_id)) }
            } else {
                null
            },
    )
}

// The OPTIONAL client secret. The label carries the "(optional)" marker and the supporting line explains the
// device-code-without-a-secret behavior + what the secret unlocks — so the user understands they can skip it.
@Composable
private fun ClientSecretField(
    value: String,
    onValueChange: (String) -> Unit,
    enabled: Boolean,
) {
    var revealed: Boolean by remember { mutableStateOf(false) }

    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        label = {
            Text(
                stringResource(
                    Res.string.twitch_app_clientSecret_optional,
                    stringResource(Res.string.twitch_app_clientSecret_label),
                )
            )
        },
        supportingText = { Text(stringResource(Res.string.twitch_app_clientSecret_help)) },
        // A secret is masked by default so it can't leak on camera, with a Show/Hide reveal toggle.
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
private fun SaveBar(
    state: TwitchAppCredentialsState.Ready,
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
        // The inline save-failure detail (the feedback host also announces it); the success path clears and
        // reloads, so a "saved" line isn't held here.
        when (val error: SaveError? = state.saveError) {
            is SaveError.Backend ->
                Text(
                    text = stringResource(Res.string.twitch_app_save_error, error.detail),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.weight(1f),
                )
            else -> Box(modifier = Modifier.weight(1f))
        }

        if (state.saving) {
            val savingLabel: String = stringResource(Res.string.twitch_app_saving)
            CircularProgressIndicator(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = savingLabel },
            )
        } else {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = onSave,
                    enabled = enabled,
                    colors = ButtonDefaults.buttonColors(
                        disabledContainerColor = tokens.muted,
                        disabledContentColor = tokens.mutedForeground,
                    ),
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(stringResource(Res.string.twitch_app_save))
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
            text = stringResource(Res.string.twitch_app_error, detail),
            style = typography.sm,
            color = tokens.destructive,
            modifier = Modifier.weight(1f),
        )
        TextButton(onClick = onRetry) { Text(stringResource(Res.string.twitch_app_retry)) }
    }
}

// A step's localized copy when the wizard's i18n key exists, else the bundled wizard string (the same
// self-describing fallback the wizard screen uses).
@Composable
private fun localizedTwitchStep(key: StringResource?, fallback: StringResource): String =
    stringResource(key ?: fallback)

// The backend's stable step key for the Twitch-app step, mirrored from the wizard so [SetupCopy] resolves
// the right copy.
private const val STEP_TWITCH: String = "twitch_app"
