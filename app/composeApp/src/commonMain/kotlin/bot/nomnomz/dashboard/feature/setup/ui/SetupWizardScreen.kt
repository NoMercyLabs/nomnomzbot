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
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.LinkedText
import bot.nomnomz.dashboard.core.designsystem.component.StepState
import bot.nomnomz.dashboard.core.designsystem.component.Stepper
import bot.nomnomz.dashboard.core.designsystem.component.StepperStep
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
import nomnomzbot.composeapp.generated.resources.setup_secret_hide
import nomnomzbot.composeapp.generated.resources.setup_secret_show
import nomnomzbot.composeapp.generated.resources.setup_copy_action
import nomnomzbot.composeapp.generated.resources.setup_copy_done
import nomnomzbot.composeapp.generated.resources.setup_error_bot
import nomnomzbot.composeapp.generated.resources.setup_error_missing_fields
import nomnomzbot.composeapp.generated.resources.setup_error_save
import nomnomzbot.composeapp.generated.resources.setup_error_signin
import nomnomzbot.composeapp.generated.resources.setup_nav_back
import nomnomzbot.composeapp.generated.resources.setup_nav_next
import nomnomzbot.composeapp.generated.resources.setup_nav_skip
import nomnomzbot.composeapp.generated.resources.setup_optional_badge
import nomnomzbot.composeapp.generated.resources.setup_review_not_ready
import nomnomzbot.composeapp.generated.resources.setup_review_status_done
import nomnomzbot.composeapp.generated.resources.setup_review_status_pending
import nomnomzbot.composeapp.generated.resources.setup_review_status_skipped
import nomnomzbot.composeapp.generated.resources.setup_review_subtitle
import nomnomzbot.composeapp.generated.resources.setup_review_title
import nomnomzbot.composeapp.generated.resources.setup_signing_in
import nomnomzbot.composeapp.generated.resources.setup_step_counter
import nomnomzbot.composeapp.generated.resources.setup_step_done
import nomnomzbot.composeapp.generated.resources.setup_subtitle
import nomnomzbot.composeapp.generated.resources.setup_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The first-run Setup wizard (frontend.md §5; the setup rung between Connect and the shell), reworked from
// one overflowing screen into a multi-step stepper flow. The backend wizard is still the source of truth —
// each backend SetupStep is ONE stepper step, followed by a trailing review/finish step — so the split
// stays in sync if the backend adds/removes a provider step. The frame is fixed: a stepper header + a
// Back/Next footer never scroll; only a step's content area scrolls IF that single step overflows. The
// step's own Save/Authorize buttons run the REAL credential saves + bot authorization through the injected
// controller; nothing about completion is hardcoded — a step is "complete" only when the backend's re-read
// says so. The final step runs the existing finish() (streamer OAuth → POST …/setup/complete).
private const val ACTION_SAVE_CREDENTIALS: String = "save_credentials"
private const val ACTION_OAUTH_REDIRECT: String = "oauth_redirect"
private const val FIELD_TYPE_PASSWORD: String = "password"
private const val SIGNING_IN: String = "__signing_in__"

@Composable
fun SetupWizardScreen(controller: SetupController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val state: SetupState by controller.state.collectAsStateWithLifecycle()

    LaunchedEffect(Unit) { controller.load() }

    Box(
        modifier = Modifier.fillMaxSize().background(tokens.background),
        contentAlignment = Alignment.TopCenter,
    ) {
        when (val current: SetupState = state) {
            SetupState.Loading -> SetupCentered { CircularProgressIndicator(modifier = Modifier.size(spacing.s6)) }

            is SetupState.Error -> SetupCentered { LoadError(detail = current.detail, controller = controller) }

            is SetupState.Steps -> SetupStepsFrame(controller, current)
        }
    }
}

// The fixed wizard frame: a stepper header that never scrolls, a content area that takes the remaining
// height (and scrolls only when a single step's own content overflows), and a Back/Next footer pinned at
// the bottom. Width is constrained by the spacing scale so the form never sprawls.
@Composable
private fun SetupStepsFrame(controller: SetupController, state: SetupState.Steps) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier
            .widthIn(max = spacing.s24 * 6)
            .fillMaxSize()
            .padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        // ── Fixed header ────────────────────────────────────────────────────────
        Text(text = stringResource(Res.string.setup_title), style = typography.xl2, color = tokens.foreground)
        Text(text = stringResource(Res.string.setup_subtitle), style = typography.sm, color = tokens.mutedForeground)
        Stepper(steps = stepperModel(state), modifier = Modifier.fillMaxWidth())
        Text(
            text = stringResource(Res.string.setup_step_counter, state.currentStep + 1, state.steps.size + 1),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        // ── Scrolls only if THIS step overflows ─────────────────────────────────
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            val step: SetupStep? = state.currentBackendStep
            if (step != null) {
                StepPanel(
                    controller = controller,
                    step = step,
                    busy = state.busy == step.key,
                    error = state.error,
                    onValueChange = { fieldKey, value -> controller.onFieldChange(step.key, fieldKey, value) },
                    onSave = { scope.launch { controller.saveCredentials(step) } },
                    onConnectBot = { scope.launch { controller.connectBot() } },
                )
            } else {
                ReviewPanel(state = state)
            }
        }

        // ── Fixed footer ────────────────────────────────────────────────────────
        SetupFooter(controller = controller, state = state, scope = scope)
    }
}

// The backend steps + the trailing review step, each tagged with its visual state relative to the current
// position. A backend step shows Completed once the backend's re-read marks it complete; the review step is
// Completed only when the whole flow is ready (all required steps configured).
@Composable
private fun stepperModel(state: SetupState.Steps): List<StepperStep> {
    val backend: List<StepperStep> =
        state.steps.mapIndexed { index, step ->
            StepperStep(
                label = localizedOrBackend(SetupCopy.stepTitle(step.key), step.title),
                state = stepStateFor(complete = step.complete, index = index, current = state.currentStep),
            )
        }
    val review: StepperStep =
        StepperStep(
            label = stringResource(Res.string.setup_review_title),
            state = stepStateFor(complete = state.ready, index = state.reviewIndex, current = state.currentStep),
        )
    return backend + review
}

private fun stepStateFor(complete: Boolean, index: Int, current: Int): StepState =
    when {
        complete -> StepState.Completed
        index == current -> StepState.Current
        else -> StepState.Upcoming
    }

// One backend step's panel — the same card the old screen rendered, minus the per-step header chrome that
// the stepper now carries. Shows the step's copy + instructions, then the action (credential fields or the
// bot authorize button). When already complete, the inputs collapse to the "Done" line.
@Composable
private fun StepPanel(
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
            Text(
                text = localizedOrBackend(SetupCopy.stepTitle(step.key), step.title),
                style = typography.lg,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f).padding(end = spacing.s2),
            )
            if (step.complete) {
                Text(
                    text = stringResource(Res.string.setup_step_done),
                    style = typography.sm,
                    color = tokens.primary,
                    maxLines = 1,
                )
            } else if (!step.required) {
                Text(
                    text = stringResource(Res.string.setup_optional_badge),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }

        Text(
            text = localizedOrBackend(SetupCopy.stepDescription(step.key), step.description),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        // The redirect URI the operator registers on the external console — the same value an instruction
        // line names — surfaced once as a copy-to-clipboard chip so it's pasted verbatim, never retyped.
        val copyAffordance: CopyAffordance =
            CopyAffordance(
                copy = stringResource(Res.string.setup_copy_action),
                copied = stringResource(Res.string.setup_copy_done),
            )

        step.instructions.forEachIndexed { index, backendLine ->
            val line: String = localizedOrBackend(SetupCopy.instruction(step.key, index), backendLine)
            // URLs in the localized line are clickable (open the external console). A redirect/callback URL
            // the operator must PASTE gets its own copy chip — read from the BACKEND line (always carries the
            // exact URI) so the chip stays correct even though the localized prose says "shown below".
            LinkedText(text = line, style = typography.xs, color = tokens.mutedForeground)
            copyableUrl(backendLine)?.let { url ->
                CopyValue(value = url, copyLabel = copyAffordance.copy, copiedLabel = copyAffordance.copied)
            }
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

// The review/finish step: a compact per-step status list, then the readiness hint when a required step is
// still pending. The finish ACTION lives in the footer's Next button (gated by state.ready).
@Composable
private fun ReviewPanel(state: SetupState.Steps) {
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
        Text(text = stringResource(Res.string.setup_review_title), style = typography.lg, color = tokens.cardForeground)
        Text(text = stringResource(Res.string.setup_review_subtitle), style = typography.sm, color = tokens.mutedForeground)

        state.steps.forEach { step ->
            ReviewRow(step = step)
        }

        if (!state.ready) {
            ErrorText(stringResource(Res.string.setup_review_not_ready))
        }
        if (state.error is SetupError.SignIn) {
            ErrorText(stringResource(Res.string.setup_error_signin))
        }
    }
}

// One review line: the step's title plus its outcome — configured, skipped (optional + not configured), or
// pending (required + not configured). The status colour comes from the tokens, never a raw hex.
@Composable
private fun ReviewRow(step: SetupStep) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val (label: String, color) =
        when {
            step.complete -> stringResource(Res.string.setup_review_status_done) to tokens.primary
            !step.required -> stringResource(Res.string.setup_review_status_skipped) to tokens.mutedForeground
            else -> stringResource(Res.string.setup_review_status_pending) to tokens.destructive
        }

    Row(
        modifier = Modifier.fillMaxWidth().padding(top = spacing.s0_5),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = step.title,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f).padding(end = spacing.s2),
        )
        Text(text = label, style = typography.xs, color = color, maxLines = 1)
    }
}

// The pinned footer: Back on the left (hidden on the first step), and on the right either Next (advance to
// the next step), Skip (advance past an optional, not-yet-complete step), or Finish (on the review step —
// runs the existing finish()). Next is disabled until the current step's required inputs are satisfied.
@Composable
private fun SetupFooter(
    controller: SetupController,
    state: SetupState.Steps,
    scope: CoroutineScope,
) {
    val spacing = LocalSpacing.current
    val signingIn: Boolean = state.busy == SIGNING_IN

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (state.currentStep > 0) {
            OutlinedButton(
                onClick = { controller.back() },
                enabled = state.busy == null,
                modifier = Modifier.weight(1f),
            ) {
                Text(stringResource(Res.string.setup_nav_back))
            }
        }

        if (state.onReviewStep) {
            Button(
                onClick = { scope.launch { controller.finish() } },
                enabled = state.ready && state.busy == null,
                modifier = Modifier.weight(1f),
            ) {
                Text(stringResource(if (signingIn) Res.string.setup_signing_in else Res.string.setup_action_continue))
            }
        } else {
            val skippable: Boolean = state.currentBackendStep?.let { !it.complete && !it.required } == true
            Button(
                onClick = { controller.next() },
                enabled = state.canAdvance && state.busy == null,
                modifier = Modifier.weight(1f),
            ) {
                Text(stringResource(if (skippable) Res.string.setup_nav_skip else Res.string.setup_nav_next))
            }
        }
    }
}

@Composable
private fun SetupCentered(content: @Composable () -> Unit) {
    val spacing = LocalSpacing.current
    Box(
        modifier = Modifier.fillMaxSize().padding(spacing.s8),
        contentAlignment = Alignment.Center,
    ) {
        content()
    }
}

@Composable
private fun LoadError(detail: String, controller: SetupController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2), horizontalAlignment = Alignment.CenterHorizontally) {
        Text(text = detail, style = typography.sm, color = tokens.destructive)
        TextButton(onClick = { scope.launch { controller.load() } }) {
            Text(stringResource(Res.string.setup_action_retry))
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
                stepKey = step.key,
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
    stepKey: String,
    field: SetupField,
    value: String,
    enabled: Boolean,
    onValueChange: (String) -> Unit,
) {
    val label: String = localizedOrBackend(SetupCopy.fieldLabel(stepKey, field.key), field.label)
    // Help is nullable on both sides: prefer the localized help, then the backend's (itself nullable), then
    // show none — so a field with no help anywhere simply renders without a supporting line.
    val help: String? = SetupCopy.fieldHelp(stepKey, field.key)?.let { stringResource(it) } ?: field.help

    // Secret fields (client secrets, tokens) render masked by default so a streamer can't leak a key on camera,
    // with a joined Show/Hide toggle to reveal it when they need to check what they pasted.
    val isSecret: Boolean = field.type == FIELD_TYPE_PASSWORD
    var revealed: Boolean by remember(field.key) { mutableStateOf(false) }

    AppTextField(
        value = value,
        onValueChange = onValueChange,
        label = label,
        modifier = Modifier.fillMaxWidth(),
        enabled = enabled,
        supportingText = help,
        visualTransformation =
            if (isSecret && !revealed) PasswordVisualTransformation() else VisualTransformation.None,
        trailingIcon =
            if (isSecret) {
                {
                    TextButton(onClick = { revealed = !revealed }, enabled = enabled) {
                        Text(
                            stringResource(
                                if (revealed) Res.string.setup_secret_hide
                                else Res.string.setup_secret_show,
                            ),
                        )
                    }
                }
            } else {
                null
            },
    )
}

// ── Localization + copy helpers ────────────────────────────────────────────────

/** A step/field's localized copy when its i18n key exists, else the backend's English (the self-describing fallback). */
@Composable
private fun localizedOrBackend(key: StringResource?, backend: String): String =
    if (key != null) stringResource(key) else backend

/** The already-localized copy/copied labels a [CopyValue] flips between, resolved once per step. */
private data class CopyAffordance(val copy: String, val copied: String)

/**
 * The single URL in [line] the operator must PASTE into an external console — a redirect/callback URI the
 * bot owns — or null when the line only carries an external console URL to visit (which [LinkedText] already
 * makes clickable). Matching on the callback path keeps "copy this" attached to the paste-value, never the
 * "go here" links, with no hardcoded per-step knowledge.
 */
private fun copyableUrl(line: String): String? {
    val start: Int = indexOfHttp(line)
    if (start < 0) return null
    var end: Int = start
    while (end < line.length && !line[end].isWhitespace()) end++
    while (end > start && line[end - 1] in COPY_TRAILING_PUNCTUATION) end--
    val url: String = line.substring(start, end)
    return if (url.contains("/callback")) url else null
}

private fun indexOfHttp(text: String): Int {
    val https: Int = text.indexOf("https://")
    val http: Int = text.indexOf("http://")
    return when {
        https < 0 -> http
        http < 0 -> https
        else -> minOf(https, http)
    }
}

private val COPY_TRAILING_PUNCTUATION: Set<Char> = setOf('.', ',', ';', ':', ')', ']', '}', '!', '?', '"', '\'')

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
