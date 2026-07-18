// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mydata.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ConsentRecord
import bot.nomnomz.dashboard.core.network.ErasureRequest
import bot.nomnomz.dashboard.feature.mydata.state.MyDataController
import bot.nomnomz.dashboard.feature.mydata.state.MyDataUiState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.mydata_action_error
import nomnomzbot.composeapp.generated.resources.mydata_cancel
import nomnomzbot.composeapp.generated.resources.mydata_completed_at
import nomnomzbot.composeapp.generated.resources.mydata_consent_basis
import nomnomzbot.composeapp.generated.resources.mydata_consent_granted
import nomnomzbot.composeapp.generated.resources.mydata_consent_granted_at
import nomnomzbot.composeapp.generated.resources.mydata_consent_withdrawn
import nomnomzbot.composeapp.generated.resources.mydata_consent_withdrawn_at
import nomnomzbot.composeapp.generated.resources.mydata_consents_empty
import nomnomzbot.composeapp.generated.resources.mydata_consents_title
import nomnomzbot.composeapp.generated.resources.mydata_erase_button
import nomnomzbot.composeapp.generated.resources.mydata_erase_confirm
import nomnomzbot.composeapp.generated.resources.mydata_erase_confirm_message
import nomnomzbot.composeapp.generated.resources.mydata_erase_confirm_title
import nomnomzbot.composeapp.generated.resources.mydata_erasure_desc
import nomnomzbot.composeapp.generated.resources.mydata_erasure_title
import nomnomzbot.composeapp.generated.resources.mydata_error
import nomnomzbot.composeapp.generated.resources.mydata_export_button
import nomnomzbot.composeapp.generated.resources.mydata_export_desc
import nomnomzbot.composeapp.generated.resources.mydata_export_saved
import nomnomzbot.composeapp.generated.resources.mydata_export_title
import nomnomzbot.composeapp.generated.resources.mydata_grant_basis_label
import nomnomzbot.composeapp.generated.resources.mydata_grant_button
import nomnomzbot.composeapp.generated.resources.mydata_grant_confirm
import nomnomzbot.composeapp.generated.resources.mydata_grant_title
import nomnomzbot.composeapp.generated.resources.mydata_grant_type_label
import nomnomzbot.composeapp.generated.resources.mydata_loading
import nomnomzbot.composeapp.generated.resources.mydata_optout_button
import nomnomzbot.composeapp.generated.resources.mydata_optout_confirm
import nomnomzbot.composeapp.generated.resources.mydata_optout_confirm_message
import nomnomzbot.composeapp.generated.resources.mydata_optout_confirm_title
import nomnomzbot.composeapp.generated.resources.mydata_request_scope
import nomnomzbot.composeapp.generated.resources.mydata_requested_at
import nomnomzbot.composeapp.generated.resources.mydata_requests_empty
import nomnomzbot.composeapp.generated.resources.mydata_requests_title
import nomnomzbot.composeapp.generated.resources.mydata_retry
import nomnomzbot.composeapp.generated.resources.mydata_status_completed
import nomnomzbot.composeapp.generated.resources.mydata_status_failed
import nomnomzbot.composeapp.generated.resources.mydata_status_processing
import nomnomzbot.composeapp.generated.resources.mydata_status_received
import nomnomzbot.composeapp.generated.resources.mydata_subtitle
import nomnomzbot.composeapp.generated.resources.mydata_title
import nomnomzbot.composeapp.generated.resources.mydata_withdraw
import nomnomzbot.composeapp.generated.resources.mydata_withdraw_confirm
import nomnomzbot.composeapp.generated.resources.mydata_withdraw_confirm_message
import nomnomzbot.composeapp.generated.resources.mydata_withdraw_confirm_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The "My data" (GDPR self-service) page (frontend-ia.md, Settings group; privacy.md §5): the signed-in caller's
// own data-subject rights — export a portable copy, opt out of analytics/marketing, request an irreversible
// erasure, and review the request history + consent ledger. A pure projection of [MyDataController]. There is no
// role param: these routes are Gate-1 (always the caller's own data), so every action is always available.
@Composable
fun MyDataScreen(controller: MyDataController) {
    val state: MyDataUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    var showErase: Boolean by remember { mutableStateOf(false) }
    var showOptOut: Boolean by remember { mutableStateOf(false) }
    var showGrant: Boolean by remember { mutableStateOf(false) }
    var pendingWithdraw: ConsentRecord? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: MyDataUiState = state) {
            is MyDataUiState.Loading -> CenteredMessage(stringResource(Res.string.mydata_loading))
            is MyDataUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is MyDataUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.mydata_title),
                        subtitle = stringResource(Res.string.mydata_subtitle),
                    )
                    current.actionError?.let {
                        ActionErrorBanner(message = stringResource(Res.string.mydata_action_error, it))
                    }

                    ExportCard(
                        notice = current.notice,
                        onExport = { scope.launch { controller.exportData() } },
                    )
                    ErasureCard(
                        onOptOut = { showOptOut = true },
                        onErase = { showErase = true },
                    )
                    RequestsCard(requests = current.requests)
                    ConsentsCard(
                        consents = current.consents,
                        onGrant = { showGrant = true },
                        onWithdraw = { pendingWithdraw = it },
                    )
                }
        }
    }

    if (showOptOut) {
        ConfirmDialog(
            title = stringResource(Res.string.mydata_optout_confirm_title),
            message = stringResource(Res.string.mydata_optout_confirm_message),
            confirmLabel = stringResource(Res.string.mydata_optout_confirm),
            dismissLabel = stringResource(Res.string.mydata_cancel),
            onConfirm = {
                showOptOut = false
                scope.launch { controller.optOut() }
            },
            onDismiss = { showOptOut = false },
        )
    }

    if (showErase) {
        ConfirmDialog(
            title = stringResource(Res.string.mydata_erase_confirm_title),
            message = stringResource(Res.string.mydata_erase_confirm_message),
            confirmLabel = stringResource(Res.string.mydata_erase_confirm),
            dismissLabel = stringResource(Res.string.mydata_cancel),
            destructive = true,
            onConfirm = {
                showErase = false
                scope.launch { controller.requestErasure() }
            },
            onDismiss = { showErase = false },
        )
    }

    if (showGrant) {
        GrantConsentDialog(
            onDismiss = { showGrant = false },
            onGrant = { consentType, lawfulBasis ->
                showGrant = false
                scope.launch { controller.grantConsent(consentType, lawfulBasis) }
            },
        )
    }

    pendingWithdraw?.let { record ->
        ConfirmDialog(
            title = stringResource(Res.string.mydata_withdraw_confirm_title),
            message = stringResource(Res.string.mydata_withdraw_confirm_message, record.consentType),
            confirmLabel = stringResource(Res.string.mydata_withdraw_confirm),
            dismissLabel = stringResource(Res.string.mydata_cancel),
            destructive = true,
            onConfirm = {
                pendingWithdraw = null
                scope.launch { controller.withdrawConsent(record.consentType) }
            },
            onDismiss = { pendingWithdraw = null },
        )
    }
}

// ── section cards ─────────────────────────────────────────────────────────────

@Composable
private fun ExportCard(notice: String?, onExport: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(text = stringResource(Res.string.mydata_export_title), style = typography.lg, color = tokens.cardForeground)
            Text(text = stringResource(Res.string.mydata_export_desc), style = typography.sm, color = tokens.mutedForeground)
            Button(onClick = onExport) { Text(text = stringResource(Res.string.mydata_export_button)) }
            if (notice != null) {
                Text(
                    text = stringResource(Res.string.mydata_export_saved),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
}

@Composable
private fun ErasureCard(onOptOut: () -> Unit, onErase: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(text = stringResource(Res.string.mydata_erasure_title), style = typography.lg, color = tokens.cardForeground)
            Text(text = stringResource(Res.string.mydata_erasure_desc), style = typography.sm, color = tokens.mutedForeground)
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Button(onClick = onOptOut, variant = ButtonVariant.Outline) {
                    Text(text = stringResource(Res.string.mydata_optout_button))
                }
                Button(onClick = onErase, variant = ButtonVariant.Destructive) {
                    Text(text = stringResource(Res.string.mydata_erase_button))
                }
            }
        }
    }
}

@Composable
private fun RequestsCard(requests: List<ErasureRequest>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Text(text = stringResource(Res.string.mydata_requests_title), style = typography.lg, color = tokens.cardForeground)
            if (requests.isEmpty()) {
                CenteredMessage(stringResource(Res.string.mydata_requests_empty))
            } else {
                Column {
                    requests.forEachIndexed { index, request ->
                        RequestRow(request = request)
                        if (index < requests.lastIndex) Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun RequestRow(request: ErasureRequest) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = request.requestType,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = stringResource(Res.string.mydata_request_scope, request.scope),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            Text(
                text = stringResource(Res.string.mydata_requested_at, request.requestedAt),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            request.completedAt?.let {
                Text(
                    text = stringResource(Res.string.mydata_completed_at, it),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        Badge(variant = statusVariant(request.status)) {
            Text(text = stringResource(statusLabel(request.status)), style = typography.xs)
        }
    }
}

@Composable
private fun ConsentsCard(
    consents: List<ConsentRecord>,
    onGrant: () -> Unit,
    onWithdraw: (ConsentRecord) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(text = stringResource(Res.string.mydata_consents_title), style = typography.lg, color = tokens.cardForeground)
                Button(onClick = onGrant) { Text(text = stringResource(Res.string.mydata_grant_button)) }
            }
            if (consents.isEmpty()) {
                CenteredMessage(stringResource(Res.string.mydata_consents_empty))
            } else {
                Column {
                    consents.forEachIndexed { index, record ->
                        ConsentRow(record = record, onWithdraw = { onWithdraw(record) })
                        if (index < consents.lastIndex) Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun ConsentRow(record: ConsentRecord, onWithdraw: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val withdrawn: Boolean = record.withdrawnAt != null

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(
                    text = record.consentType,
                    style = typography.base,
                    color = if (withdrawn) tokens.mutedForeground else tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (withdrawn) {
                    Badge(variant = BadgeVariant.Destructive) {
                        Text(text = stringResource(Res.string.mydata_consent_withdrawn), style = typography.xs)
                    }
                } else {
                    Badge(variant = BadgeVariant.Default) {
                        Text(text = stringResource(Res.string.mydata_consent_granted), style = typography.xs)
                    }
                }
            }
            Text(
                text = stringResource(Res.string.mydata_consent_basis, record.lawfulBasis),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            Text(
                text =
                    record.withdrawnAt?.let { stringResource(Res.string.mydata_consent_withdrawn_at, it) }
                        ?: stringResource(Res.string.mydata_consent_granted_at, record.grantedAt),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        if (!withdrawn) {
            TextButton(onClick = onWithdraw) {
                Text(text = stringResource(Res.string.mydata_withdraw), color = tokens.destructive, maxLines = 1)
            }
        }
    }
}

// ── dialogs ─────────────────────────────────────────────────────────────────

@Composable
private fun GrantConsentDialog(onDismiss: () -> Unit, onGrant: (consentType: String, lawfulBasis: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var consentType: String by remember { mutableStateOf("") }
    var lawfulBasis: String by remember { mutableStateOf("consent") }

    val canGrant: Boolean = consentType.isNotBlank() && lawfulBasis.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.mydata_grant_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = consentType,
                    onValueChange = { consentType = it },
                    label = stringResource(Res.string.mydata_grant_type_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = lawfulBasis,
                    onValueChange = { lawfulBasis = it },
                    label = stringResource(Res.string.mydata_grant_basis_label),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onGrant(consentType.trim(), lawfulBasis.trim()) },
                enabled = canGrant,
            ) {
                Text(
                    text = stringResource(Res.string.mydata_grant_confirm),
                    color = if (canGrant) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.mydata_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// ── shared bits ────────────────────────────────────────────────────────────

private fun statusVariant(status: String): BadgeVariant =
    when (status.lowercase()) {
        "completed" -> BadgeVariant.Default
        "failed" -> BadgeVariant.Destructive
        else -> BadgeVariant.Secondary
    }

private fun statusLabel(status: String): StringResource =
    when (status.lowercase()) {
        "completed" -> Res.string.mydata_status_completed
        "failed" -> Res.string.mydata_status_failed
        "processing" -> Res.string.mydata_status_processing
        else -> Res.string.mydata_status_received
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
                text = stringResource(Res.string.mydata_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.mydata_retry)) }
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
