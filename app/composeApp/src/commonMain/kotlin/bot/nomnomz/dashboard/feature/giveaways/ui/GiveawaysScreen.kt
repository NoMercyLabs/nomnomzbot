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
import bot.nomnomz.dashboard.core.designsystem.component.PipelineBindPicker
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
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
import bot.nomnomz.dashboard.core.network.GiveawayEntry
import bot.nomnomz.dashboard.core.network.GiveawayEntryMode
import bot.nomnomz.dashboard.core.network.GiveawayPrizeMode
import bot.nomnomz.dashboard.core.network.GiveawayStatus
import bot.nomnomz.dashboard.core.network.GiveawayWinner
import bot.nomnomz.dashboard.core.network.GiveawayWinnerStatus
import bot.nomnomz.dashboard.core.network.MaskedCode
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.UpsertGiveawayBody
import bot.nomnomz.dashboard.feature.giveaways.state.CodePoolsState
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysAccess
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysController
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysState
import bot.nomnomz.dashboard.feature.giveaways.state.PoolDetailState
import bot.nomnomz.dashboard.feature.giveaways.state.EntriesState
import bot.nomnomz.dashboard.feature.giveaways.state.WinnersState
import kotlin.time.Duration.Companion.minutes
import kotlinx.coroutines.launch
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.JsonPrimitive
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
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_auto_close_help
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_auto_close_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_claim_window_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_none
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_exhausted
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_code_pool_placeholder
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_create
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_create_title
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_currency_amount_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_eligibility_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_entry_cost_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_entry_mode_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_exclude_mods_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_from_pot_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_keyword_help
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_keyword_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_max_entries_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_min_account_age_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_min_standing_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_min_watch_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_pipeline_choose
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_pipeline_create_confirm
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_pipeline_create_new
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_pipeline_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_pipeline_new_name
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_prize_mode_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_require_sub_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_requires_18plus_help
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_requires_18plus_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_save
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_title_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_weight_sub_t1_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_weight_sub_t2_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_weight_sub_t3_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_weight_vip_label
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_weighting_help
import nomnomzbot.composeapp.generated.resources.giveaways_dialog_weighting_label
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
import nomnomzbot.composeapp.generated.resources.giveaways_pool_row_type
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
import nomnomzbot.composeapp.generated.resources.giveaways_prize_pipeline
import nomnomzbot.composeapp.generated.resources.giveaways_requires_write
import nomnomzbot.composeapp.generated.resources.giveaways_retry
import nomnomzbot.composeapp.generated.resources.giveaways_row_type
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
import nomnomzbot.composeapp.generated.resources.giveaways_entries_action
import nomnomzbot.composeapp.generated.resources.giveaways_entries_title
import nomnomzbot.composeapp.generated.resources.giveaways_entries_loading
import nomnomzbot.composeapp.generated.resources.giveaways_entries_empty
import nomnomzbot.composeapp.generated.resources.giveaways_entries_error
import nomnomzbot.composeapp.generated.resources.giveaways_entries_close
import nomnomzbot.composeapp.generated.resources.giveaways_entry_tickets
import nomnomzbot.composeapp.generated.resources.giveaways_winners_close
import nomnomzbot.composeapp.generated.resources.giveaways_winners_empty
import nomnomzbot.composeapp.generated.resources.giveaways_winners_error
import nomnomzbot.composeapp.generated.resources.giveaways_winners_loading
import nomnomzbot.composeapp.generated.resources.giveaways_winners_title
import nomnomzbot.composeapp.generated.resources.shell_nav_giveaways
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import bot.nomnomz.dashboard.core.consequences.BlastRadiusLoadState
import bot.nomnomz.dashboard.core.consequences.DeleteBlastRadiusDialog
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary

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
    val entries: EntriesState by controller.entries.collectAsStateWithLifecycle()
    val pipelines: List<PipelineSummary> by controller.pipelines.collectAsStateWithLifecycle()
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
            onEdit = { editor = GiveawayEditor.edit(it, Clock.System.now()) },
            onDelete = { pendingDelete = it },
            onOpen = { giveaway -> scope.launch { controller.openGiveaway(giveaway.id) } },
            onConfirmLifecycle = { giveaway, kind -> pendingLifecycle = LifecycleConfirm(giveaway, kind) },
            onShowWinners = { giveaway -> scope.launch { controller.showWinners(giveaway) } },
            onShowEntries = { giveaway -> scope.launch { controller.showEntries(giveaway) } },
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
            pipelines = pipelines,
            onCreatePipeline = { name -> controller.createPipelineReturning(name) },
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
        val resolvedTitle: String =
            resolveRowLabel(
                primary = giveaway.title,
                typeLabel = stringResource(Res.string.giveaways_row_type),
                discriminatorSource = giveaway.id,
            )
        // Fetched fresh per row (never cached or guessed) â€” the counted blast radius the confirm MUST show
        // before the destructive save can proceed (S-CONSEQ).
        var blastRadius: BlastRadiusLoadState by remember(giveaway.id) { mutableStateOf(BlastRadiusLoadState.Loading) }
        LaunchedEffect(giveaway.id) {
            blastRadius =
                when (val result: ApiResult<BlastRadiusSummary> = controller.fetchBlastRadius(giveaway.id)) {
                    is ApiResult.Ok -> BlastRadiusLoadState.Loaded(result.value)
                    is ApiResult.Failure -> BlastRadiusLoadState.Failed
                }
        }
        DeleteBlastRadiusDialog(
            title = stringResource(Res.string.giveaways_delete_title),
            message = stringResource(Res.string.giveaways_delete_message, resolvedTitle),
            confirmLabel = stringResource(Res.string.giveaways_delete_confirm),
            dismissLabel = stringResource(Res.string.giveaways_cancel),
            blastRadius = blastRadius,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteGiveaway(giveaway.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }

    pendingLifecycle?.let { confirm ->
        val isClose: Boolean = confirm.kind == LifecycleKind.Close
        val resolvedLifecycleTitle: String =
            resolveRowLabel(
                primary = confirm.giveaway.title,
                typeLabel = stringResource(Res.string.giveaways_row_type),
                discriminatorSource = confirm.giveaway.id,
            )
        ConfirmDialog(
            title = stringResource(if (isClose) Res.string.giveaways_close_title else Res.string.giveaways_draw_title),
            message =
                stringResource(
                    if (isClose) Res.string.giveaways_close_message else Res.string.giveaways_draw_message,
                    resolvedLifecycleTitle,
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
        val resolvedPoolName: String =
            resolveRowLabel(
                primary = pool.name,
                typeLabel = stringResource(Res.string.giveaways_pool_row_type),
                discriminatorSource = pool.id,
            )
        // Fetched fresh per row (never cached or guessed) — the counted blast radius the delete confirm MUST
        // show before the destructive delete can proceed (S-CONSEQ).
        var blastRadius: BlastRadiusLoadState by remember(pool.id) { mutableStateOf(BlastRadiusLoadState.Loading) }
        LaunchedEffect(pool.id) {
            blastRadius =
                when (val result: ApiResult<BlastRadiusSummary> = controller.fetchCodePoolBlastRadius(pool.id)) {
                    is ApiResult.Ok -> BlastRadiusLoadState.Loaded(result.value)
                    is ApiResult.Failure -> BlastRadiusLoadState.Failed
                }
        }
        DeleteBlastRadiusDialog(
            title = stringResource(Res.string.giveaways_pool_delete_title),
            message = stringResource(Res.string.giveaways_pool_delete_message, resolvedPoolName),
            confirmLabel = stringResource(Res.string.giveaways_delete_confirm),
            dismissLabel = stringResource(Res.string.giveaways_cancel),
            blastRadius = blastRadius,
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

    if (entries !is EntriesState.Hidden) {
        EntriesDialog(state = entries, onDismiss = { controller.hideEntries() })
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
            GlyphButton(icon = AddGlyph, label = newLabel, onClick = onNew, enabled = enabled)
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
    val displayTitle: String =
        resolveRowLabel(
            primary = giveaway.title,
            typeLabel = stringResource(Res.string.giveaways_row_type),
            discriminatorSource = giveaway.id,
        )
    val editLabel: String = stringResource(Res.string.giveaways_edit_action, displayTitle)
    val deleteLabel: String = stringResource(Res.string.giveaways_delete_action, displayTitle)

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
                text = displayTitle,
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
            // Entries only exist for keyword mode (active_viewers pulls from live chat, no rows to inspect) and
            // only once the giveaway has actually been opened — draft never has any.
            if (giveaway.entryMode == GiveawayEntryMode.Keyword && giveaway.status != GiveawayStatus.Draft) {
                Button(
                    onClick = { callbacks.onShowEntries(giveaway) },
                    variant = ButtonVariant.Outline,
                    size = ButtonSize.Sm,
                ) {
                    Text(text = stringResource(Res.string.giveaways_entries_action))
                }
            }
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
                    icon = EditGlyph,
                    label = editLabel,
                    onClick = { callbacks.onEdit(giveaway) },
                    enabled = enabled,
                )
            }
            ManageGate(decision = writeManage) { enabled ->
                GlyphButton(
                    icon = TrashGlyph,
                    label = deleteLabel,
                    onClick = { callbacks.onDelete(giveaway) },
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
            LifecycleButton(giveaway = giveaway, writeManage = writeManage, callbacks = callbacks)
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
                GlyphButton(icon = AddGlyph, label = newLabel, onClick = onNewPool, enabled = enabled)
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
    val displayName: String =
        resolveRowLabel(
            primary = pool.name,
            typeLabel = stringResource(Res.string.giveaways_pool_row_type),
            discriminatorSource = pool.id,
        )
    val deleteLabel: String = stringResource(Res.string.giveaways_pool_delete_action, displayName)

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = displayName,
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
        GlyphButton(
            icon = TrashGlyph,
            label = deleteLabel,
            onClick = onDelete,
            tint = tokens.destructive,
        )
        Button(onClick = onManage, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.giveaways_pool_manage_button))
        }
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
    val onShowEntries: (Giveaway) -> Unit,
)

private enum class LifecycleKind { Open, Close, Draw }

private data class LifecycleConfirm(val giveaway: Giveaway, val kind: LifecycleKind)

private data class RedrawConfirm(val giveaway: Giveaway, val winner: GiveawayWinner)

// ── Create / edit dialog ─────────────────────────────────────────────────────────

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until the title is non-blank (and, in keyword mode, the keyword). The prize
// picker offers all four modes (announce / currency / pipeline / code pool) — pipeline mode uses the shared
// create-and-bind [PipelineBindPicker] (S046), same as rewards/commands/timers.
@Composable
private fun GiveawayFormDialog(
    editor: GiveawayEditor,
    pools: List<CodePool>,
    pipelines: List<PipelineSummary>,
    onCreatePipeline: suspend (name: String) -> PipelineSummary?,
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
    var pipelineId: String? by remember { mutableStateOf(editor.prizePipelineId) }
    var requires18: Boolean by remember { mutableStateOf(editor.requires18Plus) }
    var requireSub: Boolean by remember { mutableStateOf(editor.requireSub) }
    var minStanding: String by remember { mutableStateOf(editor.minStandingLevel) }
    var minWatch: String by remember { mutableStateOf(editor.minWatchMinutes) }
    var minAccountAge: String by remember { mutableStateOf(editor.minAccountAgeDays) }
    var subT1: String by remember { mutableStateOf(editor.subT1) }
    var subT2: String by remember { mutableStateOf(editor.subT2) }
    var subT3: String by remember { mutableStateOf(editor.subT3) }
    var vip: String by remember { mutableStateOf(editor.vipMultiplier) }
    var autoCloseMinutes: String by remember { mutableStateOf(editor.autoCloseMinutes) }

    // A paid code_pool giveaway needs the 18+ gate on, or the backend refuses VALUE_OUT_PAID_ENTRY (D5) — shown
    // only when it's actually relevant so the form isn't cluttered with a toggle that does nothing otherwise.
    val needsAgeGate: Boolean = prizeMode == GiveawayPrizeMode.CodePool && entryCost.toLongOrNull()?.let { it > 0 } == true

    val keywordMode: Boolean = entryMode == GiveawayEntryMode.Keyword
    val canSubmit: Boolean =
        title.isNotBlank() && (!keywordMode || keyword.isNotBlank()) && (!needsAgeGate || requires18)
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
                TabsList {
                    TabsTrigger(
                        selected = entryMode == GiveawayEntryMode.Keyword,
                        onClick = { entryMode = GiveawayEntryMode.Keyword },
                    ) { Text(stringResource(Res.string.giveaways_entry_mode_keyword), maxLines = 1) }
                    TabsTrigger(
                        selected = entryMode == GiveawayEntryMode.ActiveViewers,
                        onClick = { entryMode = GiveawayEntryMode.ActiveViewers },
                    ) { Text(stringResource(Res.string.giveaways_entry_mode_active), maxLines = 1) }
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
                NumberField(
                    value = autoCloseMinutes,
                    onValueChange = { autoCloseMinutes = it },
                    label = stringResource(Res.string.giveaways_dialog_auto_close_label),
                )
                Text(
                    text = stringResource(Res.string.giveaways_dialog_auto_close_help),
                    style = LocalTypography.current.xs,
                    color = tokens.mutedForeground,
                )
                ToggleRow(
                    label = stringResource(Res.string.giveaways_dialog_exclude_mods_label),
                    checked = excludeMods,
                    onCheckedChange = { excludeMods = it },
                )

                // Eligibility (D3) — opt-in filters; empty/unset = everyone.
                Separator()
                FieldLabel(stringResource(Res.string.giveaways_dialog_eligibility_label))
                ToggleRow(
                    label = stringResource(Res.string.giveaways_dialog_require_sub_label),
                    checked = requireSub,
                    onCheckedChange = { requireSub = it },
                )
                NumberField(
                    value = minStanding,
                    onValueChange = { minStanding = it },
                    label = stringResource(Res.string.giveaways_dialog_min_standing_label),
                )
                NumberField(
                    value = minWatch,
                    onValueChange = { minWatch = it },
                    label = stringResource(Res.string.giveaways_dialog_min_watch_label),
                )
                NumberField(
                    value = minAccountAge,
                    onValueChange = { minAccountAge = it },
                    label = stringResource(Res.string.giveaways_dialog_min_account_age_label),
                )

                // Weighting (D4) — sub-luck ticket multipliers; "1" (unweighted) is the no-op default.
                Separator()
                FieldLabel(stringResource(Res.string.giveaways_dialog_weighting_label))
                Text(
                    text = stringResource(Res.string.giveaways_dialog_weighting_help),
                    style = LocalTypography.current.xs,
                    color = tokens.mutedForeground,
                )
                NumberField(
                    value = subT1,
                    onValueChange = { subT1 = it },
                    label = stringResource(Res.string.giveaways_dialog_weight_sub_t1_label),
                )
                NumberField(
                    value = subT2,
                    onValueChange = { subT2 = it },
                    label = stringResource(Res.string.giveaways_dialog_weight_sub_t2_label),
                )
                NumberField(
                    value = subT3,
                    onValueChange = { subT3 = it },
                    label = stringResource(Res.string.giveaways_dialog_weight_sub_t3_label),
                )
                NumberField(
                    value = vip,
                    onValueChange = { vip = it },
                    label = stringResource(Res.string.giveaways_dialog_weight_vip_label),
                )

                // Prize mode — a four-option segmented picker, with the mode-specific config below it.
                Separator()
                FieldLabel(stringResource(Res.string.giveaways_dialog_prize_mode_label))
                TabsList {
                    TabsTrigger(
                        selected = prizeMode == GiveawayPrizeMode.Announce,
                        onClick = { prizeMode = GiveawayPrizeMode.Announce },
                    ) { Text(stringResource(Res.string.giveaways_prize_announce), maxLines = 1) }
                    TabsTrigger(
                        selected = prizeMode == GiveawayPrizeMode.Currency,
                        onClick = { prizeMode = GiveawayPrizeMode.Currency },
                    ) { Text(stringResource(Res.string.giveaways_prize_currency), maxLines = 1) }
                    TabsTrigger(
                        selected = prizeMode == GiveawayPrizeMode.Pipeline,
                        onClick = { prizeMode = GiveawayPrizeMode.Pipeline },
                    ) { Text(stringResource(Res.string.giveaways_prize_pipeline), maxLines = 1) }
                    TabsTrigger(
                        selected = prizeMode == GiveawayPrizeMode.CodePool,
                        onClick = { prizeMode = GiveawayPrizeMode.CodePool },
                    ) { Text(stringResource(Res.string.giveaways_prize_code_pool), maxLines = 1) }
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
                if (prizeMode == GiveawayPrizeMode.Pipeline) {
                    PipelineBindPicker(
                        pipelines = pipelines,
                        selectedId = pipelineId,
                        onSelect = { pipelineId = it },
                        onCreate = { name -> onCreatePipeline(name) },
                        pickLabel = stringResource(Res.string.giveaways_dialog_pipeline_label),
                        choosePlaceholder = stringResource(Res.string.giveaways_dialog_pipeline_choose),
                        createNewLabel = stringResource(Res.string.giveaways_dialog_pipeline_create_new),
                        newNameLabel = stringResource(Res.string.giveaways_dialog_pipeline_new_name),
                        createLabel = stringResource(Res.string.giveaways_dialog_pipeline_create_confirm),
                        cancelLabel = stringResource(Res.string.giveaways_cancel),
                    )
                }
                if (prizeMode == GiveawayPrizeMode.CodePool) {
                    CodePoolPicker(
                        pools = pools,
                        selectedId = codePoolId,
                        onSelect = { codePoolId = it },
                    )
                }
                if (needsAgeGate) {
                    ToggleRow(
                        label = stringResource(Res.string.giveaways_dialog_requires_18plus_label),
                        checked = requires18,
                        onCheckedChange = { requires18 = it },
                    )
                    Text(
                        text = stringResource(Res.string.giveaways_dialog_requires_18plus_help),
                        style = LocalTypography.current.xs,
                        color = if (requires18) tokens.mutedForeground else tokens.destructive,
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
                            pipelineId = pipelineId,
                            requires18 = requires18,
                            requireSub = requireSub,
                            minStanding = minStanding,
                            minWatch = minWatch,
                            minAccountAge = minAccountAge,
                            subT1 = subT1,
                            subT2 = subT2,
                            subT3 = subT3,
                            vip = vip,
                            autoCloseMinutes = autoCloseMinutes,
                            now = Clock.System.now(),
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
private fun SelectChip(label: String, selected: Boolean, enabled: Boolean = true, onClick: () -> Unit) {
    Badge(selected = selected, enabled = enabled, onClick = onClick) { Text(text = label) }
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
    val poolTypeLabel: String = stringResource(Res.string.giveaways_pool_row_type)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        pools.forEach { pool ->
            val poolLabel: String =
                resolveRowLabel(
                    primary = pool.name,
                    typeLabel = poolTypeLabel,
                    discriminatorSource = pool.id,
                )
            val isSelected: Boolean = pool.id == selectedId
            // A pool with zero available codes can never fulfill a draw — disable it so a broadcaster can't
            // bind a giveaway to a pool that's already fully claimed; the CURRENT selection stays pickable
            // even if it just ran dry, so switching prize modes doesn't silently strand the form.
            val isPickable: Boolean = isSelected || pool.available > 0
            val chipLabel: String =
                if (pool.available > 0) poolLabel
                else stringResource(Res.string.giveaways_dialog_code_pool_exhausted, poolLabel)
            SelectChip(
                label = chipLabel,
                selected = isSelected,
                enabled = isPickable,
                onClick = { onSelect(pool.id) },
            )
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
        title = {
            Text(
                text =
                    stringResource(
                        Res.string.giveaways_winners_title,
                        resolveRowLabel(
                            primary = giveaway?.title,
                            typeLabel = stringResource(Res.string.giveaways_row_type),
                            discriminatorSource = giveaway?.id ?: "unknown",
                        ),
                    ),
            )
        },
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

// ── Entries panel ────────────────────────────────────────────────────────────────

@Composable
private fun EntriesDialog(state: EntriesState, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val giveaway: Giveaway? =
        when (state) {
            is EntriesState.Loading -> state.giveaway
            is EntriesState.Ready -> state.giveaway
            is EntriesState.Error -> state.giveaway
            EntriesState.Hidden -> null
        }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text =
                    stringResource(
                        Res.string.giveaways_entries_title,
                        resolveRowLabel(
                            primary = giveaway?.title,
                            typeLabel = stringResource(Res.string.giveaways_row_type),
                            discriminatorSource = giveaway?.id ?: "unknown",
                        ),
                    ),
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                when (state) {
                    is EntriesState.Loading ->
                        Text(
                            text = stringResource(Res.string.giveaways_entries_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is EntriesState.Error ->
                        Text(
                            text = stringResource(Res.string.giveaways_entries_error, state.detail),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is EntriesState.Ready -> {
                        if (state.entries.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.giveaways_entries_empty),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            state.entries.forEach { entry ->
                                EntryRow(entry)
                                Separator()
                            }
                        }
                    }
                    EntriesState.Hidden -> Unit
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.giveaways_entries_close), color = tokens.primary)
            }
        },
    )
}

@Composable
private fun EntryRow(entry: GiveawayEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = entry.viewerDisplayName,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        // Ticket count only matters once weighting is on — plain 1-ticket entries don't need the noise, but a
        // sub-luck-weighted entrant should be visible here so the list explains why the draw favors them.
        if (entry.ticketCount > 1) {
            Badge(variant = BadgeVariant.Secondary) {
                Text(text = stringResource(Res.string.giveaways_entry_tickets, entry.ticketCount))
            }
        }
    }
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

    val codeLabel: String = code.label?.takeIf { it.isNotBlank() } ?: stringResource(Res.string.giveaways_code_unlabeled)

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = codeLabel,
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
    val prizePipelineId: String?,
    val requires18Plus: Boolean,
    // Eligibility (D3) — opt-in filters; require_follower is deliberately absent, the backend rejects it
    // (unverifiable truthfully) so the dialog never offers a toggle that can only fail.
    val requireSub: Boolean,
    val minStandingLevel: String,
    val minWatchMinutes: String,
    val minAccountAgeDays: String,
    // Weighting (D4) — sub-luck ticket multipliers; "1" (the backend default) means unweighted.
    val subT1: String,
    val subT2: String,
    val subT3: String,
    val vipMultiplier: String,
    // Auto-close (ClosesAt schedule) — minutes from NOW the dialog is open/submitted, not an absolute time, so
    // editing an existing schedule always shows "how long is left" rather than a stale clock-face value.
    val autoCloseMinutes: String,
) {
    // Build the wire body from the dialog's current inputs. Keyword is sent only in keyword mode; the currency /
    // code-pool / pipeline references only in their prize mode — so switching mode never leaves a stale reference
    // behind. Eligibility/weighting collapse to null when every field is at its no-op default (empty json is
    // functionally identical to "everyone" / "unweighted" per D3/D4, so an unedited form sends null, not `{}`).
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
        pipelineId: String?,
        requires18: Boolean,
        requireSub: Boolean,
        minStanding: String,
        minWatch: String,
        minAccountAge: String,
        subT1: String,
        subT2: String,
        subT3: String,
        vip: String,
        autoCloseMinutes: String,
        now: Instant,
    ): UpsertGiveawayBody =
        UpsertGiveawayBody(
            title = title.trim(),
            entryMode = entryMode,
            keyword = if (entryMode == GiveawayEntryMode.Keyword) keyword.trim().takeIf { it.isNotBlank() } else null,
            entryCost = entryCost.toLongOrNull(),
            maxEntriesPerUser = maxEntries.toIntOrNull() ?: 1,
            eligibilityJson = buildEligibilityJson(requireSub, minStanding, minWatch, minAccountAge),
            weightingJson = buildWeightingJson(subT1, subT2, subT3, vip),
            winnerCount = winnerCount.toIntOrNull() ?: 1,
            excludeModerators = excludeMods,
            claimWindowMinutes = claimWindow.toIntOrNull(),
            prizeMode = prizeMode,
            prizeCurrencyAmount = if (prizeMode == GiveawayPrizeMode.Currency) currencyAmount.toLongOrNull() else null,
            prizeFromPot = prizeMode == GiveawayPrizeMode.Currency && fromPot,
            prizePipelineId = if (prizeMode == GiveawayPrizeMode.Pipeline) pipelineId else null,
            prizeCodePoolId = if (prizeMode == GiveawayPrizeMode.CodePool) codePoolId else null,
            requires18Plus = requires18,
            scheduledCloseAt = autoCloseMinutes.toLongOrNull()?.takeIf { it > 0 }?.let { (now + it.minutes).toString() },
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
                prizePipelineId = null,
                requires18Plus = false,
                requireSub = false,
                minStandingLevel = "",
                minWatchMinutes = "",
                minAccountAgeDays = "",
                subT1 = "1",
                subT2 = "1",
                subT3 = "1",
                vipMultiplier = "1",
                autoCloseMinutes = "",
            )

        fun edit(giveaway: Giveaway, now: Instant): GiveawayEditor {
            val eligibility: JsonObject? = parseJsonObjectOrNull(giveaway.eligibilityJson)
            val weighting: JsonObject? = parseJsonObjectOrNull(giveaway.weightingJson)
            // How many whole minutes remain until the existing target, or blank if there is none (or it already
            // passed) — never a stale absolute instant the operator would have to mentally convert.
            val remainingMinutes: String =
                giveaway.scheduledCloseAt
                    ?.let { runCatching { Instant.parse(it) }.getOrNull() }
                    ?.let { target -> (target - now).inWholeMinutes.takeIf { it > 0 } }
                    ?.toString()
                    .orEmpty()
            return GiveawayEditor(
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
                prizePipelineId = giveaway.prizePipelineId,
                requires18Plus = giveaway.requires18Plus,
                requireSub = eligibility?.get("require_sub")?.jsonPrimitive?.booleanOrNull ?: false,
                minStandingLevel = eligibility?.get("min_standing_level")?.jsonPrimitive?.intOrNull?.toString().orEmpty(),
                minWatchMinutes = eligibility?.get("min_watch_minutes")?.jsonPrimitive?.intOrNull?.toString().orEmpty(),
                minAccountAgeDays = eligibility?.get("min_account_age_days")?.jsonPrimitive?.intOrNull?.toString().orEmpty(),
                subT1 = (weighting?.get("sub_t1")?.jsonPrimitive?.intOrNull ?: 1).toString(),
                subT2 = (weighting?.get("sub_t2")?.jsonPrimitive?.intOrNull ?: 1).toString(),
                subT3 = (weighting?.get("sub_t3")?.jsonPrimitive?.intOrNull ?: 1).toString(),
                vipMultiplier = (weighting?.get("vip")?.jsonPrimitive?.intOrNull ?: 1).toString(),
                autoCloseMinutes = remainingMinutes,
            )
        }
    }
}

private fun parseJsonObjectOrNull(json: String?): JsonObject? =
    json?.takeIf(String::isNotBlank)?.let { runCatching { Json.parseToJsonElement(it).jsonObject }.getOrNull() }

private fun buildEligibilityJson(
    requireSub: Boolean,
    minStanding: String,
    minWatch: String,
    minAccountAge: String,
): String? {
    val standing: Int? = minStanding.toIntOrNull()
    val watch: Int? = minWatch.toIntOrNull()
    val age: Int? = minAccountAge.toIntOrNull()
    if (!requireSub && standing == null && watch == null && age == null) return null
    return buildJsonObject {
        if (requireSub) put("require_sub", JsonPrimitive(true))
        standing?.let { put("min_standing_level", JsonPrimitive(it)) }
        watch?.let { put("min_watch_minutes", JsonPrimitive(it)) }
        age?.let { put("min_account_age_days", JsonPrimitive(it)) }
    }.toString()
}

private fun buildWeightingJson(subT1: String, subT2: String, subT3: String, vip: String): String? {
    val t1: Int = subT1.toIntOrNull() ?: 1
    val t2: Int = subT2.toIntOrNull() ?: 1
    val t3: Int = subT3.toIntOrNull() ?: 1
    val v: Int = vip.toIntOrNull() ?: 1
    if (t1 <= 1 && t2 <= 1 && t3 <= 1 && v <= 1) return null
    return buildJsonObject {
        put("sub_t1", JsonPrimitive(t1))
        put("sub_t2", JsonPrimitive(t2))
        put("sub_t3", JsonPrimitive(t3))
        put("vip", JsonPrimitive(v))
    }.toString()
}
