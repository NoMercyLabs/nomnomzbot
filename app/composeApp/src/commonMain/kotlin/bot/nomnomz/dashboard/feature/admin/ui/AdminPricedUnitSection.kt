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
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AdminAuthorPricedUnitRequest
import bot.nomnomz.dashboard.core.network.AdminPricedUnit
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_add
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_dialog_title_edit
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_dialog_title_new
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_edit
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_field_batch_size
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_field_currency
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_field_price
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_field_unit_key
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_rate
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_save
import nomnomzbot.composeapp.generated.resources.admin_priced_unit_type_label
import nomnomzbot.composeapp.generated.resources.admin_priced_units_empty
import nomnomzbot.composeapp.generated.resources.admin_priced_units_heading
import org.jetbrains.compose.resources.stringResource

/**
 * The priced-unit catalogue editor (S-ADMIN-4d) — the owner-authored real price for a usage unit the
 * [TenantUsageTab] already measures. Ships EMPTY: a unit with no row here stays unpriced, and the usage tab
 * shows its quantity only, never a fabricated cost. Authoring upserts by unit key — one dialog handles both
 * a brand-new price and a reprice, the same generic-primitive shape [TierListSection] already uses.
 */
@Composable
internal fun PricedUnitSection(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = stringResource(Res.string.admin_priced_units_heading), style = typography.base, color = tokens.foreground)
            Button(onClick = { controller.openPricedUnitForm(null) }) {
                Text(text = stringResource(Res.string.admin_priced_unit_add))
            }
        }

        if (state.pricedUnits.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_priced_units_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.pricedUnits.forEachIndexed { index, unit ->
                        PricedUnitRow(unit = unit, onEdit = { controller.openPricedUnitForm(unit.unitKey) })
                        if (index < state.pricedUnits.lastIndex) Separator()
                    }
                }
            }
        }
    }

    if (state.pricedUnitFormOpen) {
        val editing: AdminPricedUnit? =
            state.pricedUnits.firstOrNull { it.unitKey == state.pricedUnitFormEditingKey }
        PricedUnitFormDialog(
            editing = editing,
            onDismiss = { controller.dismissPricedUnitForm() },
            onSave = { request -> scope.launch { controller.authorPricedUnit(request) } },
        )
    }
}

@Composable
private fun PricedUnitRow(unit: AdminPricedUnit, onEdit: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(all = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column {
            Text(
                text = resolveRowLabel(
                    primary = unit.unitKey,
                    typeLabel = stringResource(Res.string.admin_priced_unit_type_label),
                    discriminatorSource = unit.unitKey,
                ),
                style = typography.sm,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(
                    Res.string.admin_priced_unit_rate,
                    unit.priceMinorUnitsPerBatch.toString(),
                    unit.currency.uppercase(),
                    unit.batchSize.toString(),
                ),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        TextButton(onClick = onEdit) {
            Text(text = stringResource(Res.string.admin_priced_unit_edit))
        }
    }
}

/**
 * Author (create or reprice) one priced unit. [editing] non-null prefills the form from that row and locks
 * the unit key (repricing changes the rate, never the key it prices); null starts a brand-new, blank entry.
 */
@Composable
private fun PricedUnitFormDialog(
    editing: AdminPricedUnit?,
    onDismiss: () -> Unit,
    onSave: (AdminAuthorPricedUnitRequest) -> Unit,
) {
    val spacing = LocalSpacing.current

    var unitKey: String by remember(editing?.unitKey) { mutableStateOf(editing?.unitKey.orEmpty()) }
    var currency: String by remember(editing?.unitKey) { mutableStateOf(editing?.currency ?: "usd") }
    var price: String by remember(editing?.unitKey) {
        mutableStateOf(editing?.priceMinorUnitsPerBatch?.toString().orEmpty())
    }
    var batchSize: String by remember(editing?.unitKey) {
        mutableStateOf(editing?.batchSize?.toString() ?: "1")
    }

    val priceValid: Long? = price.toLongOrNull()
    val batchValid: Long? = batchSize.toLongOrNull()
    val formValid: Boolean =
        unitKey.isNotBlank() && currency.isNotBlank() &&
            priceValid != null && priceValid >= 0 &&
            batchValid != null && batchValid >= 1

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(
            text = if (editing != null) {
                stringResource(Res.string.admin_priced_unit_dialog_title_edit, editing.unitKey)
            } else {
                stringResource(Res.string.admin_priced_unit_dialog_title_new)
            },
        )

        AppTextField(
            value = unitKey,
            onValueChange = { unitKey = it },
            label = stringResource(Res.string.admin_priced_unit_field_unit_key),
            enabled = editing == null,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        AppTextField(
            value = currency,
            onValueChange = { currency = it },
            label = stringResource(Res.string.admin_priced_unit_field_currency),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        AppTextField(
            value = price,
            onValueChange = { price = it },
            label = stringResource(Res.string.admin_priced_unit_field_price),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            isError = priceValid == null || priceValid < 0,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        AppTextField(
            value = batchSize,
            onValueChange = { batchSize = it },
            label = stringResource(Res.string.admin_priced_unit_field_batch_size),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            isError = batchValid == null || batchValid < 1,
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(modifier = Modifier.height(spacing.s2))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(
                onClick = {
                    onSave(
                        AdminAuthorPricedUnitRequest(
                            unitKey = unitKey,
                            currency = currency,
                            priceMinorUnitsPerBatch = priceValid ?: 0L,
                            batchSize = batchValid ?: 1L,
                        ),
                    )
                },
                enabled = formValid,
            ) {
                Text(text = stringResource(Res.string.admin_priced_unit_save))
            }
        }
    }
}
