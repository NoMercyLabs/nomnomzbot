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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.Text

import bot.nomnomz.dashboard.core.designsystem.component.TextButton
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
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import androidx.compose.foundation.layout.heightIn
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.CatalogPurchase
import bot.nomnomz.dashboard.core.network.CreateCatalogItemBody
import bot.nomnomz.dashboard.core.network.AdminJarContributeBody
import bot.nomnomz.dashboard.core.network.AdminJarWithdrawBody
import bot.nomnomz.dashboard.core.network.CreateSavingsJarBody
import bot.nomnomz.dashboard.core.network.InviteChannelBody
import bot.nomnomz.dashboard.core.network.JarMovement
import bot.nomnomz.dashboard.core.network.SavingsJarDetail
import bot.nomnomz.dashboard.core.network.SavingsJarMembership
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.CurrencyConfig
import bot.nomnomz.dashboard.core.network.CurrencyLedgerEntry
import bot.nomnomz.dashboard.core.network.EarningRule
import bot.nomnomz.dashboard.core.network.UpsertEarningRuleBody
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.TransferBody
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
import nomnomzbot.composeapp.generated.resources.economy_catalog_add
import nomnomzbot.composeapp.generated.resources.economy_catalog_cancel
import nomnomzbot.composeapp.generated.resources.economy_catalog_cost
import nomnomzbot.composeapp.generated.resources.economy_catalog_cost_invalid
import nomnomzbot.composeapp.generated.resources.economy_catalog_create
import nomnomzbot.composeapp.generated.resources.economy_catalog_create_title
import nomnomzbot.composeapp.generated.resources.economy_catalog_delete
import nomnomzbot.composeapp.generated.resources.economy_catalog_delete_action
import nomnomzbot.composeapp.generated.resources.economy_catalog_delete_confirm
import nomnomzbot.composeapp.generated.resources.economy_catalog_delete_dismiss
import nomnomzbot.composeapp.generated.resources.economy_catalog_delete_message
import nomnomzbot.composeapp.generated.resources.economy_catalog_delete_title
import nomnomzbot.composeapp.generated.resources.economy_catalog_description
import nomnomzbot.composeapp.generated.resources.economy_catalog_enable
import nomnomzbot.composeapp.generated.resources.economy_catalog_enable_action
import nomnomzbot.composeapp.generated.resources.economy_catalog_name
import nomnomzbot.composeapp.generated.resources.economy_catalog_name_required
import nomnomzbot.composeapp.generated.resources.economy_catalog_empty
import nomnomzbot.composeapp.generated.resources.economy_catalog_row_description
import nomnomzbot.composeapp.generated.resources.economy_catalog_stock
import nomnomzbot.composeapp.generated.resources.economy_catalog_title
import nomnomzbot.composeapp.generated.resources.economy_disable_confirm_cancel
import nomnomzbot.composeapp.generated.resources.economy_earning_add
import nomnomzbot.composeapp.generated.resources.economy_earning_edit
import nomnomzbot.composeapp.generated.resources.economy_earning_edit_action
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_create_title
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_source_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_rate_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_window_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_window_cap_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_stream_cap_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_role_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_save
import nomnomzbot.composeapp.generated.resources.economy_earning_dialog_cancel
import nomnomzbot.composeapp.generated.resources.economy_earning_role_everyone
import nomnomzbot.composeapp.generated.resources.economy_earning_role_subscriber
import nomnomzbot.composeapp.generated.resources.economy_earning_role_vip
import nomnomzbot.composeapp.generated.resources.economy_earning_role_moderator
import nomnomzbot.composeapp.generated.resources.economy_earning_source_chat_message
import nomnomzbot.composeapp.generated.resources.economy_earning_source_watch_time
import nomnomzbot.composeapp.generated.resources.economy_earning_source_follow
import nomnomzbot.composeapp.generated.resources.economy_earning_source_subscription
import nomnomzbot.composeapp.generated.resources.economy_earning_source_gift_subscription
import nomnomzbot.composeapp.generated.resources.economy_earning_source_cheer
import nomnomzbot.composeapp.generated.resources.economy_earning_source_raid
import nomnomzbot.composeapp.generated.resources.economy_earning_source_supporter
import nomnomzbot.composeapp.generated.resources.economy_earning_delete
import nomnomzbot.composeapp.generated.resources.economy_earning_delete_cancel
import nomnomzbot.composeapp.generated.resources.economy_earning_delete_confirm
import nomnomzbot.composeapp.generated.resources.economy_earning_delete_message
import nomnomzbot.composeapp.generated.resources.economy_earning_delete_title
import nomnomzbot.composeapp.generated.resources.economy_earning_disable
import nomnomzbot.composeapp.generated.resources.economy_earning_disable_action
import nomnomzbot.composeapp.generated.resources.economy_earning_disabled
import nomnomzbot.composeapp.generated.resources.economy_earning_empty
import nomnomzbot.composeapp.generated.resources.economy_earning_enable
import nomnomzbot.composeapp.generated.resources.economy_earning_enable_action
import nomnomzbot.composeapp.generated.resources.economy_earning_row_description
import nomnomzbot.composeapp.generated.resources.economy_earning_title
import nomnomzbot.composeapp.generated.resources.economy_jars_add
import nomnomzbot.composeapp.generated.resources.economy_jars_close
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute_amount
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute_amount_invalid
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute_cancel
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute_confirm
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute_title
import nomnomzbot.composeapp.generated.resources.economy_jars_contribute_viewer
import nomnomzbot.composeapp.generated.resources.economy_jars_detail_title
import nomnomzbot.composeapp.generated.resources.economy_jars_history
import nomnomzbot.composeapp.generated.resources.economy_jars_history_close
import nomnomzbot.composeapp.generated.resources.economy_jars_history_empty
import nomnomzbot.composeapp.generated.resources.economy_jars_history_error
import nomnomzbot.composeapp.generated.resources.economy_jars_history_title
import nomnomzbot.composeapp.generated.resources.economy_jars_invite
import nomnomzbot.composeapp.generated.resources.economy_jars_invite_broadcaster
import nomnomzbot.composeapp.generated.resources.economy_jars_invite_cancel
import nomnomzbot.composeapp.generated.resources.economy_jars_invite_confirm
import nomnomzbot.composeapp.generated.resources.economy_jars_invite_role
import nomnomzbot.composeapp.generated.resources.economy_jars_invite_title
import nomnomzbot.composeapp.generated.resources.economy_jars_manage
import nomnomzbot.composeapp.generated.resources.economy_jars_membership_accept
import nomnomzbot.composeapp.generated.resources.economy_jars_membership_accepted
import nomnomzbot.composeapp.generated.resources.economy_jars_membership_pending
import nomnomzbot.composeapp.generated.resources.economy_jars_membership_revoke
import nomnomzbot.composeapp.generated.resources.economy_jars_memberships
import nomnomzbot.composeapp.generated.resources.economy_jars_memberships_empty
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw_amount
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw_amount_invalid
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw_cancel
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw_confirm
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw_title
import nomnomzbot.composeapp.generated.resources.economy_jars_withdraw_viewer
import nomnomzbot.composeapp.generated.resources.economy_jars_balance
import nomnomzbot.composeapp.generated.resources.economy_jars_cancel
import nomnomzbot.composeapp.generated.resources.economy_jars_closed
import nomnomzbot.composeapp.generated.resources.economy_jars_create
import nomnomzbot.composeapp.generated.resources.economy_jars_create_title
import nomnomzbot.composeapp.generated.resources.economy_jars_description
import nomnomzbot.composeapp.generated.resources.economy_jars_empty
import nomnomzbot.composeapp.generated.resources.economy_jars_goal
import nomnomzbot.composeapp.generated.resources.economy_jars_goal_progress
import nomnomzbot.composeapp.generated.resources.economy_jars_name
import nomnomzbot.composeapp.generated.resources.economy_jars_name_required
import nomnomzbot.composeapp.generated.resources.economy_jars_open
import nomnomzbot.composeapp.generated.resources.economy_jars_row_description
import nomnomzbot.composeapp.generated.resources.economy_jars_title
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
import nomnomzbot.composeapp.generated.resources.economy_account_adjust
import nomnomzbot.composeapp.generated.resources.economy_account_adjust_amount
import nomnomzbot.composeapp.generated.resources.economy_account_adjust_amount_invalid
import nomnomzbot.composeapp.generated.resources.economy_account_adjust_cancel
import nomnomzbot.composeapp.generated.resources.economy_account_adjust_confirm
import nomnomzbot.composeapp.generated.resources.economy_account_adjust_reason
import nomnomzbot.composeapp.generated.resources.economy_account_adjust_title
import nomnomzbot.composeapp.generated.resources.economy_account_ledger
import nomnomzbot.composeapp.generated.resources.economy_ledger_close
import nomnomzbot.composeapp.generated.resources.economy_ledger_empty
import nomnomzbot.composeapp.generated.resources.economy_ledger_error
import nomnomzbot.composeapp.generated.resources.economy_ledger_title
import nomnomzbot.composeapp.generated.resources.economy_transfer
import nomnomzbot.composeapp.generated.resources.economy_transfer_amount
import nomnomzbot.composeapp.generated.resources.economy_transfer_amount_invalid
import nomnomzbot.composeapp.generated.resources.economy_transfer_cancel
import nomnomzbot.composeapp.generated.resources.economy_transfer_confirm
import nomnomzbot.composeapp.generated.resources.economy_transfer_from
import nomnomzbot.composeapp.generated.resources.economy_transfer_reason
import nomnomzbot.composeapp.generated.resources.economy_transfer_same_account
import nomnomzbot.composeapp.generated.resources.economy_transfer_title
import nomnomzbot.composeapp.generated.resources.economy_transfer_to
import nomnomzbot.composeapp.generated.resources.economy_purchases_buyer
import nomnomzbot.composeapp.generated.resources.economy_purchases_empty
import nomnomzbot.composeapp.generated.resources.economy_purchases_refund
import nomnomzbot.composeapp.generated.resources.economy_purchases_refund_cancel
import nomnomzbot.composeapp.generated.resources.economy_purchases_refund_confirm
import nomnomzbot.composeapp.generated.resources.economy_purchases_refund_message
import nomnomzbot.composeapp.generated.resources.economy_purchases_refund_title
import nomnomzbot.composeapp.generated.resources.economy_purchases_status
import nomnomzbot.composeapp.generated.resources.economy_purchases_title
import nomnomzbot.composeapp.generated.resources.shell_nav_economy
import org.jetbrains.compose.resources.StringResource
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
                    onCreateCatalogItem = { request ->
                        scope.launch { controller.createCatalogItem(request) }
                    },
                    onDeleteCatalogItem = { itemId ->
                        scope.launch { controller.deleteCatalogItem(itemId) }
                    },
                    onToggleEarningRule = { source, enabled ->
                        scope.launch { controller.toggleEarningRule(source, enabled) }
                    },
                    onUpsertEarningRule = { body ->
                        scope.launch { controller.upsertEarningRule(body) }
                    },
                    onDeleteEarningRule = { ruleId ->
                        scope.launch { controller.deleteEarningRule(ruleId) }
                    },
                    onCreateSavingsJar = { request ->
                        scope.launch { controller.createSavingsJar(request) }
                    },
                    loadJarDetail = controller::getJar,
                    onJarInvite = { jarId, request -> controller.inviteChannel(jarId, request) },
                    onJarAcceptMembership = { membershipId -> controller.acceptMembership(membershipId) },
                    onJarRemoveMembership = { membershipId -> controller.removeMembership(membershipId) },
                    onJarContribute = { jarId, request -> controller.contribute(jarId, request) },
                    onJarWithdraw = { jarId, request -> controller.withdraw(jarId, request) },
                    loadJarHistory = controller::jarHistory,
                    onAdjustAccount = { viewerUserId, amount, reason ->
                        scope.launch { controller.adjustAccount(viewerUserId, amount, reason) }
                    },
                    loadLedger = controller::loadLedger,
                    onTransfer = { request ->
                        scope.launch { controller.transfer(request) }
                    },
                    searchViewers = { query -> controller.searchViewers(query) },
                    onRefundPurchase = { purchaseId ->
                        scope.launch { controller.refundPurchase(purchaseId) }
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
    onCreateCatalogItem: (CreateCatalogItemBody) -> Unit,
    onDeleteCatalogItem: (String) -> Unit,
    onToggleEarningRule: (source: String, enabled: Boolean) -> Unit,
    onUpsertEarningRule: (UpsertEarningRuleBody) -> Unit,
    onDeleteEarningRule: (ruleId: String) -> Unit,
    onCreateSavingsJar: (CreateSavingsJarBody) -> Unit,
    loadJarDetail: suspend (jarId: String) -> SavingsJarDetail?,
    // The jar-detail mutations are suspend so the manage dialog can await them and reload its own detail — a
    // fire-and-forget write left the dialog's membership list / balance stale until it was reopened.
    onJarInvite: suspend (jarId: String, InviteChannelBody) -> Unit,
    onJarAcceptMembership: suspend (membershipId: String) -> Unit,
    onJarRemoveMembership: suspend (membershipId: String) -> Unit,
    onJarContribute: suspend (jarId: String, AdminJarContributeBody) -> Unit,
    onJarWithdraw: suspend (jarId: String, AdminJarWithdrawBody) -> Unit,
    loadJarHistory: suspend (jarId: String) -> List<JarMovement>?,
    onAdjustAccount: (viewerUserId: String, amount: Long, reason: String?) -> Unit,
    loadLedger: suspend (viewerUserId: String) -> List<CurrencyLedgerEntry>?,
    onTransfer: (TransferBody) -> Unit,
    searchViewers: suspend (query: String) -> List<PickerOption>,
    onRefundPurchase: (purchaseId: Long) -> Unit,
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
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_economy))
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

        AccountsSection(
            accounts = state.accounts,
            manage = config,
            onFreeze = onFreeze,
            onAdjust = onAdjustAccount,
            loadLedger = loadLedger,
            onTransfer = onTransfer,
            searchViewers = searchViewers,
        )

        EarningRulesSection(
            rules = state.earningRules,
            manage = payoutRules,
            onToggle = onToggleEarningRule,
            onUpsert = onUpsertEarningRule,
            onDelete = onDeleteEarningRule,
        )

        CatalogSection(
            catalog = state.catalog,
            manage = config,
            onToggle = onToggleCatalog,
            onCreate = onCreateCatalogItem,
            onDelete = onDeleteCatalogItem,
        )

        SavingsJarsSection(
            jars = state.savingsJars,
            manage = config,
            onCreate = onCreateSavingsJar,
            loadJarDetail = loadJarDetail,
            onInvite = onJarInvite,
            onAcceptMembership = onJarAcceptMembership,
            onRemoveMembership = onJarRemoveMembership,
            onContribute = onJarContribute,
            onWithdraw = onJarWithdraw,
            loadHistory = loadJarHistory,
        )

        CatalogPurchasesSection(
            purchases = state.catalogPurchases,
            manage = config,
            onRefund = onRefundPurchase,
        )
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
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        isError = isError,
        modifier = Modifier.fillMaxWidth(),
        label = label,
        errorText = errorText,
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
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        isError = !valid,
        modifier = Modifier.fillMaxWidth(),
        label = label,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
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
            Spinner(
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

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth()) {
            entries.forEachIndexed { index, entry ->
                LeaderboardRow(entry = entry)
                if (index < entries.lastIndex) {
                    Separator()
                }
            }
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
    onAdjust: (viewerUserId: String, amount: Long, reason: String?) -> Unit,
    loadLedger: suspend (viewerUserId: String) -> List<CurrencyLedgerEntry>?,
    onTransfer: (TransferBody) -> Unit,
    searchViewers: suspend (query: String) -> List<PickerOption>,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var showTransfer: Boolean by remember { mutableStateOf(false) }

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = stringResource(Res.string.economy_accounts_title),
            style = typography.lg,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { showTransfer = true }, enabled = enabled) {
                Text(stringResource(Res.string.economy_transfer))
            }
        }
    }

    if (accounts.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_accounts_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
    } else {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.fillMaxWidth()) {
                accounts.forEachIndexed { index, account ->
                    AccountRow(
                        account = account,
                        manage = manage,
                        onFreeze = onFreeze,
                        onAdjust = onAdjust,
                        loadLedger = loadLedger,
                        searchViewers = searchViewers,
                    )
                    if (index < accounts.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }

    if (showTransfer) {
        TransferDialog(
            searchViewers = searchViewers,
            onConfirm = { request -> showTransfer = false; onTransfer(request) },
            onDismiss = { showTransfer = false },
        )
    }
}

@Composable
private fun AccountRow(
    account: CurrencyAccountSummary,
    manage: ManageDecision,
    onFreeze: (String, Boolean) -> Unit,
    onAdjust: (viewerUserId: String, amount: Long, reason: String?) -> Unit,
    loadLedger: suspend (viewerUserId: String) -> List<CurrencyLedgerEntry>?,
    searchViewers: suspend (query: String) -> List<PickerOption>,
) {
    var showAdjust: Boolean by remember { mutableStateOf(false) }
    var showLedger: Boolean by remember { mutableStateOf(false) }
    var ledgerEntries: List<CurrencyLedgerEntry>? by remember { mutableStateOf(null) }
    var ledgerLoading: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(showLedger) {
        if (showLedger && ledgerEntries == null) {
            ledgerLoading = true
            ledgerEntries = loadLedger(account.viewerUserId)
            ledgerLoading = false
        }
    }
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
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { showAdjust = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.economy_account_adjust),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        TextButton(onClick = { showLedger = true }) {
            Text(
                text = stringResource(Res.string.economy_account_ledger),
                color = tokens.primary,
                maxLines = 1,
            )
        }
    }

    if (showAdjust) {
        AccountAdjustDialog(
            // Opened from a row, so the target is pre-selected (the row already carries the platform User GUID);
            // the picker shows it with a "Change" affordance so the operator can retarget without leaving.
            initial = PickerRef(account.viewerUserId, account.viewerTwitchUserId),
            searchViewers = searchViewers,
            onConfirm = { viewerUserId, amount, reason ->
                showAdjust = false
                onAdjust(viewerUserId, amount, reason)
            },
            onDismiss = { showAdjust = false },
        )
    }

    if (showLedger) {
        LedgerDialog(
            viewerLabel = account.viewerTwitchUserId,
            loading = ledgerLoading,
            entries = ledgerEntries,
            onDismiss = { showLedger = false; ledgerEntries = null },
        )
    }
}

@Composable
private fun AccountAdjustDialog(
    initial: PickerRef?,
    searchViewers: suspend (query: String) -> List<PickerOption>,
    onConfirm: (viewerUserId: String, amount: Long, reason: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The account to adjust — [PickerRef.id] is the platform User GUID the adjust write keys on. Seeded from the
    // row that opened the dialog; the picker lets the operator retarget to any other viewer.
    var target: PickerRef? by remember { mutableStateOf(initial) }
    var amountText: String by remember { mutableStateOf("") }
    var reason: String by remember { mutableStateOf("") }
    val amount: Long? = amountText.toLongOrNull()
    val amountValid: Boolean = amount != null
    val canConfirm: Boolean = amountValid && target != null

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.economy_account_adjust_title, target?.name.orEmpty()), style = typography.lg, color = tokens.cardForeground) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                SearchPickerField(
                    search = searchViewers,
                    selected = target,
                    onSelect = { ref -> target = ref },
                    onClear = { target = null },
                )
                AppTextField(
                    value = amountText,
                    onValueChange = { amountText = it.filter { c -> c == '-' || c.isDigit() } },
                    label = stringResource(Res.string.economy_account_adjust_amount),
                    isError = amountText.isNotBlank() && !amountValid,
                    errorText = if (amountText.isNotBlank() && !amountValid) stringResource(Res.string.economy_account_adjust_amount_invalid) else null,
                )
                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.economy_account_adjust_reason),
                    isError = false,
                    errorText = null,
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val picked: PickerRef? = target
                    if (canConfirm && picked != null) {
                        onConfirm(picked.id, amount!!, reason.trim().ifBlank { null })
                    }
                },
                enabled = canConfirm,
            ) {
                Text(stringResource(Res.string.economy_account_adjust_confirm))
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.economy_account_adjust_cancel)) } },
    )
}

@Composable
private fun LedgerDialog(
    viewerLabel: String,
    loading: Boolean,
    entries: List<CurrencyLedgerEntry>?,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                stringResource(Res.string.economy_ledger_title, viewerLabel),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            when {
                loading -> Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    Spinner()
                }
                entries == null -> Text(
                    stringResource(Res.string.economy_ledger_error),
                    style = typography.sm,
                    color = tokens.destructive,
                )
                entries.isEmpty() -> Text(
                    stringResource(Res.string.economy_ledger_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                else -> LazyColumn(
                    modifier = Modifier.heightIn(max = 400.dp),
                    verticalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    items(entries, key = { it.id }) { entry ->
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = entry.entryType,
                                    style = typography.sm,
                                    color = tokens.cardForeground,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                                entry.reason?.let { r ->
                                    Text(
                                        text = r,
                                        style = typography.xs,
                                        color = tokens.mutedForeground,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                }
                            }
                            Text(
                                text = if (entry.amount >= 0) "+${entry.amount}" else "${entry.amount}",
                                style = typography.sm,
                                color = if (entry.amount >= 0) tokens.primary else tokens.destructive,
                                maxLines = 1,
                            )
                        }
                        Separator()
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.economy_ledger_close)) } },
    )
}

@Composable
private fun TransferDialog(
    searchViewers: suspend (query: String) -> List<PickerOption>,
    onConfirm: (TransferBody) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Both endpoints are picked from the debounced viewer search — [PickerRef.id] is the platform User GUID the
    // transfer keys on. Null until chosen, so the transfer can't fire against a hand-typed guess.
    var from: PickerRef? by remember { mutableStateOf(null) }
    var to: PickerRef? by remember { mutableStateOf(null) }
    var amountText: String by remember { mutableStateOf("") }
    var reason: String by remember { mutableStateOf("") }

    val amount: Long? = amountText.toLongOrNull()?.takeIf { it > 0 }
    val sameAccount: Boolean = from != null && to != null && from?.id == to?.id
    val canConfirm: Boolean = from != null && to != null && !sameAccount && amount != null

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                stringResource(Res.string.economy_transfer_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                SearchPickerField(
                    search = searchViewers,
                    selected = from,
                    onSelect = { ref -> from = ref },
                    onClear = { from = null },
                    label = stringResource(Res.string.economy_transfer_from),
                )
                SearchPickerField(
                    search = searchViewers,
                    selected = to,
                    onSelect = { ref -> to = ref },
                    onClear = { to = null },
                    label = stringResource(Res.string.economy_transfer_to),
                )
                if (sameAccount) {
                    Text(
                        text = stringResource(Res.string.economy_transfer_same_account),
                        style = typography.xs,
                        color = tokens.destructive,
                    )
                }
                AppTextField(
                    value = amountText,
                    onValueChange = { amountText = it.filter(Char::isDigit) },
                    label = stringResource(Res.string.economy_transfer_amount),
                    isError = amountText.isNotBlank() && amount == null,
                    errorText = if (amountText.isNotBlank() && amount == null) stringResource(Res.string.economy_transfer_amount_invalid) else null,
                )
                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.economy_transfer_reason),
                    isError = false,
                    errorText = null,
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val fromRef: PickerRef? = from
                    val toRef: PickerRef? = to
                    if (canConfirm && fromRef != null && toRef != null) {
                        onConfirm(
                            TransferBody(
                                fromViewerUserId = fromRef.id,
                                toViewerUserId = toRef.id,
                                amount = amount!!,
                                reason = reason.trim().ifBlank { null },
                            )
                        )
                    }
                },
                enabled = canConfirm,
            ) {
                Text(stringResource(Res.string.economy_transfer_confirm))
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.economy_transfer_cancel)) } },
    )
}

// The earning rules (economy.md §4): one row per source — the source key, a disabled flag, and the gain rate. The
// header carries an Add action (create a rule for any not-yet-configured source); each row edits the FULL rule
// (rate, unit window, per-window / per-stream caps, minimum earning role, enabled) via [EarningRuleDialog].
@Composable
private fun EarningRulesSection(
    rules: List<EarningRule>,
    manage: ManageDecision,
    onToggle: (source: String, enabled: Boolean) -> Unit,
    onUpsert: (UpsertEarningRuleBody) -> Unit,
    onDelete: (ruleId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // null = closed; a non-null editor is open (a seeded rule = edit, a blank editor = create).
    var editor: EarningRuleEditor? by remember { mutableStateOf(null) }

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = stringResource(Res.string.economy_earning_title),
            style = typography.lg,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { editor = EarningRuleEditor.blank() }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.economy_earning_add),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }

    if (rules.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_earning_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
    } else {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.fillMaxWidth()) {
                rules.forEachIndexed { index, rule ->
                    EarningRuleRow(
                        rule = rule,
                        manage = manage,
                        onToggle = { onToggle(rule.source, !rule.isEnabled) },
                        onEdit = { editor = EarningRuleEditor.of(rule) },
                        onDelete = { onDelete(rule.id) },
                    )
                    if (index < rules.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }

    editor?.let { current ->
        EarningRuleDialog(
            editor = current,
            onConfirm = { body ->
                onUpsert(body)
                editor = null
            },
            onDismiss = { editor = null },
        )
    }
}

@Composable
private fun EarningRuleRow(
    rule: EarningRule,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingDelete: Boolean by remember { mutableStateOf(false) }

    val rowDescription: String =
        stringResource(Res.string.economy_earning_row_description, rule.source, rule.rate)
    val toggleLabel: String =
        stringResource(
            if (rule.isEnabled) Res.string.economy_earning_disable_action
            else Res.string.economy_earning_enable_action,
            rule.source,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .semantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = rule.source,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (!rule.isEnabled) {
                Text(
                    text = stringResource(Res.string.economy_earning_disabled),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        Text(
            text = "+${rule.rate}",
            style = typography.base,
            color = tokens.primary,
            maxLines = 1,
            modifier = Modifier.wrapContentWidth(),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onToggle,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (rule.isEnabled) Res.string.economy_earning_disable
                            else Res.string.economy_earning_enable
                        ),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        val editLabel: String = stringResource(Res.string.economy_earning_edit_action, rule.source)
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onEdit,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = editLabel },
            ) {
                Text(
                    text = stringResource(Res.string.economy_earning_edit),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { pendingDelete = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.economy_earning_delete),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }

    if (pendingDelete) {
        ConfirmDialog(
            title = stringResource(Res.string.economy_earning_delete_title),
            message = stringResource(Res.string.economy_earning_delete_message, rule.source),
            confirmLabel = stringResource(Res.string.economy_earning_delete_confirm),
            dismissLabel = stringResource(Res.string.economy_earning_delete_cancel),
            destructive = true,
            onConfirm = { pendingDelete = false; onDelete() },
            onDismiss = { pendingDelete = false },
        )
    }
}

// The create/edit dialog seed. A blank editor opens a create form (source is picked from the not-yet-configured
// sources); an editor seeded from a rule opens a pre-filled edit form with the source fixed (it addresses the rule).
private data class EarningRuleEditor(
    val isEdit: Boolean,
    val source: String,
    val rate: String,
    val unitWindowSeconds: String,
    val perWindowCap: String,
    val perStreamCap: String,
    val minRoleLevel: Int,
    val isEnabled: Boolean,
) {
    companion object {
        fun blank(): EarningRuleEditor =
            EarningRuleEditor(
                isEdit = false,
                source = EarningSourceTokens.first(),
                rate = "",
                unitWindowSeconds = "",
                perWindowCap = "",
                perStreamCap = "",
                minRoleLevel = 0,
                isEnabled = true,
            )

        fun of(rule: EarningRule): EarningRuleEditor =
            EarningRuleEditor(
                isEdit = true,
                source = rule.source,
                rate = rule.rate.toString(),
                unitWindowSeconds = rule.unitWindowSeconds?.toString().orEmpty(),
                perWindowCap = rule.perWindowCap?.toString().orEmpty(),
                perStreamCap = rule.perStreamCap?.toString().orEmpty(),
                minRoleLevel = rule.minRoleLevel ?: 0,
                isEnabled = rule.isEnabled,
            )
    }
}

// The full earning-rule editor: the engagement source (fixed on edit, picked on create), the gain rate per unit,
// the optional unit window (seconds), the per-window and per-stream caps, the minimum earning role (role NAMES
// only), and the enabled flag. A blank cap/window means "no limit" (null); rate must parse to a non-negative whole
// number. Save is disabled while any field is invalid so an invalid rule can never be sent.
@Composable
private fun EarningRuleDialog(
    editor: EarningRuleEditor,
    onConfirm: (UpsertEarningRuleBody) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var source: String by remember { mutableStateOf(editor.source) }
    var rate: String by remember { mutableStateOf(editor.rate) }
    var window: String by remember { mutableStateOf(editor.unitWindowSeconds) }
    var windowCap: String by remember { mutableStateOf(editor.perWindowCap) }
    var streamCap: String by remember { mutableStateOf(editor.perStreamCap) }
    var minRoleLevel: Int by remember { mutableStateOf(editor.minRoleLevel) }
    var isEnabled: Boolean by remember { mutableStateOf(editor.isEnabled) }
    var sourceMenuOpen: Boolean by remember { mutableStateOf(false) }
    var roleMenuOpen: Boolean by remember { mutableStateOf(false) }

    val rateValue: Long? = rate.toLongOrNull()
    val rateValid: Boolean = rateValue != null && rateValue >= 0
    val windowValid: Boolean = window.isBlank() || (window.toIntOrNull()?.let { it > 0 } == true)
    val windowCapValid: Boolean = windowCap.isBlank() || (windowCap.toLongOrNull()?.let { it >= 0 } == true)
    val streamCapValid: Boolean = streamCap.isBlank() || (streamCap.toLongOrNull()?.let { it >= 0 } == true)
    val canSave: Boolean = rateValid && windowValid && windowCapValid && streamCapValid

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(
                    if (editor.isEdit) Res.string.economy_earning_dialog_edit_title
                    else Res.string.economy_earning_dialog_create_title
                ),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                if (editor.isEdit) {
                    AppTextField(
                        value = earningSourceLabel(source),
                        onValueChange = {},
                        enabled = false,
                        label = stringResource(Res.string.economy_earning_dialog_source_label),
                        modifier = Modifier.fillMaxWidth(),
                    )
                } else {
                    EconomyPickerField(
                        label = stringResource(Res.string.economy_earning_dialog_source_label),
                        value = earningSourceLabel(source),
                        expanded = sourceMenuOpen,
                        onExpandedChange = { sourceMenuOpen = it },
                    ) {
                        EarningSourceTokens.forEach { token ->
                            DropdownMenuItem(
                                text = { Text(earningSourceLabel(token), color = tokens.cardForeground) },
                                onClick = {
                                    source = token
                                    sourceMenuOpen = false
                                },
                            )
                        }
                    }
                }
                AppTextField(
                    value = rate,
                    onValueChange = { rate = it },
                    isError = !rateValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    label = stringResource(Res.string.economy_earning_dialog_rate_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = window,
                    onValueChange = { window = it },
                    isError = !windowValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    label = stringResource(Res.string.economy_earning_dialog_window_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = windowCap,
                    onValueChange = { windowCap = it },
                    isError = !windowCapValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    label = stringResource(Res.string.economy_earning_dialog_window_cap_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = streamCap,
                    onValueChange = { streamCap = it },
                    isError = !streamCapValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    label = stringResource(Res.string.economy_earning_dialog_stream_cap_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                EconomyPickerField(
                    label = stringResource(Res.string.economy_earning_dialog_role_label),
                    value = earningRoleLabel(minRoleLevel),
                    expanded = roleMenuOpen,
                    onExpandedChange = { roleMenuOpen = it },
                ) {
                    EarningRoleRungs.forEach { (level, res) ->
                        DropdownMenuItem(
                            text = { Text(stringResource(res), color = tokens.cardForeground) },
                            onClick = {
                                minRoleLevel = level
                                roleMenuOpen = false
                            },
                        )
                    }
                }
                SwitchRow(
                    label = stringResource(Res.string.economy_earning_dialog_enabled_label),
                    checked = isEnabled,
                    onCheckedChange = { isEnabled = it },
                    enabled = true,
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    onConfirm(
                        UpsertEarningRuleBody(
                            source = source,
                            isEnabled = isEnabled,
                            rate = rateValue ?: 0,
                            unitWindowSeconds = window.toIntOrNull(),
                            perWindowCap = windowCap.toLongOrNull(),
                            perStreamCap = streamCap.toLongOrNull(),
                            minRoleLevel = minRoleLevel.takeIf { it > 0 },
                        )
                    )
                },
                enabled = canSave,
            ) {
                Text(stringResource(Res.string.economy_earning_dialog_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_earning_dialog_cancel))
            }
        },
    )
}

// A field-styled select that opens a themed dropdown from anywhere on its surface — the shared [AppSelectField]
// (its whole trigger is clickable, unlike a text field whose body swallows the tap for focus).
@Composable
private fun EconomyPickerField(
    label: String,
    value: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    items: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    AppSelectField(
        label = label,
        value = value,
        expanded = expanded,
        onExpandedChange = onExpandedChange,
        modifier = Modifier.fillMaxWidth(),
        menu = items,
    )
}

// The engagement sources the backend's EarningSource enum accepts (parsed case-insensitively). The stored value is
// the enum name; the label localizes it.
private val EarningSourceTokens: List<String> =
    listOf(
        "ChatMessage",
        "WatchTime",
        "Follow",
        "Subscription",
        "GiftSubscription",
        "Cheer",
        "Raid",
        "Supporter",
    )

@Composable
private fun earningSourceLabel(token: String): String =
    stringResource(
        when (token.lowercase()) {
            "chatmessage", "chat_message" -> Res.string.economy_earning_source_chat_message
            "watchtime", "watch_time" -> Res.string.economy_earning_source_watch_time
            "follow" -> Res.string.economy_earning_source_follow
            "subscription" -> Res.string.economy_earning_source_subscription
            "giftsubscription", "gift_subscription" -> Res.string.economy_earning_source_gift_subscription
            "cheer" -> Res.string.economy_earning_source_cheer
            "raid" -> Res.string.economy_earning_source_raid
            "supporter" -> Res.string.economy_earning_source_supporter
            else -> Res.string.economy_earning_source_chat_message
        }
    )

// The minimum earning-role rungs (CommunityStanding ladder values) — role NAMES only, never the numeric value.
private val EarningRoleRungs: List<Pair<Int, StringResource>> =
    listOf(
        0 to Res.string.economy_earning_role_everyone,
        2 to Res.string.economy_earning_role_subscriber,
        4 to Res.string.economy_earning_role_vip,
        10 to Res.string.economy_earning_role_moderator,
    )

@Composable
private fun earningRoleLabel(level: Int): String =
    stringResource(EarningRoleRungs.lastOrNull { level >= it.first }?.second ?: Res.string.economy_earning_role_everyone)

@Composable
private fun CatalogSection(
    catalog: List<CatalogItem>,
    manage: ManageDecision,
    onToggle: (String, Boolean) -> Unit,
    onCreate: (CreateCatalogItemBody) -> Unit,
    onDelete: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var showCreateDialog: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: CatalogItem? by remember { mutableStateOf(null) }

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = stringResource(Res.string.economy_catalog_title),
            style = typography.lg,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { showCreateDialog = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.economy_catalog_add),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }

    if (catalog.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_catalog_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
    } else {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.fillMaxWidth()) {
                catalog.forEachIndexed { index, item ->
                    CatalogItemRow(
                        item = item,
                        manage = manage,
                        onToggle = onToggle,
                        onDelete = { pendingDelete = item },
                    )
                    if (index < catalog.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }

    if (showCreateDialog) {
        CreateCatalogItemDialog(
            onConfirm = { request ->
                onCreate(request)
                showCreateDialog = false
            },
            onDismiss = { showCreateDialog = false },
        )
    }

    pendingDelete?.let { item ->
        val name: String = item.name
        ConfirmDialog(
            title = stringResource(Res.string.economy_catalog_delete_title),
            message = stringResource(Res.string.economy_catalog_delete_message, name),
            confirmLabel = stringResource(Res.string.economy_catalog_delete_confirm),
            dismissLabel = stringResource(Res.string.economy_catalog_delete_dismiss),
            destructive = true,
            onConfirm = {
                onDelete(item.id)
                pendingDelete = null
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

@Composable
private fun CatalogItemRow(
    item: CatalogItem,
    manage: ManageDecision,
    onToggle: (String, Boolean) -> Unit,
    onDelete: () -> Unit,
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
        // Enable / disable + delete — Editor floor (ManageGate); the backend re-checks economy:catalog:update.
        // Both controls MUST sit in a Row: ManageGate wraps its content in a Box, so two bare siblings would
        // stack and overlap — the Row lays the toggle and the delete button out side by side.
        ManageGate(decision = manage) { enabled ->
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
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
                // Delete — destructive; the caller confirms before calling onDelete.
                val deleteLabel: String = stringResource(Res.string.economy_catalog_delete_action, item.name)
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = deleteLabel,
                    onClick = onDelete,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

@Composable
private fun CreateCatalogItemDialog(
    onConfirm: (CreateCatalogItemBody) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var description: String by remember { mutableStateOf("") }
    var costText: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var costError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.economy_catalog_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.economy_catalog_name),
                    isError = nameError,
                    errorText = stringResource(Res.string.economy_catalog_name_required),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = description,
                    onValueChange = { description = it },
                    label = stringResource(Res.string.economy_catalog_description),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = costText,
                    onValueChange = { costText = it; costError = false },
                    label = stringResource(Res.string.economy_catalog_cost),
                    isError = costError,
                    errorText = stringResource(Res.string.economy_catalog_cost_invalid),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                val trimmedName: String = name.trim()
                val cost: Long? = costText.trim().toLongOrNull()?.takeIf { it > 0 }
                nameError = trimmedName.isEmpty()
                costError = cost == null
                if (!nameError && !costError) {
                    onConfirm(
                        CreateCatalogItemBody(
                            name = trimmedName,
                            description = description.trim().ifEmpty { null },
                            cost = cost!!,
                        )
                    )
                }
            }) {
                Text(stringResource(Res.string.economy_catalog_create))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_catalog_cancel))
            }
        },
    )
}

@Composable
private fun SavingsJarsSection(
    jars: List<SavingsJar>,
    manage: ManageDecision,
    onCreate: (CreateSavingsJarBody) -> Unit,
    loadJarDetail: suspend (jarId: String) -> SavingsJarDetail?,
    onInvite: suspend (jarId: String, InviteChannelBody) -> Unit,
    onAcceptMembership: suspend (membershipId: String) -> Unit,
    onRemoveMembership: suspend (membershipId: String) -> Unit,
    onContribute: suspend (jarId: String, AdminJarContributeBody) -> Unit,
    onWithdraw: suspend (jarId: String, AdminJarWithdrawBody) -> Unit,
    loadHistory: suspend (jarId: String) -> List<JarMovement>?,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var showCreate: Boolean by remember { mutableStateOf(false) }
    var managingJar: SavingsJar? by remember { mutableStateOf(null) }

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = stringResource(Res.string.economy_jars_title),
            style = typography.lg,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { showCreate = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.economy_jars_add),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }

    if (jars.isEmpty()) {
        Text(
            text = stringResource(Res.string.economy_jars_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
    } else {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.fillMaxWidth()) {
                jars.forEachIndexed { index, jar ->
                    SavingsJarRow(
                        jar = jar,
                        manage = manage,
                        onManage = { managingJar = jar },
                    )
                    if (index < jars.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }

    if (showCreate) {
        CreateSavingsJarDialog(
            onConfirm = { request ->
                onCreate(request)
                showCreate = false
            },
            onDismiss = { showCreate = false },
        )
    }

    managingJar?.let { jar ->
        JarManageDialog(
            jar = jar,
            manage = manage,
            loadDetail = loadJarDetail,
            onInvite = { request -> onInvite(jar.id, request) },
            onAcceptMembership = onAcceptMembership,
            onRemoveMembership = onRemoveMembership,
            onContribute = { request -> onContribute(jar.id, request) },
            onWithdraw = { request -> onWithdraw(jar.id, request) },
            loadHistory = loadHistory,
            onDismiss = { managingJar = null },
        )
    }
}

@Composable
private fun SavingsJarRow(
    jar: SavingsJar,
    manage: ManageDecision,
    onManage: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val rowDescription: String =
        stringResource(Res.string.economy_jars_row_description, jar.name, jar.balance)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = jar.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            val goal: Long? = jar.goalAmount
            val balanceText: String =
                if (goal != null) stringResource(Res.string.economy_jars_goal_progress, jar.balance, goal)
                else stringResource(Res.string.economy_jars_balance, jar.balance)
            Text(
                text = balanceText,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        Text(
            text = stringResource(if (jar.isOpen) Res.string.economy_jars_open else Res.string.economy_jars_closed),
            style = typography.sm,
            color = if (jar.isOpen) tokens.primary else tokens.mutedForeground,
            maxLines = 1,
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = onManage, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.economy_jars_manage),
                    style = typography.sm,
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
    }
}

// Full-management dialog for a savings jar: shows detail + memberships, and provides
// Contribute / Withdraw / Invite / History actions.
@Composable
private fun JarManageDialog(
    jar: SavingsJar,
    manage: ManageDecision,
    loadDetail: suspend (jarId: String) -> SavingsJarDetail?,
    onInvite: suspend (InviteChannelBody) -> Unit,
    onAcceptMembership: suspend (membershipId: String) -> Unit,
    onRemoveMembership: suspend (membershipId: String) -> Unit,
    onContribute: suspend (AdminJarContributeBody) -> Unit,
    onWithdraw: suspend (AdminJarWithdrawBody) -> Unit,
    loadHistory: suspend (jarId: String) -> List<JarMovement>?,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var detail: SavingsJarDetail? by remember { mutableStateOf(null) }
    var showInvite: Boolean by remember { mutableStateOf(false) }
    var showContribute: Boolean by remember { mutableStateOf(false) }
    var showWithdraw: Boolean by remember { mutableStateOf(false) }
    var showHistory: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(jar.id) { detail = loadDetail(jar.id) }

    // Run a jar mutation, then reload THIS dialog's detail so the memberships + balance reflect the write
    // immediately — the fire-and-forget page reload never touched the dialog's own fetched detail.
    fun mutateThenReload(action: suspend () -> Unit) {
        scope.launch {
            action()
            detail = loadDetail(jar.id)
        }
    }

    val title: String = stringResource(Res.string.economy_jars_detail_title, jar.name)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                // action buttons
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
                    ManageGate(decision = manage) { enabled ->
                        TextButton(onClick = { showContribute = true }, enabled = enabled) {
                            Text(stringResource(Res.string.economy_jars_contribute), color = if (enabled) tokens.primary else tokens.mutedForeground)
                        }
                    }
                    ManageGate(decision = manage) { enabled ->
                        TextButton(onClick = { showWithdraw = true }, enabled = enabled) {
                            Text(stringResource(Res.string.economy_jars_withdraw), color = if (enabled) tokens.primary else tokens.mutedForeground)
                        }
                    }
                    ManageGate(decision = manage) { enabled ->
                        TextButton(onClick = { showInvite = true }, enabled = enabled) {
                            Text(stringResource(Res.string.economy_jars_invite), color = if (enabled) tokens.primary else tokens.mutedForeground)
                        }
                    }
                    TextButton(onClick = { showHistory = true }) {
                        Text(stringResource(Res.string.economy_jars_history), color = tokens.primary)
                    }
                }

                // memberships
                val memberships: List<SavingsJarMembership> = detail?.memberships ?: emptyList()
                Text(
                    text = stringResource(Res.string.economy_jars_memberships),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                if (memberships.isEmpty()) {
                    Text(
                        text = stringResource(Res.string.economy_jars_memberships_empty),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                } else {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        memberships.forEach { m ->
                            val isPending: Boolean = !m.status.equals("accepted", ignoreCase = true)
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.SpaceBetween,
                            ) {
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(
                                        text = m.memberBroadcasterId,
                                        style = typography.sm,
                                        color = tokens.cardForeground,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                    val statusLabel: String =
                                        if (!isPending) {
                                            stringResource(Res.string.economy_jars_membership_accepted)
                                        } else {
                                            stringResource(Res.string.economy_jars_membership_pending)
                                        }
                                    Text(text = statusLabel, style = typography.xs, color = tokens.mutedForeground)
                                }
                                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
                                    if (isPending) {
                                        ManageGate(decision = manage) { enabled ->
                                            GlyphButton(
                                                imageVector = CheckCircleGlyph,
                                                label = stringResource(Res.string.economy_jars_membership_accept),
                                                onClick = { mutateThenReload { onAcceptMembership(m.id) } },
                                                enabled = enabled,
                                                tint = tokens.primary,
                                            )
                                        }
                                    }
                                    ManageGate(decision = manage) { enabled ->
                                        GlyphButton(
                                            imageVector = TrashGlyph,
                                            label = stringResource(Res.string.economy_jars_membership_revoke),
                                            onClick = { mutateThenReload { onRemoveMembership(m.id) } },
                                            enabled = enabled,
                                            tint = tokens.destructive,
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.economy_jars_close), color = tokens.mutedForeground)
            }
        },
    )

    if (showInvite) {
        JarInviteDialog(
            onConfirm = { request ->
                mutateThenReload { onInvite(request) }
                showInvite = false
            },
            onDismiss = { showInvite = false },
        )
    }
    if (showContribute) {
        JarContributeDialog(
            onConfirm = { request ->
                mutateThenReload { onContribute(request) }
                showContribute = false
            },
            onDismiss = { showContribute = false },
        )
    }
    if (showWithdraw) {
        JarWithdrawDialog(
            onConfirm = { request ->
                mutateThenReload { onWithdraw(request) }
                showWithdraw = false
            },
            onDismiss = { showWithdraw = false },
        )
    }
    if (showHistory) {
        JarHistoryDialog(
            jar = jar,
            loadHistory = loadHistory,
            onDismiss = { showHistory = false },
        )
    }
}

@Composable
private fun JarInviteDialog(onConfirm: (InviteChannelBody) -> Unit, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var broadcasterId: String by remember { mutableStateOf("") }
    var role: String by remember { mutableStateOf("member") }
    val isValid: Boolean = broadcasterId.isNotBlank() && role.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.economy_jars_invite_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                AppTextField(
                    value = broadcasterId,
                    onValueChange = { broadcasterId = it },
                    label = stringResource(Res.string.economy_jars_invite_broadcaster),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = role,
                    onValueChange = { role = it },
                    label = stringResource(Res.string.economy_jars_invite_role),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onConfirm(InviteChannelBody(invitedBroadcasterId = broadcasterId, role = role)) },
                enabled = isValid,
            ) {
                Text(stringResource(Res.string.economy_jars_invite_confirm), color = if (isValid) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_jars_invite_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun JarContributeDialog(onConfirm: (AdminJarContributeBody) -> Unit, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var viewerId: String by remember { mutableStateOf("") }
    var amountText: String by remember { mutableStateOf("") }
    val amount: Long? = amountText.toLongOrNull()?.takeIf { it > 0 }
    val isValid: Boolean = viewerId.isNotBlank() && amount != null

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.economy_jars_contribute_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                AppTextField(value = viewerId, onValueChange = { viewerId = it }, label = stringResource(Res.string.economy_jars_contribute_viewer), modifier = Modifier.fillMaxWidth())
                AppTextField(
                    value = amountText,
                    onValueChange = { amountText = it },
                    label = stringResource(Res.string.economy_jars_contribute_amount),
                    isError = amountText.isNotEmpty() && amount == null,
                    errorText = stringResource(Res.string.economy_jars_contribute_amount_invalid),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { amount?.let { onConfirm(AdminJarContributeBody(contributorUserId = viewerId, amount = it)) } },
                enabled = isValid,
            ) {
                Text(stringResource(Res.string.economy_jars_contribute_confirm), color = if (isValid) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_jars_contribute_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun JarWithdrawDialog(onConfirm: (AdminJarWithdrawBody) -> Unit, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var viewerId: String by remember { mutableStateOf("") }
    var amountText: String by remember { mutableStateOf("") }
    val amount: Long? = amountText.toLongOrNull()?.takeIf { it > 0 }
    val isValid: Boolean = viewerId.isNotBlank() && amount != null

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.economy_jars_withdraw_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                AppTextField(value = viewerId, onValueChange = { viewerId = it }, label = stringResource(Res.string.economy_jars_withdraw_viewer), modifier = Modifier.fillMaxWidth())
                AppTextField(
                    value = amountText,
                    onValueChange = { amountText = it },
                    label = stringResource(Res.string.economy_jars_withdraw_amount),
                    isError = amountText.isNotEmpty() && amount == null,
                    errorText = stringResource(Res.string.economy_jars_withdraw_amount_invalid),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { amount?.let { onConfirm(AdminJarWithdrawBody(targetViewerUserId = viewerId, amount = it)) } },
                enabled = isValid,
            ) {
                Text(stringResource(Res.string.economy_jars_withdraw_confirm), color = if (isValid) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_jars_withdraw_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun JarHistoryDialog(
    jar: SavingsJar,
    loadHistory: suspend (jarId: String) -> List<JarMovement>?,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var entries: List<JarMovement>? by remember { mutableStateOf(null) }
    var error: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(jar.id) {
        val result: List<JarMovement>? = loadHistory(jar.id)
        if (result == null) error = true else entries = result
    }

    val title: String = stringResource(Res.string.economy_jars_history_title, jar.name)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            when {
                error -> Text(stringResource(Res.string.economy_jars_history_error), color = tokens.destructive)
                entries == null -> Text(stringResource(Res.string.economy_loading), color = tokens.mutedForeground)
                entries!!.isEmpty() -> Text(stringResource(Res.string.economy_jars_history_empty), color = tokens.mutedForeground)
                else -> {
                    LazyColumn(modifier = Modifier.heightIn(max = spacing.s16 * 4), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        items(items = entries!!, key = { it.id }) { entry ->
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                            ) {
                                Text(entry.movementType, style = typography.sm, color = tokens.cardForeground, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Text("${if (entry.amount >= 0) "+" else ""}${entry.amount}", style = typography.sm, color = if (entry.amount >= 0) tokens.primary else tokens.destructive)
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_jars_history_close), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun CreateSavingsJarDialog(
    onConfirm: (CreateSavingsJarBody) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var description: String by remember { mutableStateOf("") }
    var goalText: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.economy_jars_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.economy_jars_name),
                    isError = nameError,
                    errorText = stringResource(Res.string.economy_jars_name_required),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = description,
                    onValueChange = { description = it },
                    label = stringResource(Res.string.economy_jars_description),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = goalText,
                    onValueChange = { goalText = it },
                    label = stringResource(Res.string.economy_jars_goal),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                val trimmedName: String = name.trim()
                nameError = trimmedName.isEmpty()
                if (!nameError) {
                    onConfirm(
                        CreateSavingsJarBody(
                            name = trimmedName,
                            description = description.trim().ifEmpty { null },
                            goalAmount = goalText.trim().toLongOrNull()?.takeIf { it > 0 },
                        )
                    )
                }
            }) {
                Text(stringResource(Res.string.economy_jars_create))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.economy_jars_cancel))
            }
        },
    )
}

@Composable
private fun CatalogPurchasesSection(
    purchases: List<CatalogPurchase>,
    manage: ManageDecision,
    onRefund: (purchaseId: Long) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingRefund: CatalogPurchase? by remember { mutableStateOf(null) }

    Text(text = stringResource(Res.string.economy_purchases_title), style = typography.lg, color = tokens.cardForeground)

    if (purchases.isEmpty()) {
        Text(text = stringResource(Res.string.economy_purchases_empty), style = typography.sm, color = tokens.mutedForeground)
    } else {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.fillMaxWidth()) {
                purchases.forEachIndexed { index, purchase ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = spacing.s4, vertical = spacing.s3),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                    ) {
                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                            Text(text = purchase.itemNameSnapshot, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                                Text(text = stringResource(Res.string.economy_purchases_buyer, purchase.buyerUserId), style = typography.xs, color = tokens.mutedForeground)
                                Text(text = stringResource(Res.string.economy_purchases_status, purchase.status), style = typography.xs, color = tokens.mutedForeground)
                            }
                        }
                        Text(text = purchase.costPaid.toString(), style = typography.base, color = tokens.primary)
                        ManageGate(manage) { enabled ->
                            val refundEnabled: Boolean = enabled && purchase.status != "refunded"
                            GlyphButton(
                                imageVector = RemoveGlyph,
                                label = stringResource(Res.string.economy_purchases_refund),
                                onClick = { pendingRefund = purchase },
                                enabled = refundEnabled,
                                tint = tokens.destructive,
                            )
                        }
                    }
                    if (index < purchases.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }

    pendingRefund?.let { p ->
        ConfirmDialog(
            title = stringResource(Res.string.economy_purchases_refund_title),
            message = stringResource(Res.string.economy_purchases_refund_message, p.itemNameSnapshot),
            confirmLabel = stringResource(Res.string.economy_purchases_refund_confirm),
            dismissLabel = stringResource(Res.string.economy_purchases_refund_cancel),
            destructive = false,
            onConfirm = { pendingRefund = null; onRefund(p.id) },
            onDismiss = { pendingRefund = null },
        )
    }
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
