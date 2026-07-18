// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.automation.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AutomationScope
import bot.nomnomz.dashboard.core.network.AutomationToken
import bot.nomnomz.dashboard.core.network.IssuedAutomationToken
import bot.nomnomz.dashboard.core.network.PairingCode
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.feature.automation.state.AutomationController
import bot.nomnomz.dashboard.feature.automation.state.AutomationUiState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import kotlin.time.Duration.Companion.days
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.automation_action_error
import nomnomzbot.composeapp.generated.resources.automation_cancel
import nomnomzbot.composeapp.generated.resources.automation_create
import nomnomzbot.composeapp.generated.resources.automation_create_title
import nomnomzbot.composeapp.generated.resources.automation_empty
import nomnomzbot.composeapp.generated.resources.automation_error
import nomnomzbot.composeapp.generated.resources.automation_expires
import nomnomzbot.composeapp.generated.resources.automation_expiry_1y
import nomnomzbot.composeapp.generated.resources.automation_expiry_30d
import nomnomzbot.composeapp.generated.resources.automation_expiry_90d
import nomnomzbot.composeapp.generated.resources.automation_expiry_label
import nomnomzbot.composeapp.generated.resources.automation_expiry_never
import nomnomzbot.composeapp.generated.resources.automation_last_used
import nomnomzbot.composeapp.generated.resources.automation_loading
import nomnomzbot.composeapp.generated.resources.automation_name_label
import nomnomzbot.composeapp.generated.resources.automation_never_used
import nomnomzbot.composeapp.generated.resources.automation_new_token
import nomnomzbot.composeapp.generated.resources.automation_no_expiry
import nomnomzbot.composeapp.generated.resources.automation_pair_cancel
import nomnomzbot.composeapp.generated.resources.automation_pair_chat_scope
import nomnomzbot.composeapp.generated.resources.automation_pair_create
import nomnomzbot.composeapp.generated.resources.automation_pair_device
import nomnomzbot.composeapp.generated.resources.automation_pair_label
import nomnomzbot.composeapp.generated.resources.automation_pair_title
import nomnomzbot.composeapp.generated.resources.automation_paircode_close
import nomnomzbot.composeapp.generated.resources.automation_paircode_copied
import nomnomzbot.composeapp.generated.resources.automation_paircode_copy
import nomnomzbot.composeapp.generated.resources.automation_paircode_expired
import nomnomzbot.composeapp.generated.resources.automation_paircode_expires_in
import nomnomzbot.composeapp.generated.resources.automation_paircode_instructions
import nomnomzbot.composeapp.generated.resources.automation_paircode_title
import nomnomzbot.composeapp.generated.resources.automation_pipelines_hint
import nomnomzbot.composeapp.generated.resources.automation_pipelines_label
import nomnomzbot.composeapp.generated.resources.automation_prefix
import nomnomzbot.composeapp.generated.resources.automation_retry
import nomnomzbot.composeapp.generated.resources.automation_revoke
import nomnomzbot.composeapp.generated.resources.automation_revoke_confirm
import nomnomzbot.composeapp.generated.resources.automation_revoke_message
import nomnomzbot.composeapp.generated.resources.automation_revoke_title
import nomnomzbot.composeapp.generated.resources.automation_revoked
import nomnomzbot.composeapp.generated.resources.automation_rotate
import nomnomzbot.composeapp.generated.resources.automation_rotate_confirm
import nomnomzbot.composeapp.generated.resources.automation_rotate_message
import nomnomzbot.composeapp.generated.resources.automation_rotate_title
import nomnomzbot.composeapp.generated.resources.automation_scope_chat
import nomnomzbot.composeapp.generated.resources.automation_scope_events
import nomnomzbot.composeapp.generated.resources.automation_scope_invoke
import nomnomzbot.composeapp.generated.resources.automation_scope_read
import nomnomzbot.composeapp.generated.resources.automation_scopes_label
import nomnomzbot.composeapp.generated.resources.automation_secret_close
import nomnomzbot.composeapp.generated.resources.automation_secret_copied
import nomnomzbot.composeapp.generated.resources.automation_secret_copy
import nomnomzbot.composeapp.generated.resources.automation_secret_title
import nomnomzbot.composeapp.generated.resources.automation_secret_warning
import nomnomzbot.composeapp.generated.resources.automation_subtitle
import nomnomzbot.composeapp.generated.resources.shell_nav_automation
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Automation API-tokens page (frontend-ia.md, Connect group): the channel's external API tokens (issue /
// rotate / revoke) and one-time device pairing codes (automation-api.md §5 + stream-deck.md). A pure projection
// of [AutomationController]. Every write gates at the page's Broadcaster manage floor (automation:tokens:write,
// Critical). The plaintext secret is shown EXACTLY ONCE in a copy-once dialog, then only the prefix is ever seen.
@Composable
fun AutomationScreen(controller: AutomationController, role: ManagementRole?) {
    val state: AutomationUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Automation)

    var showCreate: Boolean by remember { mutableStateOf(false) }
    var showPair: Boolean by remember { mutableStateOf(false) }
    var issued: IssuedAutomationToken? by remember { mutableStateOf(null) }
    var pairCode: PairingCode? by remember { mutableStateOf(null) }
    var pendingRevoke: AutomationToken? by remember { mutableStateOf(null) }
    var pendingRotate: AutomationToken? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: AutomationUiState = state) {
            is AutomationUiState.Loading -> CenteredMessage(stringResource(Res.string.automation_loading))
            is AutomationUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is AutomationUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.shell_nav_automation),
                        subtitle = stringResource(Res.string.automation_subtitle),
                        trailing = {
                            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                                ManageGate(decision = manage) { enabled ->
                                    TextButton(onClick = { showPair = true }, enabled = enabled) {
                                        Text(text = stringResource(Res.string.automation_pair_device))
                                    }
                                }
                                ManageGate(decision = manage) { enabled ->
                                    Button(onClick = { showCreate = true }, enabled = enabled) {
                                        Text(text = stringResource(Res.string.automation_new_token))
                                    }
                                }
                            }
                        },
                    )
                    current.actionError?.let {
                        ActionErrorBanner(message = stringResource(Res.string.automation_action_error, it))
                    }

                    if (current.tokens.isEmpty()) {
                        CenteredMessage(stringResource(Res.string.automation_empty))
                    } else {
                        Card(modifier = Modifier.fillMaxWidth()) {
                            Column {
                                current.tokens.forEachIndexed { index, token ->
                                    TokenRow(
                                        token = token,
                                        manage = manage,
                                        onRotate = { pendingRotate = token },
                                        onRevoke = { pendingRevoke = token },
                                    )
                                    if (index < current.tokens.lastIndex) Separator()
                                }
                            }
                        }
                    }
                }
        }
    }

    if (showCreate) {
        val pipelines: List<PipelineSummary> = (state as? AutomationUiState.Ready)?.pipelines.orEmpty()
        CreateTokenDialog(
            pipelines = pipelines,
            onDismiss = { showCreate = false },
            onCreate = { name, scopes, pipelineIds, expiresAt ->
                showCreate = false
                scope.launch { issued = controller.createToken(name, scopes, pipelineIds, expiresAt) }
            },
        )
    }

    if (showPair) {
        PairDeviceDialog(
            onDismiss = { showPair = false },
            onPair = { label, scopes ->
                showPair = false
                scope.launch { pairCode = controller.mintPairCode(label, scopes) }
            },
        )
    }

    issued?.let { token ->
        SecretDialog(secret = token.secret, onDismiss = { issued = null })
    }

    pairCode?.let { code ->
        PairCodeDialog(code = code, onDismiss = { pairCode = null })
    }

    pendingRotate?.let { token ->
        ConfirmDialog(
            title = stringResource(Res.string.automation_rotate_title),
            message = stringResource(Res.string.automation_rotate_message, token.name),
            confirmLabel = stringResource(Res.string.automation_rotate_confirm),
            dismissLabel = stringResource(Res.string.automation_cancel),
            destructive = true,
            onConfirm = {
                pendingRotate = null
                scope.launch { issued = controller.rotateToken(token.id) }
            },
            onDismiss = { pendingRotate = null },
        )
    }

    pendingRevoke?.let { token ->
        ConfirmDialog(
            title = stringResource(Res.string.automation_revoke_title),
            message = stringResource(Res.string.automation_revoke_message, token.name),
            confirmLabel = stringResource(Res.string.automation_revoke_confirm),
            dismissLabel = stringResource(Res.string.automation_cancel),
            destructive = true,
            onConfirm = {
                pendingRevoke = null
                scope.launch { controller.revokeToken(token.id) }
            },
            onDismiss = { pendingRevoke = null },
        )
    }
}

@Composable
private fun TokenRow(
    token: AutomationToken,
    manage: ManageDecision,
    onRotate: () -> Unit,
    onRevoke: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val revoked: Boolean = token.revokedAt != null

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(
                    text = token.name,
                    style = typography.base,
                    color = if (revoked) tokens.mutedForeground else tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (revoked) {
                    Badge(variant = BadgeVariant.Destructive) { Text(text = stringResource(Res.string.automation_revoked)) }
                }
            }
            Text(
                text = stringResource(Res.string.automation_prefix, token.tokenPrefix),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s1), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                token.scopes.forEach { s ->
                    Badge(variant = BadgeVariant.Secondary) { Text(text = s, style = typography.xs) }
                }
            }
            Text(
                text =
                    token.lastUsedAt?.let { stringResource(Res.string.automation_last_used, it) }
                        ?: stringResource(Res.string.automation_never_used),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            Text(
                text =
                    token.expiresAt?.let { stringResource(Res.string.automation_expires, it) }
                        ?: stringResource(Res.string.automation_no_expiry),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }

        if (!revoked) {
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onRotate, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.automation_rotate),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onRevoke, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.automation_revoke),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }
    }
}

// ── dialogs ─────────────────────────────────────────────────────────────────

@Composable
private fun CreateTokenDialog(
    pipelines: List<PipelineSummary>,
    onDismiss: () -> Unit,
    onCreate: (name: String, scopes: List<String>, pipelineIds: List<String>, expiresAt: String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    val selectedScopes = remember { mutableStateOf(setOf(AutomationScope.Invoke)) }
    val selectedPipelines = remember { mutableStateOf(emptySet<String>()) }
    var expiryDays: Int? by remember { mutableStateOf(null) }

    val canCreate: Boolean = name.isNotBlank() && selectedScopes.value.isNotEmpty()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.automation_create_title)) },
        text = {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = stringResource(Res.string.automation_name_label),
                    modifier = Modifier.fillMaxWidth(),
                )

                Text(text = stringResource(Res.string.automation_scopes_label), style = typography.sm, color = tokens.mutedForeground)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    AutomationScope.all.forEach { key ->
                        val selected: Boolean = key in selectedScopes.value
                        ToggleChip(
                            label = stringResource(scopeLabel(key)),
                            selected = selected,
                            onClick = {
                                selectedScopes.value =
                                    if (selected) selectedScopes.value - key else selectedScopes.value + key
                            },
                        )
                    }
                }

                if (pipelines.isNotEmpty()) {
                    Text(text = stringResource(Res.string.automation_pipelines_label), style = typography.sm, color = tokens.mutedForeground)
                    Text(text = stringResource(Res.string.automation_pipelines_hint), style = typography.xs, color = tokens.mutedForeground)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        pipelines.forEach { pipeline ->
                            val selected: Boolean = pipeline.id in selectedPipelines.value
                            ToggleChip(
                                label = pipeline.name,
                                selected = selected,
                                onClick = {
                                    selectedPipelines.value =
                                        if (selected) selectedPipelines.value - pipeline.id
                                        else selectedPipelines.value + pipeline.id
                                },
                            )
                        }
                    }
                }

                Text(text = stringResource(Res.string.automation_expiry_label), style = typography.sm, color = tokens.mutedForeground)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    ToggleChip(label = stringResource(Res.string.automation_expiry_never), selected = expiryDays == null, onClick = { expiryDays = null })
                    ToggleChip(label = stringResource(Res.string.automation_expiry_30d), selected = expiryDays == 30, onClick = { expiryDays = 30 })
                    ToggleChip(label = stringResource(Res.string.automation_expiry_90d), selected = expiryDays == 90, onClick = { expiryDays = 90 })
                    ToggleChip(label = stringResource(Res.string.automation_expiry_1y), selected = expiryDays == 365, onClick = { expiryDays = 365 })
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onCreate(
                        name.trim(),
                        selectedScopes.value.toList(),
                        selectedPipelines.value.toList(),
                        expiryDays?.let { Clock.System.now().plus(it.days).toString() },
                    )
                },
                enabled = canCreate,
            ) {
                Text(
                    text = stringResource(Res.string.automation_create),
                    color = if (canCreate) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.automation_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun SecretDialog(secret: String, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.automation_secret_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Text(
                    text = stringResource(Res.string.automation_secret_warning),
                    style = typography.sm,
                    color = tokens.destructiveForeground,
                )
                Text(
                    text = secret,
                    style = typography.sm,
                    color = tokens.cardForeground,
                    modifier = Modifier.fillMaxWidth(),
                )
                CopyValue(
                    value = secret,
                    copyLabel = stringResource(Res.string.automation_secret_copy),
                    copiedLabel = stringResource(Res.string.automation_secret_copied),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.automation_secret_close), color = tokens.primary)
            }
        },
    )
}

@Composable
private fun PairDeviceDialog(onDismiss: () -> Unit, onPair: (label: String, scopes: List<String>) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var label: String by remember { mutableStateOf("") }
    var chatScope: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.automation_pair_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = label,
                    onValueChange = { label = it },
                    label = stringResource(Res.string.automation_pair_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = stringResource(Res.string.automation_pair_chat_scope), color = tokens.cardForeground)
                    Switch(checked = chatScope, onCheckedChange = { chatScope = it })
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    // Device pairing defaults to invoke+events+read; chat is added only via the explicit opt-in.
                    val scopes: List<String> =
                        buildList {
                            add(AutomationScope.Invoke)
                            add(AutomationScope.Events)
                            add(AutomationScope.Read)
                            if (chatScope) add(AutomationScope.Chat)
                        }
                    onPair(label.trim(), scopes)
                },
                enabled = label.isNotBlank(),
            ) {
                Text(
                    text = stringResource(Res.string.automation_pair_create),
                    color = if (label.isNotBlank()) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.automation_pair_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun PairCodeDialog(code: PairingCode, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Live countdown to the code's expiry (~5 minutes). Recomputes each second; shows mm:ss, then "expired".
    val expiresAt: Instant? = remember(code.expiresAt) { runCatching { Instant.parse(code.expiresAt) }.getOrNull() }
    var remaining: Long by remember(code.expiresAt) {
        mutableStateOf(expiresAt?.let { (it - Clock.System.now()).inWholeSeconds } ?: 0L)
    }
    LaunchedEffect(code.expiresAt) {
        while (true) {
            remaining = expiresAt?.let { (it - Clock.System.now()).inWholeSeconds } ?: 0L
            if (remaining <= 0L) break
            delay(1000)
        }
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.automation_paircode_title)) },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                Text(
                    text = stringResource(Res.string.automation_paircode_instructions),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    textAlign = TextAlign.Center,
                )
                Text(text = code.code, style = typography.xl, color = tokens.cardForeground)
                CopyValue(
                    value = code.code,
                    copyLabel = stringResource(Res.string.automation_paircode_copy),
                    copiedLabel = stringResource(Res.string.automation_paircode_copied),
                )
                Text(
                    text =
                        if (remaining > 0L) stringResource(Res.string.automation_paircode_expires_in, formatMmSs(remaining))
                        else stringResource(Res.string.automation_paircode_expired),
                    style = typography.xs,
                    color = if (remaining > 0L) tokens.mutedForeground else tokens.destructiveForeground,
                )
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.automation_paircode_close), color = tokens.primary)
            }
        },
    )
}

// ── shared bits ────────────────────────────────────────────────────────────

@Composable
private fun ToggleChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Badge(
        variant = if (selected) BadgeVariant.Default else BadgeVariant.Outline,
        selected = selected,
        onClick = onClick,
    ) {
        Text(text = label, maxLines = 1)
    }
}

private fun scopeLabel(key: String): StringResource =
    when (key) {
        AutomationScope.Invoke -> Res.string.automation_scope_invoke
        AutomationScope.Read -> Res.string.automation_scope_read
        AutomationScope.Events -> Res.string.automation_scope_events
        AutomationScope.Chat -> Res.string.automation_scope_chat
        else -> Res.string.automation_scope_invoke
    }

private fun formatMmSs(totalSeconds: Long): String {
    val minutes: Long = totalSeconds / 60
    val seconds: Long = totalSeconds % 60
    val ss: String = if (seconds < 10) "0$seconds" else "$seconds"
    return "$minutes:$ss"
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.automation_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.automation_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
