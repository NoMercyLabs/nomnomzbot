// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.feature.rewards.state.RewardsController
import bot.nomnomz.dashboard.feature.rewards.state.RewardsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.rewards_action_error
import nomnomzbot.composeapp.generated.resources.rewards_cost
import nomnomzbot.composeapp.generated.resources.rewards_delete_action
import nomnomzbot.composeapp.generated.resources.rewards_delete_action_short
import nomnomzbot.composeapp.generated.resources.rewards_delete_cancel
import nomnomzbot.composeapp.generated.resources.rewards_delete_confirm
import nomnomzbot.composeapp.generated.resources.rewards_delete_message
import nomnomzbot.composeapp.generated.resources.rewards_delete_title
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cancel
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cost_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_create
import nomnomzbot.composeapp.generated.resources.rewards_dialog_create_title
import nomnomzbot.composeapp.generated.resources.rewards_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.rewards_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_prompt_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_save
import nomnomzbot.composeapp.generated.resources.rewards_dialog_title_label
import nomnomzbot.composeapp.generated.resources.rewards_disabled
import nomnomzbot.composeapp.generated.resources.rewards_edit_action
import nomnomzbot.composeapp.generated.resources.rewards_edit_action_short
import nomnomzbot.composeapp.generated.resources.rewards_empty
import nomnomzbot.composeapp.generated.resources.rewards_enabled
import nomnomzbot.composeapp.generated.resources.rewards_error
import nomnomzbot.composeapp.generated.resources.rewards_loading
import nomnomzbot.composeapp.generated.resources.rewards_new_action
import nomnomzbot.composeapp.generated.resources.rewards_retry
import nomnomzbot.composeapp.generated.resources.rewards_row_description
import nomnomzbot.composeapp.generated.resources.rewards_title
import nomnomzbot.composeapp.generated.resources.rewards_toggle_action
import org.jetbrains.compose.resources.stringResource

// The Rewards page (frontend-ia.md §3): the channel's channel-point rewards — every reward is real data from
// [RewardsController] (the backend sources it from Twitch's Helix Custom Rewards endpoint). The screen is a pure
// projection of the controller's state; it loads on first composition. This is the full management surface —
// create, edit, enable/disable, and delete — each routed back through the controller, which re-lists after every
// successful write so the page reflects the backend.
@Composable
fun RewardsScreen(controller: RewardsController) {
    val state: RewardsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one
    // = edit). The delete-confirm target is the reward pending confirmation, or null when none.
    var editor: RewardEditor? by remember { mutableStateOf(null) }
    var pendingDelete: RewardSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: RewardsState = state) {
            is RewardsState.Loading -> CenteredMessage(stringResource(Res.string.rewards_loading))
            is RewardsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is RewardsState.Empty ->
                ManagedContent(
                    rewards = emptyList(),
                    actionError = null,
                    onNew = { editor = RewardEditor.create() },
                    onEdit = { reward -> editor = RewardEditor.edit(reward) },
                    onToggle = { reward, enabled ->
                        scope.launch { controller.toggleReward(reward.id, enabled) }
                    },
                    onDelete = { reward -> pendingDelete = reward },
                )
            is RewardsState.Ready ->
                ManagedContent(
                    rewards = current.rewards,
                    actionError = current.actionError,
                    onNew = { editor = RewardEditor.create() },
                    onEdit = { reward -> editor = RewardEditor.edit(reward) },
                    onToggle = { reward, enabled ->
                        scope.launch { controller.toggleReward(reward.id, enabled) }
                    },
                    onDelete = { reward -> pendingDelete = reward },
                )
        }
    }

    editor?.let { open ->
        RewardFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { title, cost, prompt, enabled ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateReward(open.id, title, cost, prompt, enabled)
                    else controller.createReward(title, cost, prompt)
                }
            },
        )
    }

    pendingDelete?.let { reward ->
        ConfirmDialog(
            title = stringResource(Res.string.rewards_delete_title),
            message = stringResource(Res.string.rewards_delete_message, reward.title),
            confirmLabel = stringResource(Res.string.rewards_delete_confirm),
            dismissLabel = stringResource(Res.string.rewards_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteReward(reward.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The list-bearing content: the header with the "+ New reward" action, an optional write-failure banner, and
// either the rows or the empty hint. Shared by the Ready and Empty states so a fresh channel can still create
// its first reward from the same header.
@Composable
private fun ManagedContent(
    rewards: List<RewardSummary>,
    actionError: String?,
    onNew: () -> Unit,
    onEdit: (RewardSummary) -> Unit,
    onToggle: (RewardSummary, Boolean) -> Unit,
    onDelete: (RewardSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(onNew = onNew)
        actionError?.let { ActionErrorBanner(detail = it) }

        if (rewards.isEmpty()) {
            CenteredMessage(stringResource(Res.string.rewards_empty))
        } else {
            RewardList(
                rewards = rewards,
                onEdit = onEdit,
                onToggle = onToggle,
                onDelete = onDelete,
            )
        }
    }
}

@Composable
private fun Header(onNew: () -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val newLabel: String = stringResource(Res.string.rewards_new_action)

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = stringResource(Res.string.rewards_title),
            style = typography.xl2,
            color = tokens.foreground,
        )
        Button(
            onClick = onNew,
            colors = ButtonDefaults.buttonColors(
                containerColor = tokens.primary,
                contentColor = tokens.primaryForeground,
            ),
            modifier = Modifier.semantics { contentDescription = newLabel },
        ) {
            Text(text = newLabel)
        }
    }
}

@Composable
private fun ActionErrorBanner(detail: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.rewards_action_error, detail),
        style = typography.sm,
        color = tokens.destructiveForeground,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.destructive)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
    )
}

@Composable
private fun RewardList(
    rewards: List<RewardSummary>,
    onEdit: (RewardSummary) -> Unit,
    onToggle: (RewardSummary, Boolean) -> Unit,
    onDelete: (RewardSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = rewards, key = { reward -> reward.id }) { reward ->
            RewardRow(
                reward = reward,
                onEdit = { onEdit(reward) },
                onToggle = { enabled -> onToggle(reward, enabled) },
                onDelete = { onDelete(reward) },
            )
        }
    }
}

@Composable
private fun RewardRow(
    reward: RewardSummary,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val costLabel: String = stringResource(Res.string.rewards_cost, reward.cost)
    val stateLabel: String =
        stringResource(if (reward.isEnabled) Res.string.rewards_enabled else Res.string.rewards_disabled)
    val rowDescription: String =
        stringResource(Res.string.rewards_row_description, reward.title, costLabel, stateLabel)
    val toggleLabel: String = stringResource(Res.string.rewards_toggle_action, reward.title)
    val editLabel: String = stringResource(Res.string.rewards_edit_action, reward.title)
    val deleteLabel: String = stringResource(Res.string.rewards_delete_action, reward.title)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the text block: "Hydrate!, 500 points, Enabled".
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = reward.title,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = costLabel,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        Switch(
            checked = reward.isEnabled,
            onCheckedChange = onToggle,
            colors = SwitchDefaults.colors(
                checkedThumbColor = tokens.primaryForeground,
                checkedTrackColor = tokens.primary,
                uncheckedThumbColor = tokens.mutedForeground,
                uncheckedTrackColor = tokens.muted,
                uncheckedBorderColor = tokens.border,
            ),
            modifier = Modifier.semantics { contentDescription = toggleLabel },
        )
        TextButton(
            onClick = onEdit,
            modifier = Modifier.semantics { contentDescription = editLabel },
        ) {
            Text(
                text = stringResource(Res.string.rewards_edit_action_short),
                color = tokens.primary,
                maxLines = 1,
            )
        }
        TextButton(
            onClick = onDelete,
            modifier = Modifier.semantics { contentDescription = deleteLabel },
        ) {
            Text(
                text = stringResource(Res.string.rewards_delete_action_short),
                color = tokens.destructive,
                maxLines = 1,
            )
        }
    }
}

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until the title is non-blank and the cost parses to a positive whole number,
// so a malformed reward can never be submitted. The cost field is digits-only.
@Composable
private fun RewardFormDialog(
    editor: RewardEditor,
    onDismiss: () -> Unit,
    onSubmit: (title: String, cost: Int, prompt: String, enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var title: String by remember { mutableStateOf(editor.title) }
    var cost: String by remember { mutableStateOf(editor.cost) }
    var prompt: String by remember { mutableStateOf(editor.prompt) }
    var enabled: Boolean by remember { mutableStateOf(editor.isEnabled) }

    val parsedCost: Int? = cost.toIntOrNull()
    val canSubmit: Boolean = title.isNotBlank() && parsedCost != null && parsedCost > 0
    val dialogTitle: String =
        stringResource(
            if (editor.isEdit) Res.string.rewards_dialog_edit_title
            else Res.string.rewards_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.rewards_dialog_save else Res.string.rewards_dialog_create
        )
    val enabledLabel: String = stringResource(Res.string.rewards_dialog_enabled_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = dialogTitle) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.rewards_dialog_title_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = cost,
                    // Digits only — drop anything else so the cost field can never hold a non-number.
                    onValueChange = { input -> cost = input.filter { it.isDigit() } },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.rewards_dialog_cost_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = prompt,
                    onValueChange = { prompt = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.rewards_dialog_prompt_label)) },
                    colors = fieldColors(),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = enabledLabel, color = tokens.cardForeground)
                    Switch(
                        checked = enabled,
                        onCheckedChange = { enabled = it },
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = tokens.primaryForeground,
                            checkedTrackColor = tokens.primary,
                            uncheckedThumbColor = tokens.mutedForeground,
                            uncheckedTrackColor = tokens.muted,
                            uncheckedBorderColor = tokens.border,
                        ),
                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { parsedCost?.let { onSubmit(title, it, prompt, enabled) } },
                enabled = canSubmit,
            ) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.rewards_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

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
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
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
                text = stringResource(Res.string.rewards_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.rewards_retry)) }
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

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a reward opens a
// pre-filled edit form. [isEdit] decides create-vs-update on submit and [id] addresses the row the update /
// toggle / delete targets. The list-row projection carries no prompt (it is a detail-only field), so the edit
// form opens with an empty prompt the operator can fill to set it on save.
private data class RewardEditor(
    val isEdit: Boolean,
    val id: String,
    val title: String,
    val cost: String,
    val prompt: String,
    val isEnabled: Boolean,
) {
    companion object {
        fun create(): RewardEditor =
            RewardEditor(isEdit = false, id = "", title = "", cost = "", prompt = "", isEnabled = true)

        fun edit(reward: RewardSummary): RewardEditor =
            RewardEditor(
                isEdit = true,
                id = reward.id,
                title = reward.title,
                cost = reward.cost.toString(),
                prompt = "",
                isEnabled = reward.isEnabled,
            )
    }
}
