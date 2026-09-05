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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminTierChangePreview
import bot.nomnomz.dashboard.core.network.AdminTierLimit
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_tier_blast_radius_counted
import nomnomzbot.composeapp.generated.resources.admin_tier_blast_radius_loading
import nomnomzbot.composeapp.generated.resources.admin_tier_blast_radius_none
import nomnomzbot.composeapp.generated.resources.admin_tier_bot_name_badge
import nomnomzbot.composeapp.generated.resources.admin_tier_edit
import nomnomzbot.composeapp.generated.resources.admin_tier_edit_title
import nomnomzbot.composeapp.generated.resources.admin_tier_field_bot_name
import nomnomzbot.composeapp.generated.resources.admin_tier_field_currency
import nomnomzbot.composeapp.generated.resources.admin_tier_field_display_name
import nomnomzbot.composeapp.generated.resources.admin_tier_field_price_cents
import nomnomzbot.composeapp.generated.resources.admin_tier_field_priority_support
import nomnomzbot.composeapp.generated.resources.admin_tier_field_public
import nomnomzbot.composeapp.generated.resources.admin_tier_field_sort_order
import nomnomzbot.composeapp.generated.resources.admin_tier_internal_badge
import nomnomzbot.composeapp.generated.resources.admin_tier_limits_heading
import nomnomzbot.composeapp.generated.resources.admin_tier_price
import nomnomzbot.composeapp.generated.resources.admin_tier_priority_support_badge
import nomnomzbot.composeapp.generated.resources.admin_tier_public_badge
import nomnomzbot.composeapp.generated.resources.admin_tier_save
import nomnomzbot.composeapp.generated.resources.admin_tier_type_label
import nomnomzbot.composeapp.generated.resources.admin_tiers_empty
import nomnomzbot.composeapp.generated.resources.admin_tiers_heading
import org.jetbrains.compose.resources.stringResource

/**
 * The tier catalogue editor (S-ADMIN-4a) — lists every tier (public and internal) with its price and feature
 * flags, and opens [TierEditDialog] to edit one. Editing a tier that already has tenants on it shows the
 * COUNTED blast radius before the save can commit (consequences-must-be-visible), mirroring the feature-flag
 * kill-switch confirm dialog above on this same tab.
 */
@Composable
internal fun TierListSection(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.admin_tiers_heading), style = typography.base, color = tokens.foreground)

        if (state.tiers.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_tiers_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.tiers.forEachIndexed { index, tier ->
                        TierRow(
                            tier = tier,
                            onEdit = { scope.launch { controller.previewTierEdit(tier.id) } },
                        )
                        if (index < state.tiers.lastIndex) Separator()
                    }
                }
            }
        }
    }

    val editingId: String? = state.tierEditId
    if (editingId != null) {
        val tier: AdminTier? = state.tiers.firstOrNull { it.id == editingId }
        if (tier != null) {
            TierEditDialog(
                tier = tier,
                preview = state.tierEditPreview,
                onDismiss = { controller.dismissTierEditPreview() },
                onSave = { request ->
                    scope.launch { controller.confirmTierEdit(tier.id, request) }
                },
            )
        }
    }
}

@Composable
private fun TierRow(tier: AdminTier, onEdit: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column {
                Text(
                    text =
                        resolveRowLabel(
                            primary = tier.displayName,
                            secondary = tier.key,
                            typeLabel = stringResource(Res.string.admin_tier_type_label),
                            discriminatorSource = tier.key,
                        ),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(
                        Res.string.admin_tier_price,
                        tier.priceCents / 100,
                        // compose-resources ignores width/flags on a positional spec, so "%2$02d" rendered
                        // the literal text instead of zero-padding: 10.5 where 10.05 was meant. Pad here.
                        (tier.priceCents % 100).toString().padStart(2, '0'),
                        tier.currency.uppercase(),
                    ),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            TextButton(onClick = onEdit) {
                Text(text = stringResource(Res.string.admin_tier_edit))
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Badge(variant = if (tier.isPublic) BadgeVariant.Outline else BadgeVariant.Secondary) {
                Text(
                    text = stringResource(
                        if (tier.isPublic) Res.string.admin_tier_public_badge else Res.string.admin_tier_internal_badge,
                    ),
                )
            }
            if (tier.allowsCustomBotName) {
                Badge(variant = BadgeVariant.Secondary) {
                    Text(text = stringResource(Res.string.admin_tier_bot_name_badge))
                }
            }
            if (tier.prioritySupport) {
                Badge(variant = BadgeVariant.Secondary) {
                    Text(text = stringResource(Res.string.admin_tier_priority_support_badge))
                }
            }
        }
    }
}

/**
 * The tier edit dialog. [preview] is null while its counted-blast-radius fetch is in flight, or once loaded,
 * the REAL number of tenants on [tier] right now. Save is disabled until it resolves — the operator can never
 * commit an edit to an in-use tier blind. Limits are edited by VALUE only; a tier's set of limit keys is
 * authored server-side (which keys exist is a code-level decision — what they're worth per tier is this
 * dialog's job).
 */
@Composable
private fun TierEditDialog(
    tier: AdminTier,
    preview: AdminTierChangePreview?,
    onDismiss: () -> Unit,
    onSave: (AdminUpdateTierRequest) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var displayName: String by remember(tier.id) { mutableStateOf(tier.displayName) }
    var priceCents: String by remember(tier.id) { mutableStateOf(tier.priceCents.toString()) }
    var currency: String by remember(tier.id) { mutableStateOf(tier.currency) }
    var sortOrder: String by remember(tier.id) { mutableStateOf(tier.sortOrder.toString()) }
    var isPublic: Boolean by remember(tier.id) { mutableStateOf(tier.isPublic) }
    var allowsCustomBotName: Boolean by remember(tier.id) { mutableStateOf(tier.allowsCustomBotName) }
    var prioritySupport: Boolean by remember(tier.id) { mutableStateOf(tier.prioritySupport) }
    var limitValues: Map<String, String> by remember(tier.id) {
        mutableStateOf(tier.limits.associate { it.limitKey to it.limitValue.toString() })
    }

    val priceValid: Int? = priceCents.toIntOrNull()
    val sortValid: Int? = sortOrder.toIntOrNull()
    val formValid: Boolean = displayName.isNotBlank() && currency.isNotBlank() && priceValid != null && sortValid != null

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_tier_edit_title, tier.key))

        AppTextField(
            value = displayName,
            onValueChange = { displayName = it },
            label = stringResource(Res.string.admin_tier_field_display_name),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            AppTextField(
                value = priceCents,
                onValueChange = { priceCents = it },
                label = stringResource(Res.string.admin_tier_field_price_cents),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                isError = priceValid == null,
                modifier = Modifier.fillMaxWidth(),
            )
            AppTextField(
                value = currency,
                onValueChange = { currency = it },
                label = stringResource(Res.string.admin_tier_field_currency),
                modifier = Modifier.fillMaxWidth(),
            )
        }
        Spacer(modifier = Modifier.height(spacing.s2))
        AppTextField(
            value = sortOrder,
            onValueChange = { sortOrder = it },
            label = stringResource(Res.string.admin_tier_field_sort_order),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            isError = sortValid == null,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s2))

        SwitchRow(stringResource(Res.string.admin_tier_field_public), isPublic) { isPublic = it }
        SwitchRow(stringResource(Res.string.admin_tier_field_bot_name), allowsCustomBotName) { allowsCustomBotName = it }
        SwitchRow(stringResource(Res.string.admin_tier_field_priority_support), prioritySupport) { prioritySupport = it }

        if (limitValues.isNotEmpty()) {
            Spacer(modifier = Modifier.height(spacing.s2))
            Text(text = stringResource(Res.string.admin_tier_limits_heading), style = typography.sm, color = tokens.foreground)
            Spacer(modifier = Modifier.height(spacing.s1))
            limitValues.forEach { (key, value) ->
                AppTextField(
                    value = value,
                    onValueChange = { newValue -> limitValues = limitValues + (key to newValue) },
                    label = key,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    isError = value.toLongOrNull() == null,
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(modifier = Modifier.height(spacing.s1))
            }
        }

        Spacer(modifier = Modifier.height(spacing.s2))
        BlastRadiusNotice(preview)

        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(
                onClick = {
                    onSave(
                        AdminUpdateTierRequest(
                            displayName = displayName,
                            priceCents = priceValid ?: tier.priceCents,
                            currency = currency,
                            allowsCustomBotName = allowsCustomBotName,
                            prioritySupport = prioritySupport,
                            isPublic = isPublic,
                            sortOrder = sortValid ?: tier.sortOrder,
                            limits = limitValues.map { (key, value) ->
                                AdminTierLimit(limitKey = key, limitValue = value.toLongOrNull() ?: 0L)
                            },
                            confirmedAffectedTenantCount = preview?.affectedTenantCount ?: 0,
                        ),
                    )
                },
                enabled = formValid && preview != null && limitValues.values.all { it.toLongOrNull() != null },
            ) {
                Text(text = stringResource(Res.string.admin_tier_save))
            }
        }
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, style = typography.sm, color = tokens.cardForeground)
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
    Spacer(modifier = Modifier.height(spacing.s1))
}

/** The counted blast radius, shown BEFORE the save button can be pressed — never a guess, never hidden. */
@Composable
private fun BlastRadiusNotice(preview: AdminTierChangePreview?) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(verticalAlignment = Alignment.CenterVertically) {
        when {
            preview == null -> {
                Spinner(color = tokens.mutedForeground)
                Spacer(modifier = Modifier.width(LocalSpacing.current.s1))
                Text(
                    text = stringResource(Res.string.admin_tier_blast_radius_loading),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            preview.affectedTenantCount <= 0 -> Text(
                text = stringResource(Res.string.admin_tier_blast_radius_none),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            else -> Text(
                text = stringResource(
                    Res.string.admin_tier_blast_radius_counted,
                    preview.affectedTenantCount,
                    preview.sampleChannelNames.joinToString(", "),
                ),
                style = typography.xs,
                color = tokens.destructive,
            )
        }
    }
}
