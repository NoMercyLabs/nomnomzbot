// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_audit_break_glass
import nomnomzbot.composeapp.generated.resources.admin_audit_empty
import nomnomzbot.composeapp.generated.resources.admin_audit_outcome_all
import nomnomzbot.composeapp.generated.resources.admin_audit_outcome_allowed
import nomnomzbot.composeapp.generated.resources.admin_audit_outcome_denied
import nomnomzbot.composeapp.generated.resources.admin_audit_permission
import org.jetbrains.compose.resources.stringResource

private const val OUTCOME_ALLOWED: String = "allowed"
private const val OUTCOME_DENIED: String = "denied"

/**
 * The Plane-C audit log: a permission text filter + outcome chips over the operator action trail. Each row is
 * one recorded IAM access evaluation (who, what permission, which tenant, outcome, when) — the trail the tenant
 * suspend/reinstate + IAM actions write, so an operator can see exactly what was done from the dashboard.
 */
@Composable
internal fun AuditTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var permissionText: String by remember { mutableStateOf(state.auditPermissionFilter) }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        AppTextField(
            value = permissionText,
            onValueChange = { permissionText = it },
            label = stringResource(Res.string.admin_audit_permission),
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { scope.launch { controller.loadAudit(permission = permissionText) } }),
        )

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            OutcomeChip(stringResource(Res.string.admin_audit_outcome_all), state.auditOutcomeFilter == null) {
                scope.launch { controller.loadAudit(outcome = null) }
            }
            OutcomeChip(stringResource(Res.string.admin_audit_outcome_allowed), state.auditOutcomeFilter == OUTCOME_ALLOWED) {
                scope.launch { controller.loadAudit(outcome = OUTCOME_ALLOWED) }
            }
            OutcomeChip(stringResource(Res.string.admin_audit_outcome_denied), state.auditOutcomeFilter == OUTCOME_DENIED) {
                scope.launch { controller.loadAudit(outcome = OUTCOME_DENIED) }
            }
        }

        if (state.auditLoading) {
            Spinner(color = tokens.primary)
        } else if (state.auditEntries.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_audit_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.auditEntries.forEachIndexed { index, entry ->
                        AuditRow(entry)
                        if (index < state.auditEntries.lastIndex) Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun OutcomeChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val typography = LocalTypography.current
    Badge(selected = selected, onClick = onClick) { Text(text = label, style = typography.sm) }
}

@Composable
private fun AuditRow(entry: IamAuditEntry) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val denied: Boolean = entry.outcome.equals(OUTCOME_DENIED, ignoreCase = true)

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = entry.permission, style = typography.sm, color = tokens.cardForeground, modifier = Modifier.weight(1f))
            if (entry.breakGlass) {
                Badge(variant = BadgeVariant.Destructive) { Text(text = stringResource(Res.string.admin_audit_break_glass), style = typography.xs) }
            }
            Badge(variant = if (denied) BadgeVariant.Destructive else BadgeVariant.Secondary) {
                Text(text = entry.outcome, style = typography.xs)
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = entry.principalType, style = typography.xs, color = tokens.mutedForeground)
            Spacer(modifier = Modifier.width(spacing.s1))
            Text(text = entry.occurredAt, style = typography.xs, color = tokens.mutedForeground)
        }
        entry.justification?.takeIf { it.isNotBlank() }?.let {
            Text(text = it, style = typography.xs, color = tokens.mutedForeground)
        }
    }
}
