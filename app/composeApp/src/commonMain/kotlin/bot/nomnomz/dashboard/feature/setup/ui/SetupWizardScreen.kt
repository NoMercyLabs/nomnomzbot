// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.ui

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
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.SetupField
import bot.nomnomz.dashboard.core.network.SetupStep
import bot.nomnomz.dashboard.feature.setup.state.SetupController
import bot.nomnomz.dashboard.feature.setup.state.SetupError
import bot.nomnomz.dashboard.feature.setup.state.SetupState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_action_connect_bot
import nomnomzbot.composeapp.generated.resources.setup_action_continue
import nomnomzbot.composeapp.generated.resources.setup_action_retry
import nomnomzbot.composeapp.generated.resources.setup_action_save
import nomnomzbot.composeapp.generated.resources.setup_error_bot
import nomnomzbot.composeapp.generated.resources.setup_error_missing_fields
import nomnomzbot.composeapp.generated.resources.setup_error_save
import nomnomzbot.composeapp.generated.resources.setup_error_signin
import nomnomzbot.composeapp.generated.resources.setup_optional_badge
import nomnomzbot.composeapp.generated.resources.setup_signing_in
import nomnomzbot.composeapp.generated.resources.setup_step_done
import nomnomzbot.composeapp.generated.resources.setup_subtitle
import nomnomzbot.composeapp.generated.resources.setup_title
import org.jetbrains.compose.resources.stringResource

// The first-run Setup wizard (frontend.md §5; the setup rung between Connect and the shell). It renders
// the entire flow from the backend's self-describing wizard (SetupState.Steps.steps) — each step's copy,
// instructions, the exact redirect URI to register, its input fields, and its live completion state —
// then runs the REAL credential saves + bot authorization through the injected controller. Nothing is
// hardcoded about the steps; the backend is the source of truth, so the wizard stays in sync with it.
private const val ACTION_SAVE_CREDENTIALS: String = "save_credentials"
private const val ACTION_OAUTH_REDIRECT: String = "oauth_redirect"
private const val FIELD_TYPE_PASSWORD: String = "password"
private const val SIGNING_IN: String = "__signing_in__"

@Composable
fun SetupWizardScreen(controller: SetupController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    val state: SetupState by controller.state.collectAsStateWithLifecycle()

    LaunchedEffect(Unit) { controller.load() }

    Box(
        modifier = Modifier.fillMaxSize().background(tokens.background),
        contentAlignment = Alignment.TopCenter,
    ) {
        Column(
            modifier = Modifier
                .widthIn(max = spacing.s24 * 6)
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(spacing.s6),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            Text(
                text = stringResource(Res.string.setup_title),
                style = typography.xl2,
                color = tokens.foreground,
            )
            Text(
                text = stringResource(Res.string.setup_subtitle),
                style = typography.sm,
                color = tokens.mutedForeground,
            )

            when (val current: SetupState = state) {
                SetupState.Loading ->
                    Box(modifier = Modifier.fillMaxWidth().padding(spacing.s8), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
                    }

                is SetupState.Error ->
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        Text(text = current.detail, style = typography.sm, color = tokens.destructive)
                        TextButton(onClick = { scope.launch { controller.load() } }) {
                            Text(stringResource(Res.string.setup_action_retry))
                        }
                    }

                is SetupState.Steps -> SetupSteps(controller, current, scope)
            }
        }
    }
}

@Composable
private fun SetupSteps(
    controller: SetupController,
    state: SetupState.Steps,
    scope: CoroutineScope,
) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        state.steps.forEach { step ->
            StepCard(
                controller = controller,
                step = step,
                busy = state.busy == step.key,
                error = state.error,
                onValueChange = { fieldKey, value -> controller.onFieldChange(step.key, fieldKey, value) },
                onSave = { scope.launch { controller.saveCredentials(step) } },
                onConnectBot = { scope.launch { controller.connectBot() } },
            )
        }

        Button(
            onClick = { scope.launch { controller.finish() } },
            enabled = state.ready && state.busy == null,
            modifier = Modifier.fillMaxWidth(),
        ) {
            if (state.busy == SIGNING_IN) {
                Text(stringResource(Res.string.setup_signing_in))
            } else {
                Text(stringResource(Res.string.setup_action_continue))
            }
        }

        if (state.error is SetupError.SignIn) {
            ErrorText(stringResource(Res.string.setup_error_signin))
        }
    }
}

@Composable
private fun StepCard(
    controller: SetupController,
    step: SetupStep,
    busy: Boolean,
    error: SetupError?,
    onValueChange: (fieldKey: String, value: String) -> Unit,
    onSave: () -> Unit,
    onConnectBot: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card, RoundedCornerShape(tokens.radius.lg))
            .border(width = spacing.s0_5 / 2, color = tokens.border, shape = RoundedCornerShape(tokens.radius.lg))
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = step.title, style = typography.lg, color = tokens.cardForeground)
            if (step.complete) {
                Text(text = stringResource(Res.string.setup_step_done), style = typography.sm, color = tokens.primary)
            } else if (!step.required) {
                Text(text = stringResource(Res.string.setup_optional_badge), style = typography.xs, color = tokens.mutedForeground)
            }
        }

        Text(text = step.description, style = typography.sm, color = tokens.mutedForeground)

        step.instructions.forEach { line ->
            Text(text = line, style = typography.xs, color = tokens.mutedForeground)
        }

        if (!step.complete) {
            when (step.action.type) {
                ACTION_SAVE_CREDENTIALS ->
                    CredentialFields(
                        controller = controller,
                        step = step,
                        busy = busy,
                        error = error,
                        onValueChange = onValueChange,
                        onSave = onSave,
                    )

                ACTION_OAUTH_REDIRECT ->
                    BotConnect(busy = busy, error = error, onConnectBot = onConnectBot)
            }
        }
    }
}

@Composable
private fun CredentialFields(
    controller: SetupController,
    step: SetupStep,
    busy: Boolean,
    error: SetupError?,
    onValueChange: (fieldKey: String, value: String) -> Unit,
    onSave: () -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        step.fields.forEach { field ->
            CredentialField(
                field = field,
                value = controller.valueOf(step.key, field.key),
                enabled = !busy,
                onValueChange = { onValueChange(field.key, it) },
            )
        }

        if (error is SetupError.MissingFields && error.stepKey == step.key) {
            ErrorText(stringResource(Res.string.setup_error_missing_fields))
        }
        if (error is SetupError.Save && error.stepKey == step.key) {
            ErrorText(stringResource(Res.string.setup_error_save, error.detail))
        }

        if (busy) {
            CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
        } else {
            Button(onClick = onSave, modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(Res.string.setup_action_save))
            }
        }
    }
}

@Composable
private fun CredentialField(
    field: SetupField,
    value: String,
    enabled: Boolean,
    onValueChange: (String) -> Unit,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(field.label) },
        supportingText = field.help?.let { { Text(it) } },
        visualTransformation =
            if (field.type == FIELD_TYPE_PASSWORD) PasswordVisualTransformation() else VisualTransformation.None,
    )
}

@Composable
private fun BotConnect(
    busy: Boolean,
    error: SetupError?,
    onConnectBot: () -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        if (error is SetupError.Bot) {
            ErrorText(stringResource(Res.string.setup_error_bot, error.detail))
        }
        if (busy) {
            CircularProgressIndicator(modifier = Modifier.size(spacing.s6))
        } else {
            Button(onClick = onConnectBot, modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(Res.string.setup_action_connect_bot))
            }
        }
    }
}

@Composable
private fun ErrorText(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Text(text = text, style = typography.sm, color = tokens.destructive)
}
