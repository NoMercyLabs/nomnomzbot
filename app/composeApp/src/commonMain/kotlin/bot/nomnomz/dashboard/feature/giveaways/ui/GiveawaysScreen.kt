// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.giveaways.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
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
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CodePool
import bot.nomnomz.dashboard.core.network.Giveaway
import bot.nomnomz.dashboard.core.network.GiveawayCodeStatus
import bot.nomnomz.dashboard.core.network.GiveawayEntryMode
import bot.nomnomz.dashboard.core.network.GiveawayPrizeMode
import bot.nomnomz.dashboard.core.network.GiveawayStatus
import bot.nomnomz.dashboard.core.network.GiveawayWinner
import bot.nomnomz.dashboard.core.network.GiveawayWinnerStatus
import bot.nomnomz.dashboard.core.network.MaskedCode
import bot.nomnomz.dashboard.core.network.UpsertGiveawayBody
import bot.nomnomz.dashboard.feature.giveaways.state.CodePoolsState
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysAccess
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysController
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysState
import bot.nomnomz.dashboard.feature.giveaways.state.PoolDetailState
import bot.nomnomz.dashboard.feature.giveaways.state.WinnersState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.giveaways_action_error
import nomnomzbot.composeapp.generated.resources.giveaways_cancel
import nomnomzbot.composeapp.generated.resources.giveaways_close_action
import nomnomzbot.composeapp.generated.resources.giveaways_close_confirm
import nomnomzbot.composeapp.generated.resources.giveaways_close_message
import nomnomzbot.composeapp.generated.resources.giveaways_close_title
import nomnomzbot.composeapp.generated.resources.giveaways_code_copied
import nomnomzbot.composeapp.generated.resources.giveaways_code_copy
import nomnomzbot.composeapp.generated.resources.giveaways_code_status_assigned
import nomnomzbot.composeapp.generated.resources.giveaways_code_status_available
import nomnomzbot.composeapp.generated.resources.giveaways_code_status_delivered
import nomnomzbot.composeapp.generated.resources.giveaways_code_status_revoked
import nomnomzbot.composeapp.generated.resources.giveaways_code_unlabeled
import nomnomzbot.composeapp.generated.resources.giveaways_delete_action
import nomnomzbot.composeapp.generated.resources.giveaways_delete_confirm
import nomnomzbot.composeapp.generated.resources.giveaways_delete_message
import nomnomzbot.composeapp.generated.resources.giveaways_delete_title
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_claim_window_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_none
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_placeholder
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_create
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_create_title
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_currency_amount_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_entry_cost_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_entry_mode_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_exclude_mods_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_from_pot_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_keyword_help
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_keyword_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_max_entries_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_prize_mode_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_save
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_title_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_winner_count_label
import nomnomzbot.composeapp.generated.resources.giveaways_draw_action
import nomnomzbot.composeapp.generated.resources.giveaways_draw_confirm
import nomnomzbot.composeapp.generated.resources.giveaways_draw_message
import nomnomzbot.composeapp.generated.resources.giveaways_draw_title
import nomnomzbot.composeapp.generated.resources.giveaways_edit_action
import nomnomzbot.composeapp.generated.resources.giveaways_edit_only_draft_closed
import nomnomzbot.composeapp.generated.resources.giveaways_empty
import nomnomzbot.composeapp.generated.resources.giveaways_entries_count
import nomnomzbot.composeapp.generated.resources.giveaways_entry_mode_active
import nomnomzbot.composeapp.generated.resources.giveaways_entry_mode_keyword
import nomnomzbot.composeapp.generated.resources.giveaways_error
import nomnomzbot.composeapp.generated.resources.giveaways_helper
import nomnomzbot.composeapp.generated.resources.giveaways_keyword
import nomnomzbot.composeapp.generated.resources.giveaways_loading
import nomnomzbot.composeapp.generated.resources.giveaways_new_action
import nomnomzbot.composeapp.generated.resources.giveaways_open_action
import nomnomzbot.composeapp.generated.resources.giveaways_pool_add_action
import nomnomzbot.composeapp.generated.resources.giveaways_pool_add_label
import nomnomzbot.composeapp.generated.resources.giveaways_pool_add_placeholder
import nomnomzbot.composeapp.generated.resources.giveaways_pool_codes_label
import nomnomzbot.composeapp.generated.resources.giveaways_pool_counts
import nomnomzbot.composeapp.generated.resources.giveaways_pool_delete_action
import nomnomzbot.composeapp.generated.resources.giveaways_pool_delete_message
import nomnomzbot.composeapp.generated.resources.giveaways_pool_delete_title
import nomnomzbot.composeapp.generated.resources.giveaways_pool_dialog_description_label
import nomnomzbot.composeapp.generated.resources.giveaways_pool_dialog_name_label
import nomnomzbot.composeapp.generated.resources.giveaways_pool_dialog_title
import nomnomzbot.composeapp.generated.resources.giveaways_pool_manage_button
import nomnomzbot.composeapp.generated.resources.giveaways_pool_manage_empty
import nomnomzbot.composeapp.generated.resources.giveaways_pool_manage_error
import nomnomzbot.composeapp.generated.resources.giveaways_pool_manage_loading
import nomnomzbot.composeapp.generated.resources.giveaways_pool_manage_title
import nomnomzbot.composeapp.generated.resources.giveaways_pools_empty
import nomnomzbot.composeapp.generated.resources.giveaways_pools_error
import nomnomzbot.composeapp.generated.resources.giveaways_pools_helper
import nomnomzbot.composeapp.generated.resources.giveaways_pools_loading
import nomnomzbot.composeapp.generated.resources.giveaways_pools_new_action
import nomnomzbot.composeapp.generated.resources.giveaways_pools_requires_codes
import nomnomzbot.composeapp.generated.resources.giveaways_pools_restricted
import nomnomzbot.composeapp.generated.resources.giveaways_pools_title
import nomnomzbot.composeapp.generated.resources.giveaways_prize_announce
import nomnomzbot.composeapp.generated.resources.giveaways_prize_code_pool
import nomnomzbot.composeapp.generated.resources.giveaways_prize_currency
import nomnomzbot.composeapp.generated.resources.giveaways_requires_write
import nomnomzbot.composeapp.generated.resources.giveaways_retry
import nomnomzbot.composeapp.generated.resources.giveaways_status_archived
import nomnomzbot.composeapp.generated.resources.giveaways_status_closed
import nomnomzbot.composeapp.generated.resources.giveaways_status_draft
import nomnomzbot.composeapp.generated.resources.giveaways_status_drawn
import nomnomzbot.composeapp.generated.resources.giveaways_status_open
import nomnomzbot.composeapp.generated.resources.giveaways_winner_code_needs_reveal
import nomnomzbot.composeapp.generated.resources.giveaways_winner_code_whispered
import nomnomzbot.composeapp.generated.resources.giveaways_winner_redraw_badge
import nomnomzbot.composeapp.generated.resources.giveaways_winner_redraw_button
import nomnomzbot.composeapp.generated.resources.giveaways_winner_redraw_confirm
import nomnomzbot.composeapp.generated.resources.giveaways_winner_redraw_message
import nomnomzbot.composeapp.generated.resources.giveaways_winner_redraw_title
import nomnomzbot.composeapp.generated.resources.giveaways_winner_reveal_action
import nomnomzbot.composeapp.generated.resources.giveaways_winner_status_claimed
import nomnomzbot.composeapp.generated.resources.giveaways_winner_status_drawn
import nomnomzbot.composeapp.generated.resources.giveaways_winner_status_forfeited
import nomnomzbot.composeapp.generated.resources.giveaways_winner_status_redrawn
import nomnomzbot.composeapp.generated.resources.giveaways_winners_action
import nomnomzbot.composeapp.generated.resources.giveaways_winners_close
import nomnomzbot.composeapp.generated.resources.giveaways_winners_empty
import nomnomzbot.composeapp.generated.resources.giveaways_winners_error
import nomnomzbot.composeapp.generated.resources.giveaways_winners_loading
import nomnomzbot.composeapp.generated.resources.giveaways_winners_title
import nomnomzbot.composeapp.generated.resources.shell_nav_giveaways
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Giveaways page (giveaways.md §6, Loyalty group): the channel's giveaway campaigns — all real data from
// [GiveawaysController]. The screen is a pure projection of the controller's state; it loads on first
// composition. This is the full management surface: create / edit / delete a campaign, run its open → close →
// draw lifecycle, redraw a winner, view the winner history, and reveal a winner's assigned code. Below it sits
// the Broadcaster-only code-pool section — secret-safe by design, so reads are masked and the section is hidden
// (with a reason) from a caller who can't manage codes.
//
// Two capability gates (frontend-ia.md §7): the campaign controls gate on `giveaways:write` (Moderator floor,
// disable-with-reason via [ManageGate]); the code pools AND the winner code reveal gate on the Broadcaster-only
// `giveaways:codes:write`. The backend re-checks every write regardless — the gate is UX only.
@Composable
fun GiveawaysScreen(controller: GiveawaysController, heldActionKeys: Set<String>) {
    val state: GiveawaysState by controller.state.collectAsStateWithLifecycle()
    val codePools: CodePoolsState by controller.codePools.collectAsStateWithLifecycle()
    val winners: WinnersState by controller.winners.collectAsStateWithLifecycle()
    val poolDetail: PoolDetailState by controller.poolDetail.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val canWrite: Boolean = GiveawaysAccess.canWrite(heldActionKeys)
    val canManageCodes: Boolean = GiveawaysAccess.canManageCodes(heldActionKeys)

    // The write gate for the campaign controls (create / delete / lifecycle) resolves once, unconditionally, so no
    // composable sits behind a branch. Edit carries its own per-row decision (a status guard on top of the key).
    val writeManage: ManageDecision =
        if (canWrite) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.giveaways_requires_write))
    val codesManage: ManageDecision =
        if (canManageCodes) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.giveaways_pools_requires_codes))
    val editReasonWrite: String = stringResource(Res.string.giveaways_requires_write)
    val editReasonStatus: String = stringResource(Res.string.giveaways_edit_only_draft_closed)

    // Dialog / confirmation targets: null = closed. The editor is create (empty) or edit (pre-filled); the delete,
    // lifecycle-confirm, new-pool, and pool-delete targets each drive one dialog.
    var editor: GiveawayEditor? by remember { mutableStateOf(null) }
    var pendingDelete: Giveaway? by remember { mutableStateOf(null) }
    var pendingLifecycle: LifecycleConfirm? by remember { mutableStateOf(null) }
    var newPool: Boolean by remember { mutableStateOf(false) }
    var pendingPoolDelete: CodePool? by remember { mutableStateOf(null) }
    var pendingRedraw: RedrawConfirm? by remember { mutableStateOf(null) }

    // The giveaway-row action callbacks, resolved once. Open fires directly (low-risk start); Close and Draw route
    // to the confirm dialog; Winners opens the controller's winner panel; edit/delete open their dialogs.
    val rowCallbacks =
        GiveawayRowCallbacks(
            onEdit = { editor = GiveawayEditor.edit(it) },
            onDelete = { pendingDelete = it },
            onOpen = { giveaway -> scope.launch { controller.openGiveaway(giveaway.id) } },
            onConfirmLifecycle = { giveaway, kind -> pendingLifecycle = LifecycleConfirm(giveaway, kind) },
            onShowWinners = { giveaway -> scope.launch { controller.showWinners(giveaway) } },
        )

    LaunchedEffect(Unit) {
        controller.load()
        // The code-pool list read is itself Broadcaster-gated — only fetch it for a caller who clears the key,
        // otherwise the section renders its "Broadcaster-only" hint and never touches the endpoint (no phantom 403).
        if (canManageCodes) controller.loadCodePools()
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        Column(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            Header(writeManage = writeManage, onNew = { editor = GiveawayEditor.create() })

            // The content area takes the height left below the pinned header (weight) so its own scroll region is
            // bounded to the viewport — a fillMaxSize child directly in this Column would over-allocate and push
            // the code-pool section off the bottom.
            Box(modifier = Modifier.weight(1f).fillMaxWidth()) {
                when (val current: GiveawaysState = state) {
                    is GiveawaysState.Loading -> CenteredMessage(stringResource(Res.string.giveaways_loading))
                    is GiveawaysState.Error ->
                        ErrorContent(
                            message = stringResource(Res.string.giveaways_error, current.detail),
                            onRetry = { scope.launch { controller.load() } },
                        )
                    is GiveawaysState.Empty ->
                        Body(
                            giveaways = emptyList(),
                            actionError = null,
                            codePools = codePools,
                            canManageCodes = canManageCodes,
                            writeManage = writeManage,
                            codesManage = codesManage,
                            editReasonWrite = editReasonWrite,
                            editReasonStatus = editReasonStatus,
                            onNewPool = { newPool = true },
                            callbacks = rowCallbacks,
                            onManagePoolDetail = { scope.launch { controller.showPoolDetail(it) } },
                            onDeletePool = { pendingPoolDelete = it },
                        )
                    is GiveawaysState.Ready ->
                        Body(
                            giveaways = current.giveaways,
                            actionError = current.actionError,
                            codePools = codePools,
                            canManageCodes = canManageCodes,
                            writeManage = writeManage,
                            codesManage = codesManage,
                            editReasonWrite = editReasonWrite,
                            editReasonStatus = editReasonStatus,
                            onNewPool = { newPool = true },
                            callbacks = rowCallbacks,
                            onManagePoolDetail = { scope.launch { controller.showPoolDetail(it) } },
                            onDeletePool = { pendingPoolDelete = it },
                        )
                }
            }
        }
    }

    // ── Dialogs ────────────────────────────────────────────────────────────────────

    editor?.let { open ->
        GiveawayFormDialog(
            editor = open,
            pools = (codePools as? CodePoolsState.Ready)?.pools ?: emptyList(),
            onDismiss = { editor = null },
            onSubmit = { body ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateGiveaway(open.id, body) else controller.createGiveaway(body)
                }
            },
        )
    }

    pendingDelete?.let { giveaway ->
        ConfirmDialog(
            title = stringResource(Res.string.giveaways_delete_title),
            message = stringResource(Res.string.giveaways_delete_message, giveaway.title),
            confirmLabel = stringResource(Res.string.giveaways_delete_confirm),
            dismissLabel = stringResource(Res.string.giveaways_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteGiveaway(giveaway.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }

    pendingLifecycle?.let { confirm ->
        val isClose: Boolean = confirm.kind == LifecycleKind.Close
        ConfirmDialog(
            title = stringResource(if (isClose) Res.string.giveaways_close_title else Res.string.giveaways_draw_title),
            message =
                stringResource(
                    if (isClose) Res.string.giveaways_close_message else Res.string.giveaways_draw_message,
                    confirm.giveaway.title,
                ),
            confirmLabel =
                stringResource(if (isClose) Res.string.giveaways_close_confirm else Res.string.giveaways_draw_confirm),
            dismissLabel = stringResource(Res.string.giveaways_cancel),
            destructive = true,
            onConfirm = {
                val target: LifecycleConfirm = confirm
                pendingLifecycle = null
                scope.launch {
                    if (isClose) controller.closeGiveaway(target.giveaway.id)
                    else controller.drawGiveaway(target.giveaway)
                }
            },
            onDismiss = { pendingLifecycle = null },
        )
    }

    if (newPool) {
        NewCodePoolDialog(
            onDismiss = { newPool = false },
            onSubmit = { name, description ->
                newPool = false
                scope.launch { controller.createCodePool(name, description) }
            },
        )
    }

    pendingPoolDelete?.let { pool ->
        ConfirmDialog(
            title = stringResource(Res.string.giveaways_pool_delete_title),
            message = stringResource(Res.string.giveaways_pool_delete_message, pool.name),
            confirmLabel = stringResource(Res.string.giveaways_delete_confirm),
            dismissLabel = stringResource(Res.string.giveaways_cancel),
            destructive = true,
            onConfirm = {
                pendingPoolDelete = null
                scope.launch { controller.deleteCodePool(pool.id) }
            },
            onDismiss = { pendingPoolDelete = null },
        )
    }

    pendingRedraw?.let { confirm ->
        ConfirmDialog(
            title = stringResource(Res.string.giveaways_winner_redraw_title),
            message = stringResource(Res.string.giveaways_winner_redraw_message, confirm.winner.viewerDisplayName),
            confirmLabel = stringResource(Res.string.giveaways_winner_redraw_confirm),
            dismissLabel = stringResource(Res.string.giveaways_cancel),
            destructive = true,
            onConfirm = {
                val target: RedrawConfirm = confirm
                pendingRedraw = null
                scope.launch { controller.redrawWinner(target.giveaway, target.winner.id) }
            },
            onDismiss = { pendingRedraw = null },
        )
    }

    // The winner panel and the manage-pool panel are driven straight off the controller's state (opened by a row
    // action or by a completed draw), so they render whenever that state is not Hidden.
    if (winners !is WinnersState.Hidden) {
        WinnersDialog(
            state = winners,
            canWrite = canWrite,
            canManageCodes = canManageCodes,
            onDismiss = { controller.hideWinners() },
            onRedraw = { giveaway, winner -> pendingRedraw = RedrawConfirm(giveaway, winner) },
            onReveal = { giveaway, winnerId -> scope.launch { controller.revealCode(giveaway, winnerId) } },
        )
    }

    if (poolDetail !is PoolDetailState.Hidden) {
        ManagePoolDialog(
            state = poolDetail,
            onDismiss = { controller.hidePoolDetail() },
            onAddCodes = { poolId, codes -> scope.launch { controller.addCodes(poolId, codes) } },
        )
    }
}

@Composable
private fun Header(writeManage: ManageDecision, onNew: () -> Unit) {
    val newLabel: String = stringResource(Res.string.giveaways_new_action)

    PageHeader(title = stringResource(Res.string.shell_nav_giveaways)) {
        ManageGate(decision = writeManage) { enabled ->
            GlyphButton(imageVector = AddGlyph, label = newLabel, onClick = onNew, enabled = enabled)
        }
    }
}

// The scrollable body: the helper line, an optional write-failure banner, the campaigns card, then the
// Broadcaster-only code-pool section. Shared by the Ready and Empty states so a fresh channel can still create
// its first giveaway (and pool) from the same surface.
@Composable
private fun Body(
    giveaways: List<Giveaway>,
    actionError: String?,
    codePools: CodePoolsState,
    canManageCodes: Boolean,
    writeManage: ManageDecision,
    codesManage: ManageDecision,
    editReasonWrite: String,
    editReasonStatus: String,
    onNewPool: () -> Unit,
    callbacks: GiveawayRowCallbacks,
    onManagePoolDetail: (CodePool) -> Unit,
    onDeletePool: (CodePool) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.giveaways_helper),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.giveaways_action_error, it)) }

        Card(modifier = Modifier.fillMaxWidth()) {
            if (giveaways.isEmpty()) {
                Box(
                    modifier = Modifier.fillMaxWidth().padding(spacing.s6),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = stringResource(Res.string.giveaways_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                Column(modifier = Modifier.fillMaxWidth()) {
                    giveaways.forEachIndexed { index, giveaway ->
                        GiveawayRow(
                            giveaway = giveaway,
                            writeManage = writeManage,
                            editReasonWrite = editReasonWrite,
                            editReasonStatus = editReasonStatus,
                            callbacks = callbacks,
                        )
                        if (index < giveaways.lastIndex) Separator()
                    }
                }
            }
        }

        CodePoolsSection(
            state = codePools,
            canManageCodes = canManageCodes,
            codesManage = codesManage,
            onNewPool = onNewPool,
            onManage = onManagePoolDetail,
            onDelete = onDeletePool,
        )
    }
}

@Composable
private fun GiveawayRow(
    giveaway: Giveaway,
    writeManage: ManageDecision,
    editReasonWrite: String,
    editReasonStatus: String,
    callbacks: GiveawayRowCallbacks,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val editable: Boolean = giveaway.status == GiveawayStatus.Draft || giveaway.status == GiveawayStatus.Closed
    val editManage: ManageDecision =
        when {
            !writeManage.isAllowed -> ManageDecision.Denied(editReasonWrite)
            !editable -> ManageDecision.Denied(editReasonStatus)
            else -> ManageDecision.Allowed
        }
    val editLabel: String = stringResource(Res.string.giveaways_edit_action, giveaway.title)
    val deleteLabel: String = stringResource(Res.string.giveaways_delete_action, giveaway.title)

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = giveaway.title,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            StatusBadge(status = giveaway.status)
        }

        Text(
            text = metaLine(giveaway),
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )

        FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            LifecycleButton(giveaway = giveaway, writeManage = writeManage, callbacks = callbacks)
            if (giveaway.drawnAt != null || giveaway.status == GiveawayStatus.Drawn) {
                Button(
                    onClick = { callbacks.onShowWinners(giveaway) },
                    variant = ButtonVariant.Outline,
                    size = ButtonSize.Sm,
                ) {
                    Text(text = stringResource(Res.string.giveaways_winners_action))
                }
            }
            ManageGate(decision = editManage) { enabled ->
                GlyphButton(
                    imageVector = EditGlyph,
                    label = editLabel,
                    onClick = { callbacks.onEdit(giveaway) },
                    enabled = enabled,
                )
            }
            ManageGate(decision = writeManage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = deleteLabel,
                    onClick = { callbacks.onDelete(giveaway) },
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

// The one contextual lifecycle action for the row's current status: open a draft, close an open one, draw a
// closed one. Drawn / archived giveaways show no lifecycle button (their next action is Winners). Close and Draw
// route through a confirm; Open is the low-risk start action and fires directly.
@Composable
private fun LifecycleButton(
    giveaway: Giveaway,
    writeManage: ManageDecision,
    callbacks: GiveawayRowCallbacks,
) {
    val label: String
    val kind: LifecycleKind?
    val direct: Boolean
    when (giveaway.status) {
        GiveawayStatus.Draft -> {
            label = stringResource(Res.string.giveaways_open_action)
            kind = LifecycleKind.Open
            direct = true
        }
        GiveawayStatus.Open -> {
            label = stringResource(Res.string.giveaways_close_action)
            kind = LifecycleKind.Close
            direct = false
        }
        GiveawayStatus.Closed -> {
            label = stringResource(Res.string.giveaways_draw_action)
            kind = LifecycleKind.Draw
            direct = false
        }
        else -> {
            label = ""
            kind = null
            direct = false
        }
    }
    if (kind == null) return

    ManageGate(decision = writeManage) { enabled ->
        Button(
            onClick = {
                if (direct) callbacks.onOpen(giveaway) else callbacks.onConfirmLifecycle(giveaway, kind)
            },
            variant = if (kind == LifecycleKind.Draw) ButtonVariant.Default else ButtonVariant.Outline,
            size = ButtonSize.Sm,
            enabled = enabled,
        ) {
            Text(text = label)
        }
    }
}

@Composable
private fun CodePoolsSection(
    state: CodePoolsState,
    canManageCodes: Boolean,
    codesManage: ManageDecision,
    onNewPool: () -> Unit,
    onManage: (CodePool) -> Unit,
    onDelete: (CodePool) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val newLabel: String = stringResource(Res.string.giveaways_pools_new_action)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.giveaways_pools_title),
                style = typography.lg,
                color = tokens.foreground,
                modifier = Modifier.weight(1f),
            )
            ManageGate(decision = codesManage) { enabled ->
                GlyphButton(imageVector = AddGlyph, label = newLabel, onClick = onNewPool, enabled = enabled)
            }
        }
        Text(
            text = stringResource(Res.string.giveaways_pools_helper),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Card(modifier = Modifier.fillMaxWidth()) {
            when {
                // The read is Broadcaster-gated; a caller who can't manage codes never fetched the list, so show
                // the honest "Broadcaster-only" hint rather than a phantom empty state.
                !canManageCodes -> PoolPlaceholder(stringResource(Res.string.giveaways_pools_restricted))
                state is CodePoolsState.Loading -> PoolPlaceholder(stringResource(Res.string.giveaways_pools_loading))
                state is CodePoolsState.Error ->
                    PoolPlaceholder(stringResource(Res.string.giveaways_pools_error, state.detail))
                state is CodePoolsState.Empty -> PoolPlaceholder(stringResource(Res.string.giveaways_pools_empty))
                state is CodePoolsState.Ready -> {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        state.actionError?.let {
                            ActionErrorBanner(message = stringResource(Res.string.giveaways_action_error, it))
                        }
                        state.pools.forEachIndexed { index, pool ->
                            CodePoolRow(pool = pool, onManage = { onManage(pool) }, onDelete = { onDelete(pool) })
                            if (index < state.pools.lastIndex) Separator()
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun CodePoolRow(pool: CodePool, onManage: () -> Unit, onDelete: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val counts: String =
        stringResource(Res.string.giveaways_pool_counts, pool.total, pool.available, pool.assigned)
    val description: String? = pool.description?.takeIf { it.isNotBlank() }
    val deleteLabel: String = stringResource(Res.string.giveaways_pool_delete_action, pool.name)

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = pool.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(text = counts, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
            description?.let {
                Text(
                    text = it,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        Button(onClick = onManage, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.giveaways_pool_manage_button))
        }
        GlyphButton(
            imageVector = TrashGlyph,
            label = deleteLabel,
            onClick = onDelete,
            tint = tokens.destructive,
        )
    }
}

@Composable
private fun PoolPlaceholder(text: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth().padding(spacing.s6), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
    }
}

// ── Status / meta helpers ────────────────────────────────────────────────────────

@Composable
private fun StatusBadge(status: String) {
    val variant: BadgeVariant =
        when (status) {
            GiveawayStatus.Open -> BadgeVariant.Default
            GiveawayStatus.Drawn -> BadgeVariant.Secondary
            GiveawayStatus.Archived -> BadgeVariant.Destructive
            else -> BadgeVariant.Outline
        }
    Badge(variant = variant) { Text(text = statusLabel(status)) }
}

@Composable
private fun statusLabel(status: String): String =
    stringResource(
        when (status) {
            GiveawayStatus.Draft -> Res.string.giveaways_status_draft
            GiveawayStatus.Open -> Res.string.giveaways_status_open
            GiveawayStatus.Closed -> Res.string.giveaways_status_closed
            GiveawayStatus.Drawn -> Res.string.giveaways_status_drawn
            else -> Res.string.giveaways_status_archived
        }
    )

// The muted meta line: how viewers enter, the live entry count, and (keyword mode) the keyword itself.
@Composable
private fun metaLine(giveaway: Giveaway): String {
    val mode: String =
        stringResource(
            if (giveaway.entryMode == GiveawayEntryMode.ActiveViewers) Res.string.giveaways_entry_mode_active
            else Res.string.giveaways_entry_mode_keyword
        )
    val entries: String = stringResource(Res.string.giveaways_entries_count, giveaway.entryCount)
    val keyword: String? =
        giveaway.keyword
            ?.takeIf { it.isNotBlank() && giveaway.entryMode == GiveawayEntryMode.Keyword }
            ?.let { stringResource(Res.string.giveaways_keyword, it) }
    return listOfNotNull(mode, entries, keyword).joinToString(" · ")
}

@Composable
private fun ErrorContent(message: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(text = message, style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.giveaways_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth().padding(top = LocalSpacing.current.s6), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

// ── Row callback plumbing ────────────────────────────────────────────────────────

// One bundle of the giveaway-row callbacks so the row signature stays small. The lifecycle callbacks split into a
// direct [onOpen] (no confirm) and [onConfirmLifecycle] (close/draw → confirm dialog).
private class GiveawayRowCallbacks(
    val onEdit: (Giveaway) -> Unit,
    val onDelete: (Giveaway) -> Unit,
    val onOpen: (Giveaway) -> Unit,
    val onConfirmLifecycle: (Giveaway, LifecycleKind) -> Unit,
    val onShowWinners: (Giveaway) -> Unit,
)

private enum class LifecycleKind { Open, Close, Draw }

private data class LifecycleConfirm(val giveaway: Giveaway, val kind: LifecycleKind)

private data class RedrawConfirm(val giveaway: Giveaway, val winner: GiveawayWinner)

// ── Create / edit dialog ─────────────────────────────────────────────────────────

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until the title is non-blank (and, in keyword mode, the keyword). The prize
// picker offers the three self-contained modes (announce / currency / code pool); a giveaway created elsewhere in
// `pipeline` mode keeps that mode + its pipeline reference untouched (they pass through the seed) unless the
// operator picks a different prize here.
@Composable
private fun GiveawayFormDialog(
    editor: GiveawayEditor,
    pools: List<CodePool>,
    onDismiss: () -> Unit,
    onSubmit: (UpsertGiveawayBody) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var title: String by remember { mutableStateOf(editor.title) }
    var entryMode: String by remember { mutableStateOf(editor.entryMode) }
    var keyword: String by remember { mutableStateOf(editor.keyword) }
    var winnerCount: String by remember { mutableStateOf(editor.winnerCount) }
    var maxEntries: String by remember { mutableStateOf(editor.maxEntriesPerUser) }
    var entryCost: String by remember { mutableStateOf(editor.entryCost) }
    var excludeMods: Boolean by remember { mutableStateOf(editor.excludeModerators) }
    var claimWindow: String by remember { mutableStateOf(editor.claimWindowMinutes) }
    var prizeMode: String by remember { mutableStateOf(editor.prizeMode) }
    var currencyAmount: String by remember { mutableStateOf(editor.prizeCurrencyAmount) }
    var fromPot: Boolean by remember { mutableStateOf(editor.prizeFromPot) }
    var codePoolId: String? by remember { mutableStateOf(editor.prizeCodePoolId) }

    val keywordMode: Boolean = entryMode == GiveawayEntryMode.Keyword
    val canSubmit: Boolean = title.isNotBlank() && (!keywordMode || keyword.isNotBlank())
    val dialogTitle: String =
        stringResource(if (editor.isEdit) Res.string.giveaways_dialog_edit_title else Res.string.giveaways_dialog_create_title)
    val submitLabel: String =
        stringResource(if (editor.isEdit) Res.string.giveaways_dialog_save else Res.string.giveaways_dialog_create)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = dialogTitle) },
        text = {
            Column(
                modifier = Modifier.heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = title,
                    onValueChange = { title = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.giveaways_dialog_title_label),
                )

                // Entry mode — a two-option segmented picker.
                FieldLabel(stringResource(Res.string.giveaways_dialog_entry_mode_label))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    SelectChip(
                        label = stringResource(Res.string.giveaways_entry_mode_keyword),
                        selected = entryMode == GiveawayEntryMode.Keyword,
                        onClick = { entryMode = GiveawayEntryMode.Keyword },
                    )
                    SelectChip(
                        label = stringResource(Res.string.giveaways_entry_mode_active),
                        selected = entryMode == GiveawayEntryMode.ActiveViewers,
                        onClick = { entryMode = GiveawayEntryMode.ActiveViewers },
                    )
                }
                if (keywordMode) {
                    AppTextField(
                        value = keyword,
                        onValueChange = { keyword = it },
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.giveaways_dialog_keyword_label),
                    )
                    Text(
                        text = stringResource(Res.string.giveaways_dialog_keyword_help),
                        style = LocalTypography.current.xs,
                        color = tokens.mutedForeground,
                    )
                }

                NumberField(
                    value = winnerCount,
                    onValueChange = { winnerCount = it },
                    label = stringResource(Res.string.giveaways_dialog_winner_count_label),
                )
                NumberField(
                    value = maxEntries,
                    onValueChange = { maxEntries = it },
                    label = stringResource(Res.string.giveaways_dialog_max_entries_label),
                )
                NumberField(
                    value = entryCost,
                    onValueChange = { entryCost = it },
                    label = stringResource(Res.string.giveaways_dialog_entry_cost_label),
                )
                NumberField(
                    value = claimWindow,
                    onValueChange = { claimWindow = it },
                    label = stringResource(Res.string.giveaways_dialog_claim_window_label),
                )
                ToggleRow(
                    label = stringResource(Res.string.giveaways_dialog_exclude_mods_label),
                    checked = excludeMods,
                    onCheckedChange = { excludeMods = it },
                )

                // Prize mode — a three-option segmented picker, with the mode-specific config below it.
                FieldLabel(stringResource(Res.string.giveaways_dialog_prize_mode_label))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    SelectChip(
                        label = stringResource(Res.string.giveaways_prize_announce),
                        selected = prizeMode == GiveawayPrizeMode.Announce,
                        onClick = { prizeMode = GiveawayPrizeMode.Announce },
                    )
                    SelectChip(
                        label = stringResource(Res.string.giveaways_prize_currency),
                        selected = prizeMode == GiveawayPrizeMode.Currency,
                        onClick = { prizeMode = GiveawayPrizeMode.Currency },
                    )
                    SelectChip(
                        label = stringResource(Res.string.giveaways_prize_code_pool),
                        selected = prizeMode == GiveawayPrizeMode.CodePool,
                        onClick = { prizeMode = GiveawayPrizeMode.CodePool },
                    )
                }
                if (prizeMode == GiveawayPrizeMode.Currency) {
                    NumberField(
                        value = currencyAmount,
                        onValueChange = { currencyAmount = it },
                        label = stringResource(Res.string.giveaways_dialog_currency_amount_label),
                    )
                    ToggleRow(
                        label = stringResource(Res.string.giveaways_dialog_from_pot_label),
                        checked = fromPot,
                        onCheckedChange = { fromPot = it },
                    )
                }
                if (prizeMode == GiveawayPrizeMode.CodePool) {
                    CodePoolPicker(
                        pools = pools,
                        selectedId = codePoolId,
                        onSelect = { codePoolId = it },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSubmit(
                        editor.toBody(
                            title = title,
                            entryMode = entryMode,
                            keyword = keyword,
                            winnerCount = winnerCount,
                            maxEntries = maxEntries,
                            entryCost = entryCost,
                            excludeMods = excludeMods,
                            claimWindow = claimWindow,
                            prizeMode = prizeMode,
                            currencyAmount = currencyAmount,
                            fromPot = fromPot,
                            codePoolId = codePoolId,
                        )
                    )
                },
                enabled = canSubmit,
            ) {
                Text(text = submitLabel, color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.giveaways_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// A digit-only text field for a numeric config value (winner count, entry cost, …). Non-digits are dropped as
// they are typed, so the value is always parseable; a blank field means "unset" (the controller maps it to the
// default or null on submit).
@Composable
private fun NumberField(value: String, onValueChange: (String) -> Unit, label: String) {
    AppTextField(
        value = value,
        onValueChange = { input -> onValueChange(input.filter(Char::isDigit)) },
        modifier = Modifier.fillMaxWidth(),
        label = label,
    )
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(text = label, style = typography.sm, color = tokens.foreground, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}

@Composable
private fun FieldLabel(text: String) {
    Text(text = text, style = LocalTypography.current.sm, color = LocalTokens.current.foreground)
}

// A selectable chip (shadcn Badge in its selectable/toggle form) — one option in a segmented picker.
@Composable
private fun SelectChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Badge(selected = selected, onClick = onClick) { Text(text = label) }
}

// The code-pool picker for a code-prize giveaway: the channel's pools as selectable chips (a code pool has no
// plaintext exposure here — just its name). Empty when the caller has no pools (or can't read them), with a hint
// pointing at the code-pool section below.
@Composable
private fun CodePoolPicker(pools: List<CodePool>, selectedId: String?, onSelect: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    FieldLabel(stringResource(Res.string.giveaways_dialog_code_pool_label))
    if (pools.isEmpty()) {
        Text(
            text = stringResource(Res.string.giveaways_dialog_code_pool_none),
            style = LocalTypography.current.xs,
            color = tokens.mutedForeground,
        )
        return
    }
    if (selectedId == null) {
        Text(
            text = stringResource(Res.string.giveaways_dialog_code_pool_placeholder),
            style = LocalTypography.current.xs,
            color = tokens.mutedForeground,
        )
    }
    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        pools.forEach { pool ->
            SelectChip(label = pool.name, selected = pool.id == selectedId, onClick = { onSelect(pool.id) })
        }
    }
}

// ── Winner panel ─────────────────────────────────────────────────────────────────

@Composable
private fun WinnersDialog(
    state: WinnersState,
    canWrite: Boolean,
    canManageCodes: Boolean,
    onDismiss: () -> Unit,
    onRedraw: (Giveaway, GiveawayWinner) -> Unit,
    onReveal: (Giveaway, String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val giveaway: Giveaway? =
        when (state) {
            is WinnersState.Loading -> state.giveaway
            is WinnersState.Ready -> state.giveaway
            is WinnersState.Error -> state.giveaway
            WinnersState.Hidden -> null
        }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.giveaways_winners_title, giveaway?.title ?: "")) },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                when (state) {
                    is WinnersState.Loading ->
                        Text(
                            text = stringResource(Res.string.giveaways_winners_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is WinnersState.Error ->
                        Text(
                            text = stringResource(Res.string.giveaways_winners_error, state.detail),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is WinnersState.Ready -> {
                        state.actionError?.let {
                            ActionErrorBanner(message = stringResource(Res.string.giveaways_action_error, it))
                        }
                        if (state.winners.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.giveaways_winners_empty),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            state.winners.forEach { winner ->
                                WinnerRow(
                                    giveaway = state.giveaway,
                                    winner = winner,
                                    revealedCode = state.revealedCodes[winner.id],
                                    canWrite = canWrite,
                                    canManageCodes = canManageCodes,
                                    onRedraw = { onRedraw(state.giveaway, winner) },
                                    onReveal = { onReveal(state.giveaway, winner.id) },
                                )
                                Separator()
                            }
                        }
                    }
                    WinnersState.Hidden -> Unit
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.giveaways_winners_close), color = tokens.primary)
            }
        },
    )
}

@Composable
private fun WinnerRow(
    giveaway: Giveaway,
    winner: GiveawayWinner,
    revealedCode: String?,
    canWrite: Boolean,
    canManageCodes: Boolean,
    onRedraw: () -> Unit,
    onReveal: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val redrawable: Boolean = winner.status != GiveawayWinnerStatus.Redrawn
    // A code-prize winner shows its delivery state: whispered (delivered), or "whisper failed — reveal" (the
    // broadcaster reveal path). Non-code prizes carry no code, so no delivery line.
    val needsReveal: Boolean = winner.assignedCodeId != null && winner.whisperDelivered == false
    val whispered: Boolean = winner.assignedCodeId != null && winner.whisperDelivered == true

    Column(modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(
                text = winner.viewerDisplayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (winner.isRedraw) {
                Badge(variant = BadgeVariant.Secondary) { Text(text = stringResource(Res.string.giveaways_winner_redraw_badge)) }
            }
            Badge(variant = winnerStatusVariant(winner.status)) { Text(text = winnerStatusLabel(winner.status)) }
        }

        if (whispered) {
            Text(text = stringResource(Res.string.giveaways_winner_code_whispered), style = typography.xs, color = tokens.mutedForeground)
        }
        if (needsReveal) {
            Text(text = stringResource(Res.string.giveaways_winner_code_needs_reveal), style = typography.xs, color = tokens.destructive)
        }

        // The revealed plaintext, shown once on demand with a copy control (the single decrypt path). Only ever
        // reachable by a broadcaster (canManageCodes), and only when a code was assigned.
        if (revealedCode != null) {
            CopyValue(
                value = revealedCode,
                copyLabel = stringResource(Res.string.giveaways_code_copy),
                copiedLabel = stringResource(Res.string.giveaways_code_copied),
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            if (canManageCodes && winner.assignedCodeId != null && revealedCode == null) {
                Button(onClick = onReveal, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
                    Text(text = stringResource(Res.string.giveaways_winner_reveal_action))
                }
            }
            if (canWrite && redrawable) {
                Button(onClick = onRedraw, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
                    Text(text = stringResource(Res.string.giveaways_winner_redraw_button))
                }
            }
        }
    }
}

@Composable
private fun winnerStatusLabel(status: String): String =
    stringResource(
        when (status) {
            GiveawayWinnerStatus.Claimed -> Res.string.giveaways_winner_status_claimed
            GiveawayWinnerStatus.Forfeited -> Res.string.giveaways_winner_status_forfeited
            GiveawayWinnerStatus.Redrawn -> Res.string.giveaways_winner_status_redrawn
            else -> Res.string.giveaways_winner_status_drawn
        }
    )

private fun winnerStatusVariant(status: String): BadgeVariant =
    when (status) {
        GiveawayWinnerStatus.Claimed -> BadgeVariant.Default
        GiveawayWinnerStatus.Forfeited -> BadgeVariant.Destructive
        GiveawayWinnerStatus.Redrawn -> BadgeVariant.Outline
        else -> BadgeVariant.Secondary
    }

// ── Code-pool dialogs ────────────────────────────────────────────────────────────

@Composable
private fun NewCodePoolDialog(onDismiss: () -> Unit, onSubmit: (name: String, description: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var name: String by remember { mutableStateOf("") }
    var description: String by remember { mutableStateOf("") }
    val canSubmit: Boolean = name.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.giveaways_pool_dialog_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.giveaways_pool_dialog_name_label),
                )
                AppTextField(
                    value = description,
                    onValueChange = { description = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.giveaways_pool_dialog_description_label),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(name, description) }, enabled = canSubmit) {
                Text(
                    text = stringResource(Res.string.giveaways_dialog_create),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.giveaways_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// The manage-pool panel: the pool's MASKED code rows (label + status, never plaintext — D6) plus a bulk add-codes
// field (one code per line). Adding reloads both the masked list and the pool counts.
@Composable
private fun ManagePoolDialog(
    state: PoolDetailState,
    onDismiss: () -> Unit,
    onAddCodes: (poolId: String, codes: List<String>) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var draft: String by remember { mutableStateOf("") }
    val poolName: String =
        when (state) {
            is PoolDetailState.Loading -> state.name
            is PoolDetailState.Ready -> state.pool.name
            is PoolDetailState.Error -> state.name
            PoolDetailState.Hidden -> ""
        }
    val poolId: String? = (state as? PoolDetailState.Ready)?.pool?.id
    val canAdd: Boolean = poolId != null && draft.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.giveaways_pool_manage_title, poolName)) },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                when (state) {
                    is PoolDetailState.Loading ->
                        Text(
                            text = stringResource(Res.string.giveaways_pool_manage_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is PoolDetailState.Error ->
                        Text(
                            text = stringResource(Res.string.giveaways_pool_manage_error, state.detail),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is PoolDetailState.Ready -> {
                        state.actionError?.let {
                            ActionErrorBanner(message = stringResource(Res.string.giveaways_action_error, it))
                        }
                        FieldLabel(stringResource(Res.string.giveaways_pool_codes_label))
                        if (state.pool.codes.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.giveaways_pool_manage_empty),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            state.pool.codes.forEach { code -> MaskedCodeRow(code) }
                        }
                    }
                    PoolDetailState.Hidden -> Unit
                }

                Separator()
                Textarea(
                    value = draft,
                    onValueChange = { draft = it },
                    label = stringResource(Res.string.giveaways_pool_add_label),
                    modifier = Modifier.fillMaxWidth(),
                    placeholder = stringResource(Res.string.giveaways_pool_add_placeholder),
                    minLines = 3,
                    monospace = true,
                )
                Button(
                    onClick = {
                        val codes: List<String> = draft.split('\n')
                        if (poolId != null) {
                            onAddCodes(poolId, codes)
                            draft = ""
                        }
                    },
                    variant = ButtonVariant.Default,
                    size = ButtonSize.Sm,
                    enabled = canAdd,
                ) {
                    Text(text = stringResource(Res.string.giveaways_pool_add_action))
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.giveaways_winners_close), color = tokens.primary)
            }
        },
    )
}

@Composable
private fun MaskedCodeRow(code: MaskedCode) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = code.label?.takeIf { it.isNotBlank() } ?: stringResource(Res.string.giveaways_code_unlabeled),
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Badge(variant = codeStatusVariant(code.status)) { Text(text = codeStatusLabel(code.status)) }
    }
}

@Composable
private fun codeStatusLabel(status: String): String =
    stringResource(
        when (status) {
            GiveawayCodeStatus.Assigned -> Res.string.giveaways_code_status_assigned
            GiveawayCodeStatus.Delivered -> Res.string.giveaways_code_status_delivered
            GiveawayCodeStatus.Revoked -> Res.string.giveaways_code_status_revoked
            else -> Res.string.giveaways_code_status_available
        }
    )

private fun codeStatusVariant(status: String): BadgeVariant =
    when (status) {
        GiveawayCodeStatus.Delivered -> BadgeVariant.Default
        GiveawayCodeStatus.Assigned -> BadgeVariant.Secondary
        GiveawayCodeStatus.Revoked -> BadgeVariant.Destructive
        else -> BadgeVariant.Outline
    }

// ── Editor seed ──────────────────────────────────────────────────────────────────

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a giveaway opens a
// pre-filled edit form. Numeric fields are held as text (validated to digits in the field). [eligibilityJson] /
// [weightingJson] / [prizePipelineId] are NOT surfaced by the form — they are carried here so an edit sends them
// back UNCHANGED (a full-body PUT would otherwise wipe them to null).
private data class GiveawayEditor(
    val isEdit: Boolean,
    val id: String,
    val title: String,
    val entryMode: String,
    val keyword: String,
    val winnerCount: String,
    val maxEntriesPerUser: String,
    val entryCost: String,
    val excludeModerators: Boolean,
    val claimWindowMinutes: String,
    val prizeMode: String,
    val prizeCurrencyAmount: String,
    val prizeFromPot: Boolean,
    val prizeCodePoolId: String?,
    val eligibilityJson: String?,
    val weightingJson: String?,
    val prizePipelineId: String?,
) {
    // Build the wire body from the dialog's current inputs plus the passed-through (unedited) fields. Keyword is
    // sent only in keyword mode; the currency / code-pool references only in their prize mode — so switching mode
    // never leaves a stale reference behind.
    fun toBody(
        title: String,
        entryMode: String,
        keyword: String,
        winnerCount: String,
        maxEntries: String,
        entryCost: String,
        excludeMods: Boolean,
        claimWindow: String,
        prizeMode: String,
        currencyAmount: String,
        fromPot: Boolean,
        codePoolId: String?,
    ): UpsertGiveawayBody =
        UpsertGiveawayBody(
            title = title.trim(),
            entryMode = entryMode,
            keyword = if (entryMode == GiveawayEntryMode.Keyword) keyword.trim().takeIf { it.isNotBlank() } else null,
            entryCost = entryCost.toLongOrNull(),
            maxEntriesPerUser = maxEntries.toIntOrNull() ?: 1,
            eligibilityJson = eligibilityJson,
            weightingJson = weightingJson,
            winnerCount = winnerCount.toIntOrNull() ?: 1,
            excludeModerators = excludeMods,
            claimWindowMinutes = claimWindow.toIntOrNull(),
            prizeMode = prizeMode,
            prizeCurrencyAmount = if (prizeMode == GiveawayPrizeMode.Currency) currencyAmount.toLongOrNull() else null,
            prizeFromPot = prizeMode == GiveawayPrizeMode.Currency && fromPot,
            prizePipelineId = prizePipelineId,
            prizeCodePoolId = if (prizeMode == GiveawayPrizeMode.CodePool) codePoolId else null,
        )

    companion object {
        fun create(): GiveawayEditor =
            GiveawayEditor(
                isEdit = false,
                id = "",
                title = "",
                entryMode = GiveawayEntryMode.Keyword,
                keyword = "",
                winnerCount = "1",
                maxEntriesPerUser = "1",
                entryCost = "",
                excludeModerators = false,
                claimWindowMinutes = "",
                prizeMode = GiveawayPrizeMode.Announce,
                prizeCurrencyAmount = "",
                prizeFromPot = false,
                prizeCodePoolId = null,
                eligibilityJson = null,
                weightingJson = null,
                prizePipelineId = null,
            )

        fun edit(giveaway: Giveaway): GiveawayEditor =
            GiveawayEditor(
                isEdit = true,
                id = giveaway.id,
                title = giveaway.title,
                entryMode = giveaway.entryMode.ifBlank { GiveawayEntryMode.Keyword },
                keyword = giveaway.keyword.orEmpty(),
                winnerCount = giveaway.winnerCount.toString(),
                maxEntriesPerUser = giveaway.maxEntriesPerUser.toString(),
                entryCost = giveaway.entryCost?.toString().orEmpty(),
                excludeModerators = giveaway.excludeModerators,
                claimWindowMinutes = giveaway.claimWindowMinutes?.toString().orEmpty(),
                prizeMode = giveaway.prizeMode.ifBlank { GiveawayPrizeMode.Announce },
                prizeCurrencyAmount = giveaway.prizeCurrencyAmount?.toString().orEmpty(),
                prizeFromPot = giveaway.prizeFromPot,
                prizeCodePoolId = giveaway.prizeCodePoolId,
                eligibilityJson = giveaway.eligibilityJson,
                weightingJson = giveaway.weightingJson,
                prizePipelineId = giveaway.prizePipelineId,
            )
    }
}
