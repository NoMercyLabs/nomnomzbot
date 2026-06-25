// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.economy.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextFieldColors
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
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.EarningRule
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.feature.economy.state.EconomyController
import bot.nomnomz.dashboard.feature.economy.state.EconomyState
import bot.nomnomz.dashboard.feature.shell.nav.ManageAction
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.economy_account_freeze
import nomnomzbot.composeapp.generated.resources.economy_account_freeze_action
import nomnomzbot.composeapp.generated.resources.economy_account_frozen
import nomnomzbot.composeapp.generated.resources.economy_account_unfreeze
import nomnomzbot.composeapp.generated.resources.economy_account_unfreeze_action
import nomnomzbot.composeapp.generated.resources.economy_accounts_empty
import nomnomzbot.composeapp.generated.resources.economy_accounts_row_description
import nomnomzbot.composeapp.generated.resources.economy_accounts_title
import nomnomzbot.composeapp.generated.resources.economy_catalog_disable
import nomnomzbot.composeapp.generated.resources.economy_catalog_disable_action
import nomnomzbot.composeapp.generated.resources.economy_catalog_disabled
import nomnomzbot.composeapp.generated.resources.economy_catalog_enable
import nomnomzbot.composeapp.generated.resources.economy_catalog_enable_action
import nomnomzbot.composeapp.generated.resources.economy_catalog_empty
import nomnomzbot.composeapp.generated.resources.economy_catalog_row_description
import nomnomzbot.composeapp.generated.resources.economy_catalog_stock
import nomnomzbot.composeapp.generated.resources.economy_catalog_title
import nomnomzbot.composeapp.generated.resources.economy_disable_confirm_cancel
import nomnomzbot.composeapp.generated.resources.economy_earning_disabled
import nomnomzbot.composeapp.generated.resources.economy_earning_empty
import nomnomzbot.composeapp.generated.resources.economy_earning_row_description
import nomnomzbot.composeapp.generated.resources.economy_earning_title
import nomnomzbot.composeapp.generated.resources.economy_disable_confirm_confirm
import nomnomzbot.composeapp.generated.resources.economy_disable_confirm_message
import nomnomzbot.composeapp.generated.resources.economy_disable_confirm_title
import nomnomzbot.composeapp.generated.resources.economy_error
import nomnomzbot.composeapp.generated.resources.economy_label_currency_name
import nomnomzbot.composeapp.generated.resources.economy_label_currency_name_plural
import nomnomzbot.composeapp.generated.resources.economy_label_decimal_places
import nomnomzbot.composeapp.generated.resources.economy_label_icon_url
import nomnomzbot.composeapp.generated.resources.economy_label_max_balance
import nomnomzbot.composeapp.generated.resources.economy_label_starting_balance
import nomnomzbot.composeapp.generated.resources.economy_leaderboard_empty
import nomnomzbot.composeapp.generated.resources.economy_leaderboard_row_description
import nomnomzbot.composeapp.generated.resources.economy_leaderboard_title
import nomnomzbot.composeapp.generated.resources.economy_loading
import nomnomzbot.composeapp.generated.resources.economy_name_invalid
import nomnomzbot.composeapp.generated.resources.economy_retry
import nomnomzbot.composeapp.generated.resources.economy_save
import nomnomzbot.composeapp.generated.resources.economy_save_error
import nomnomzbot.composeapp.generated.resources.economy_saved
import nomnomzbot.composeapp.generated.resources.economy_saving
import nomnomzbot.composeapp.generated.resources.economy_status_disabled
import nomnomzbot.composeapp.generated.resources.economy_status_enabled
import nomnomzbot.composeapp.generated.resources.economy_toggle_enabled
import org.jetbrains.compose.resources.stringResource

// The Economy page (economy.md §4): an editable form over the channel's currency definition (name, symbol, earn
// settings, the enabled toggle) plus the read-only points leaderboard below it — all real data from
// [EconomyController] (no fabricated balances). The screen seeds a local form from the controller's loaded config;
// Save persists the whole config and the controller echoes the saved values back. Disabling the economy is the
// consequential action (it stops viewers earning), so that save routes through a ConfirmDialog. The screen loads on
// first composition and offers a retry on failure.
@Composable
fun EconomyScreen(controller: EconomyController, role: ManagementRole?) {
    val state: EconomyState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Economy splits its write floor (frontend-ia.md §3 Loyalty row): the general currency editor — name, icon,
    // the enabled toggle, Save — gates at the page's Editor floor, but the PAYOUT / EARN RULES (the economic
    // parameters: starting balance, max balance, decimal places) are Broadcaster-only. An Editor can rename the
    // currency but the earn-rule fields render read-only with reason (§7); the backend re-checks every write.
    val config: ManageDecision = rememberManageDecision(role, ShellRoute.Economy)
    val payoutRules: ManageDecision =
        rememberManageDecision(role, ShellRoute.Economy, ManageAction.EconomyPayoutRules)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: EconomyState = state) {
            is EconomyState.Loading -> CenteredMessage(stringResource(Res.string.economy_loading))
            is EconomyState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is EconomyState.Ready ->
                ReadyContent(
                    state = current,
                    config = config,
                    payoutRules = payoutRules,
                    onSave = { edited -> scope.launch { controller.save(edited) } },
                    onFreeze = { viewerUserId, frozen ->
                        scope.launch { controller.freezeAccount(viewerUserId, frozen) }
                    },
                    onToggleCatalog = { itemId, enabled ->
                        scope.launch { controller.setCatalogItemEnabled(itemId, enabled) }
                    },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    state: EconomyState.Ready,
    config: ManageDecision,
    payoutRules: ManageDecision,
    onSave: (CurrencyConfig) -> Unit,
    onFreeze: (String, Boolean) -> Unit,
    onToggleCatalog: (String, Boolean) -> Unit,
) {
    val spacing = LocalSpacing.current
    val loaded: CurrencyConfig = state.config

    // Local editable form, re-seeded whenever a new config loads (initial load or a successful save). Holding it
    // screen-side keeps the controller a thin persistence boundary; `remember(loaded)` resets every field to the
    // saved baseline so the "differs from loaded" check is exact.
    var isEnabled: Boolean by remember(loaded) { mutableStateOf(loaded.isEnabled) }
    var currencyName: String by remember(loaded) { mutableStateOf(loaded.currencyName) }
    var currencyNamePlural: String by
        remember(loaded) { mutableStateOf(loaded.currencyNamePlural.orEmpty()) }
    var iconUrl: String by remember(loaded) { mutableStateOf(loaded.iconUrl.orEmpty()) }
    var startingBalanceText: String by
        remember(loaded) { mutableStateOf(loaded.startingBalance.toString()) }
    var maxBalanceText: String by
        remember(loaded) { mutableStateOf(loaded.maxBalance?.toString().orEmpty()) }
    var decimalPlacesText: String by
        remember(loaded) { mutableStateOf(loaded.decimalPlaces.toString()) }

    // The confirm-disable dialog is shown only when the user tries to save with the economy turned off after it was
    // enabled — turning it off stops viewers earning, so it is the page's destructive action. Holds the pending
    // edited config while the operator confirms; null = no confirmation pending.
    var pendingDisable: CurrencyConfig? by remember { mutableStateOf(null) }

    val startingBalance: Long? = startingBalanceText.toLongOrNull()
    val maxBalance: Long? = maxBalanceText.toLongOrNull()
    val decimalPlaces: Int? = decimalPlacesText.toIntOrNull()

    val nameValid: Boolean = currencyName.isNotBlank()
    val startingValid: Boolean = startingBalance != null && startingBalance >= 0
    val maxValid: Boolean = maxBalanceText.isBlank() || (maxBalance != null && maxBalance >= 0)
    val decimalsValid: Boolean = decimalPlaces != null && decimalPlaces in 0..8
    val rangeValid: Boolean =
        maxBalance == null || (startingBalance != null && startingBalance <= maxBalance)
    val formValid: Boolean =
        nameValid && startingValid && maxValid && decimalsValid && rangeValid

    val edited: CurrencyConfig =
        loaded.copy(
            isEnabled = isEnabled,
            currencyName = currencyName.trim(),
            currencyNamePlural = currencyNamePlural.trim().ifBlank { null },
            iconUrl = iconUrl.trim().ifBlank { null },
            startingBalance = startingBalance ?: loaded.startingBalance,
            maxBalance = if (maxBalanceText.isBlank()) null else maxBalance ?: loaded.maxBalance,
            decimalPlaces = decimalPlaces ?: loaded.decimalPlaces,
        )

    // Save is offered only when the form is valid AND actually differs from the saved baseline — saving an
    // unchanged config is a no-op the user shouldn't be invited to make.
    val canSave: Boolean = formValid && edited != loaded && !state.saving

    // Save normally; but if this save turns a previously-enabled economy off, route through the confirm dialog
    // first (the destructive consequence). Creating/editing while disabled, or any enabling save, goes straight
    // through.
    val onSaveClick: () -> Unit = {
        if (loaded.isEnabled && !edited.isEnabled) pendingDisable = edited else onSave(edited)
    }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatusBanner(isEnabled = isEnabled, currencyName = edited.currencyName)

        // The general editor is enabled only while not saving AND the caller clears the Editor config floor; the
        // earn-rule number fields additionally require the Broadcaster payout floor (combined below).
        val configEnabled: Boolean = !state.saving && config.isAllowed
        val payoutEnabled: Boolean = !state.saving && payoutRules.isAllowed

        EditCard(
            isEnabled = isEnabled,
            onEnabledChange = { isEnabled = it },
            currencyName = currencyName,
            onCurrencyNameChange = { currencyName = it },
            nameValid = nameValid,
            currencyNamePlural = currencyNamePlural,
            onCurrencyNamePluralChange = { currencyNamePlural = it },
            iconUrl = iconUrl,
            onIconUrlChange = { iconUrl = it },
            startingBalanceText = startingBalanceText,
            onStartingBalanceChange = { startingBalanceText = it.filter { c -> c.isDigit() } },
            startingValid = startingValid,
            maxBalanceText = maxBalanceText,
            onMaxBalanceChange = { maxBalanceText = it.filter { c -> c.isDigit() } },
            maxValid = maxValid && rangeValid,
            decimalPlacesText = decimalPlacesText,
            onDecimalPlacesChange = { decimalPlacesText = it.filter { c -> c.isDigit() } },
            decimalsValid = decimalsValid,
            configEnabled = configEnabled,
            payoutEnabled = payoutEnabled,
            payoutRules = payoutRules,
        )

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            // Saving persists the whole config, so it gates at the page's Editor floor; the earn-rule fields are
            // already individually locked for a non-Broadcaster, so an Editor can save the parts they may edit.
            manage = config,
            canSave = canSave && config.isAllowed,
            onSave = onSaveClick,
        )

        LeaderboardSection(entries = state.leaderboard)

        AccountsSection(accounts = state.accounts, manage = config, onFreeze = onFreeze)

        EarningRulesSection(rules = state.earningRules)

        CatalogSection(catalog = state.catalog, manage = config, onToggle = onToggleCatalog)
    }

    pendingDisable?.let { edit ->
        ConfirmDialog(
            title = stringResource(Res.string.economy_disable_confirm_title),
            message = stringResource(Res.string.economy_disable_confirm_message),
            confirmLabel = stringResource(Res.string.economy_disable_confirm_confirm),
            dismissLabel = stringResource(Res.string.economy_disable_confirm_cancel),
            destructive = true,
            onConfirm = {
                pendingDisable = null
                onSave(edit)
            },
            onDismiss = { pendingDisable = null },
        )
    }
}

@Composable
private fun StatusBanner(isEnabled: Boolean, currencyName: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusText: String =
        stringResource(
            if (isEnabled) Res.string.economy_status_enabled else Res.string.economy_status_disabled,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "Economy enabled" rather than a disconnected dot + label.
            .clearAndSetSemantics { contentDescription = statusText },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Box(
            modifier = Modifier
                .size(spacing.s2)
                .clip(CircleShape)
                .background(if (isEnabled) tokens.primary else tokens.mutedForeground),
        )
        Text(
            text = statusText,
            style = typography.xl,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        if (currencyName.isNotBlank()) {
            Text(
                text = currencyName,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.wrapContentWidth(),
            )
        }
    }
}

@Composable
private fun EditCard(
    isEnabled: Boolean,
    onEnabledChange: (Boolean) -> Unit,
    currencyName: String,
    onCurrencyNameChange: (String) -> Unit,
    nameValid: Boolean,
    currencyNamePlural: String,
    onCurrencyNamePluralChange: (String) -> Unit,
    iconUrl: String,
    onIconUrlChange: (String) -> Unit,
    startingBalanceText: String,
    onStartingBalanceChange: (String) -> Unit,
    startingValid: Boolean,
    maxBalanceText: String,
    onMaxBalanceChange: (String) -> Unit,
    maxValid: Boolean,
    decimalPlacesText: String,
    onDecimalPlacesChange: (String) -> Unit,
    decimalsValid: Boolean,
    configEnabled: Boolean,
    payoutEnabled: Boolean,
    payoutRules: ManageDecision,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        SwitchRow(
            label = stringResource(Res.string.economy_toggle_enabled),
            checked = isEnabled,
            onCheckedChange = onEnabledChange,
            enabled = configEnabled,
        )
        TextField(
            value = currencyName,
            onValueChange = onCurrencyNameChange,
            label = stringResource(Res.string.economy_label_currency_name),
            enabled = configEnabled,
            isError = !nameValid,
            errorText = stringResource(Res.string.economy_name_invalid),
        )
        TextField(
            value = currencyNamePlural,
            onValueChange = onCurrencyNamePluralChange,
            label = stringResource(Res.string.economy_label_currency_name_plural),
            enabled = configEnabled,
        )
        TextField(
            value = iconUrl,
            onValueChange = onIconUrlChange,
            label = stringResource(Res.string.economy_label_icon_url),
            enabled = configEnabled,
        )
        // The earn / payout rules (frontend-ia.md §3): the economic parameters that change how viewers earn —
        // Broadcaster-only. Each gates through ManageGate so the field is disabled WITH a reason a screen reader
        // announces when the caller is below the Broadcaster floor.
        ManageGate(decision = payoutRules) {
            NumberField(
                value = startingBalanceText,
                onValueChange = onStartingBalanceChange,
                label = stringResource(Res.string.economy_label_starting_balance),
                enabled = payoutEnabled,
                valid = startingValid,
            )
        }
        ManageGate(decision = payoutRules) {
            NumberField(
                value = maxBalanceText,
                onValueChange = onMaxBalanceChange,
                label = stringResource(Res.string.economy_label_max_balance),
                enabled = payoutEnabled,
                valid = maxValid,
            )
        }
        ManageGate(decision = payoutRules) {
            NumberField(
                value = decimalPlacesText,
                onValueChange = onDecimalPlacesChange,
                label = stringResource(Res.string.economy_label_decimal_places),
                enabled = payoutEnabled,
                valid = decimalsValid,
            )
        }
    }
}

@Composable
private fun SwitchRow(
    label: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            // One toggleable node for screen readers: the label names what the switch controls.
            .clearAndSetSemantics { contentDescription = label },
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = label,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            enabled = enabled,
            colors = switchColors(),
        )
    }
}

@Composable
private fun TextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    isError: Boolean = false,
    errorText: String? = null,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        isError = isError,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(label, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        colors = fieldColors(),
        supportingText =
            if (isError && errorText != null) {
                { Text(errorText) }
            } else {
                null
            },
    )
}

@Composable
private fun NumberField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    valid: Boolean,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        isError = !valid,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(label, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        colors = fieldColors(),
    )
}

@Composable
private fun SaveBar(
    saving: Boolean,
    justSaved: Boolean,
    saveError: String?,
    manage: ManageDecision,
    canSave: Boolean,
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
        // The save feedback line: an error takes priority, then the transient "Saved" confirmation.
        when {
            saveError != null ->
                Text(
                    text = stringResource(Res.string.economy_save_error, saveError),
                    style = typography.sm,
                    color = tokens.destructive,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
            justSaved ->
                Text(
                    text = stringResource(Res.string.economy_saved),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
            else -> Box(modifier = Modifier.weight(1f))
        }

        if (saving) {
            val savingLabel: String = stringResource(Res.string.economy_saving)
            CircularProgressIndicator(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = savingLabel },
            )
        } else {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = onSave,
                    enabled = canSave && enabled,
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(stringResource(Res.string.economy_save), maxLines = 1)
                }
            }
        }
    }
}

// The read-only top-holders ranking. A configured-but-empty economy (or no configured leaderboard) shows the
// empty line rather than a fabricated list.
@Composable
private fun LeaderboardSection(entries: List<LeaderboardEntry>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.economy_leaderboard_title),
        style = typography.lg,
        color = tokens.cardForeground,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )

    if (entries.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_leaderboard_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = entries, key = { entry -> "${entry.rank}-${entry.displayName}" }) { entry ->
            LeaderboardRow(entry = entry)
        }
    }
}

@Composable
private fun LeaderboardRow(entry: LeaderboardEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val valueText: String = entry.points.toString()
    // One node for screen readers describing the row: "#1, Stoney_Eagle, 1200".
    val rowDescription: String =
        stringResource(
            Res.string.economy_leaderboard_row_description,
            entry.rank,
            entry.displayName,
            valueText,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = "#${entry.rank}",
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 1,
            modifier = Modifier.wrapContentWidth(),
        )
        Text(
            text = entry.displayName,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Text(
            text = valueText,
            style = typography.base,
            color = tokens.primary,
            maxLines = 1,
            modifier = Modifier.wrapContentWidth(),
        )
    }
}

// The account-admin list (economy.md §4): one row per viewer account — the holder (Twitch id), a frozen flag,
// the current balance, and a freeze / unfreeze action gated at the page's Editor floor. Balance adjustments are
// a follow-up management surface.
@Composable
private fun AccountsSection(
    accounts: List<CurrencyAccountSummary>,
    manage: ManageDecision,
    onFreeze: (String, Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.economy_accounts_title),
        style = typography.lg,
        color = tokens.cardForeground,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )

    if (accounts.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_accounts_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = accounts, key = { it.id }) { account ->
            AccountRow(account = account, manage = manage, onFreeze = onFreeze)
        }
    }
}

@Composable
private fun AccountRow(
    account: CurrencyAccountSummary,
    manage: ManageDecision,
    onFreeze: (String, Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val rowDescription: String =
        stringResource(
            Res.string.economy_accounts_row_description,
            account.viewerTwitchUserId,
            account.balance,
        )
    val freezeLabel: String =
        stringResource(
            if (account.isFrozen) Res.string.economy_account_unfreeze_action
            else Res.string.economy_account_freeze_action,
            account.viewerTwitchUserId,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // The descriptive portion is one semantics node ("39863651, balance 1200"); the freeze button below keeps
        // its own semantics so it stays individually reachable.
        Row(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = account.viewerTwitchUserId,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (account.isFrozen) {
                Text(
                    text = stringResource(Res.string.economy_account_frozen),
                    style = typography.sm,
                    color = tokens.destructive,
                    maxLines = 1,
                    modifier = Modifier.wrapContentWidth(),
                )
            }
            Text(
                text = account.balance.toString(),
                style = typography.base,
                color = tokens.primary,
                maxLines = 1,
                modifier = Modifier.wrapContentWidth(),
            )
        }
        // Freeze / unfreeze — Editor floor (ManageGate); the backend re-checks economy:account:freeze.
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onFreeze(account.viewerUserId, !account.isFrozen) },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = freezeLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (account.isFrozen) Res.string.economy_account_unfreeze
                            else Res.string.economy_account_freeze
                        ),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// The earning rules (economy.md §4): one row per source — the source key, a disabled flag, and the gain rate.
// Read-only here; rate / cap editing is a follow-up management surface.
@Composable
private fun EarningRulesSection(rules: List<EarningRule>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.economy_earning_title),
        style = typography.lg,
        color = tokens.cardForeground,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )

    if (rules.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_earning_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = rules, key = { it.id }) { rule -> EarningRuleRow(rule = rule) }
    }
}

@Composable
private fun EarningRuleRow(rule: EarningRule) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val rowDescription: String =
        stringResource(Res.string.economy_earning_row_description, rule.source, rule.rate)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = rule.source,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        if (!rule.isEnabled) {
            Text(
                text = stringResource(Res.string.economy_earning_disabled),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                modifier = Modifier.wrapContentWidth(),
            )
        }
        Text(
            text = "+${rule.rate}",
            style = typography.base,
            color = tokens.primary,
            maxLines = 1,
            modifier = Modifier.wrapContentWidth(),
        )
    }
}

// The store catalog (economy.md §4): one row per item — the name, a disabled flag, stock (for limited items),
// and the cost. Read-only here; create / edit / delete + purchase history are a follow-up management surface.
@Composable
private fun CatalogSection(
    catalog: List<CatalogItem>,
    manage: ManageDecision,
    onToggle: (String, Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.economy_catalog_title),
        style = typography.lg,
        color = tokens.cardForeground,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )

    if (catalog.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_catalog_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = catalog, key = { it.id }) { item ->
            CatalogItemRow(item = item, manage = manage, onToggle = onToggle)
        }
    }
}

@Composable
private fun CatalogItemRow(
    item: CatalogItem,
    manage: ManageDecision,
    onToggle: (String, Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val rowDescription: String =
        stringResource(Res.string.economy_catalog_row_description, item.name, item.cost)
    val toggleLabel: String =
        stringResource(
            if (item.isEnabled) Res.string.economy_catalog_disable_action
            else Res.string.economy_catalog_enable_action,
            item.name,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // The descriptive portion is one semantics node; the enable/disable button keeps its own.
        Row(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = item.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (!item.isEnabled) {
                Text(
                    text = stringResource(Res.string.economy_catalog_disabled),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    modifier = Modifier.wrapContentWidth(),
                )
            }
            // Stock only for limited items — a null stockLimit means unlimited, so show nothing.
            val limit: Int? = item.stockLimit
            if (limit != null) {
                Text(
                    text =
                        stringResource(
                            Res.string.economy_catalog_stock,
                            item.stockRemaining ?: 0,
                            limit,
                        ),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    modifier = Modifier.wrapContentWidth(),
                )
            }
            Text(
                text = item.cost.toString(),
                style = typography.base,
                color = tokens.primary,
                maxLines = 1,
                modifier = Modifier.wrapContentWidth(),
            )
        }
        // Enable / disable — Editor floor (ManageGate); the backend re-checks economy:catalog:update.
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onToggle(item.id, !item.isEnabled) },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (item.isEnabled) Res.string.economy_catalog_disable
                            else Res.string.economy_catalog_enable
                        ),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// The shared switch color set: every slot driven by a token so the control reads on-theme in light + dark.
@Composable
private fun switchColors() =
    SwitchDefaults.colors(
        checkedThumbColor = LocalTokens.current.primaryForeground,
        checkedTrackColor = LocalTokens.current.primary,
        uncheckedThumbColor = LocalTokens.current.mutedForeground,
        uncheckedTrackColor = LocalTokens.current.muted,
        uncheckedBorderColor = LocalTokens.current.border,
    )

// The shared text-field color set: every slot driven by a token so the field reads on-theme in light + dark.
@Composable
private fun fieldColors(): TextFieldColors {
    val tokens: Tokens = LocalTokens.current
    return OutlinedTextFieldDefaults.colors(
        focusedTextColor = tokens.cardForeground,
        unfocusedTextColor = tokens.cardForeground,
        disabledTextColor = tokens.mutedForeground,
        focusedBorderColor = tokens.ring,
        unfocusedBorderColor = tokens.border,
        disabledBorderColor = tokens.border,
        errorBorderColor = tokens.destructive,
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
        errorLabelColor = tokens.destructive,
        cursorColor = tokens.primary,
    )
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
                text = stringResource(Res.string.economy_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.economy_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
