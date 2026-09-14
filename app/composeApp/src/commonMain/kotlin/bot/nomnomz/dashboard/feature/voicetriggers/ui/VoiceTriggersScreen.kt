// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.voicetriggers.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyLinkButton
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelAsset
import bot.nomnomz.dashboard.core.network.EMPTY_STICKER_ASSET_ID
import bot.nomnomz.dashboard.core.network.VoiceTrigger
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.voicetriggers.state.VoiceTriggersController
import bot.nomnomz.dashboard.feature.voicetriggers.state.VoiceTriggersState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.voicetriggers_count
import nomnomzbot.composeapp.generated.resources.voicetriggers_cooldown
import nomnomzbot.composeapp.generated.resources.voicetriggers_delete_action
import nomnomzbot.composeapp.generated.resources.voicetriggers_delete_cancel
import nomnomzbot.composeapp.generated.resources.voicetriggers_delete_confirm
import nomnomzbot.composeapp.generated.resources.voicetriggers_delete_message
import nomnomzbot.composeapp.generated.resources.voicetriggers_delete_title
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_cancel
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_create
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_create_title
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_save
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_sticker_label
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_sticker_none
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_starting_count_help
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_starting_count_label
import nomnomzbot.composeapp.generated.resources.voicetriggers_dialog_word_label
import nomnomzbot.composeapp.generated.resources.voicetriggers_disabled
import nomnomzbot.composeapp.generated.resources.voicetriggers_edit_action
import nomnomzbot.composeapp.generated.resources.voicetriggers_empty
import nomnomzbot.composeapp.generated.resources.voicetriggers_enabled
import nomnomzbot.composeapp.generated.resources.voicetriggers_error
import nomnomzbot.composeapp.generated.resources.voicetriggers_helper
import nomnomzbot.composeapp.generated.resources.voicetriggers_listener_copied
import nomnomzbot.composeapp.generated.resources.voicetriggers_listener_copy
import nomnomzbot.composeapp.generated.resources.voicetriggers_listener_explainer
import nomnomzbot.composeapp.generated.resources.voicetriggers_listener_title
import nomnomzbot.composeapp.generated.resources.voicetriggers_listener_unavailable
import nomnomzbot.composeapp.generated.resources.voicetriggers_loading
import nomnomzbot.composeapp.generated.resources.voicetriggers_new_action
import nomnomzbot.composeapp.generated.resources.voicetriggers_retry
import nomnomzbot.composeapp.generated.resources.voicetriggers_row_description
import nomnomzbot.composeapp.generated.resources.voicetriggers_toggle_action
import nomnomzbot.composeapp.generated.resources.shell_nav_voice_triggers
import org.jetbrains.compose.resources.stringResource

// The Voice Triggers page (Chat group, beside Chat Triggers): spoken-word counters + overlay stickers. Every
// trigger and its LIVE count is real data from [VoiceTriggersController]. Sleak: one primary action (New
// trigger) on the header; every row action is ghost/outline or a neutral Switch; the listener-link card's
// CopyLinkButton is its own ghost affordance, never competing with the page's one filled action.
@Composable
fun VoiceTriggersScreen(controller: VoiceTriggersController, role: ManagementRole?) {
    val state: VoiceTriggersState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.VoiceTriggers)

    var editor: TriggerEditor? by remember { mutableStateOf(null) }
    var pendingDelete: VoiceTrigger? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: VoiceTriggersState = state) {
            is VoiceTriggersState.Loading -> CenteredMessage(stringResource(Res.string.voicetriggers_loading))
            is VoiceTriggersState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is VoiceTriggersState.Ready ->
                ManagedContent(
                    triggers = current.triggers,
                    listenerLink = current.listenerLink,
                    manage = manage,
                    onNew = { editor = TriggerEditor.create() },
                    onEdit = { trigger -> editor = TriggerEditor.edit(trigger) },
                    onToggle = { trigger, enabled -> scope.launch { controller.toggleTrigger(trigger.id, enabled) } },
                    onDelete = { trigger -> pendingDelete = trigger },
                )
        }
    }

    editor?.let { open ->
        val assets: List<ChannelAsset> = (state as? VoiceTriggersState.Ready)?.assets ?: emptyList()
        TriggerFormDialog(
            editor = open,
            assets = assets,
            onDismiss = { editor = null },
            onSubmit = { form ->
                editor = null
                scope.launch {
                    if (open.isEdit) {
                        controller.updateTrigger(
                            triggerId = open.id,
                            word = form.word,
                            cooldownSeconds = form.cooldownSeconds,
                            isEnabled = form.isEnabled,
                            stickerAssetId = form.stickerAssetId,
                        )
                    } else {
                        controller.createTrigger(
                            word = form.word,
                            startingCount = form.startingCount,
                            cooldownSeconds = form.cooldownSeconds,
                            isEnabled = form.isEnabled,
                            stickerAssetId = form.stickerAssetId,
                        )
                    }
                }
            },
        )
    }

    pendingDelete?.let { trigger ->
        ConfirmDialog(
            title = stringResource(Res.string.voicetriggers_delete_title),
            message = stringResource(Res.string.voicetriggers_delete_message, trigger.word),
            confirmLabel = stringResource(Res.string.voicetriggers_delete_confirm),
            dismissLabel = stringResource(Res.string.voicetriggers_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteTrigger(trigger.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

@Composable
private fun ManagedContent(
    triggers: List<VoiceTrigger>,
    listenerLink: String?,
    manage: ManageDecision,
    onNew: () -> Unit,
    onEdit: (VoiceTrigger) -> Unit,
    onToggle: (VoiceTrigger, Boolean) -> Unit,
    onDelete: (VoiceTrigger) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(manage = manage, onNew = onNew)
        Text(
            text = stringResource(Res.string.voicetriggers_helper),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        ListenerLinkCard(listenerLink = listenerLink)

        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (triggers.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.voicetriggers_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(items = triggers, key = { _, t -> t.id }) { index, trigger ->
                        TriggerRow(
                            trigger = trigger,
                            manage = manage,
                            onEdit = { onEdit(trigger) },
                            onToggle = { enabled -> onToggle(trigger, enabled) },
                            onDelete = { onDelete(trigger) },
                        )
                        if (index < triggers.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

// A plain-language card explaining WHY the streamer opens this link in a real Chrome tab, never as an OBS
// source — this is a genuinely unusual instruction, so it gets an explanation, not just a bare link. The
// CopyLinkButton is a ghost action on its own row: the page's one filled/primary action stays the header's
// "New trigger" button (Sleak: one primary focal point per page).
@Composable
private fun ListenerLinkCard(listenerLink: String?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.voicetriggers_listener_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.voicetriggers_listener_explainer),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            if (listenerLink != null) {
                CopyLinkButton(
                    url = listenerLink,
                    copyLabel = stringResource(Res.string.voicetriggers_listener_copy),
                    copiedLabel = stringResource(Res.string.voicetriggers_listener_copied),
                )
            } else {
                Text(
                    text = stringResource(Res.string.voicetriggers_listener_unavailable),
                    style = typography.sm,
                    color = tokens.destructive,
                )
            }
        }
    }
}

@Composable
private fun Header(manage: ManageDecision, onNew: () -> Unit) {
    val newLabel: String = stringResource(Res.string.voicetriggers_new_action)
    PageHeader(title = stringResource(Res.string.shell_nav_voice_triggers)) {
        ManageGate(decision = manage) { enabled ->
            Button(onClick = onNew, enabled = enabled) { Text(text = newLabel) }
        }
    }
}

@Composable
private fun TriggerRow(
    trigger: VoiceTrigger,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusLabel: String =
        if (trigger.isEnabled) stringResource(Res.string.voicetriggers_enabled)
        else stringResource(Res.string.voicetriggers_disabled)
    val cooldownLabel: String = stringResource(Res.string.voicetriggers_cooldown, trigger.cooldownSeconds)
    val countLabel: String = stringResource(Res.string.voicetriggers_count, trigger.currentCount)
    val rowDescription: String =
        stringResource(Res.string.voicetriggers_row_description, trigger.word, cooldownLabel, statusLabel)
    val toggleLabel: String = stringResource(Res.string.voicetriggers_toggle_action, trigger.word)
    val editLabel: String = stringResource(Res.string.voicetriggers_edit_action, trigger.word)
    val deleteLabel: String = stringResource(Res.string.voicetriggers_delete_action, trigger.word)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = trigger.word, style = typography.base, color = tokens.cardForeground)
            Text(
                text = "$countLabel · $cooldownLabel",
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(icon = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = trigger.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
    }
}

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit.
// [startingCount] is only editable in create mode — it is a one-time seed (backend never accepts an update to
// it), so the edit form does not even render the field, avoiding a control that looks editable but silently
// does nothing.
@Composable
private fun TriggerFormDialog(
    editor: TriggerEditor,
    assets: List<ChannelAsset>,
    onDismiss: () -> Unit,
    onSubmit: (TriggerForm) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var word: String by remember { mutableStateOf(editor.word) }
    var startingCount: String by remember { mutableStateOf(editor.startingCount.toString()) }
    var cooldown: String by remember { mutableStateOf(editor.cooldownSeconds.toString()) }
    var isEnabled: Boolean by remember { mutableStateOf(editor.isEnabled) }
    var stickerAssetId: String? by remember { mutableStateOf(editor.stickerAssetId) }
    var stickerMenuOpen: Boolean by remember { mutableStateOf(false) }

    val startingCountValue: Int? = startingCount.ifBlank { "0" }.toIntOrNull()
    val cooldownValue: Int? = cooldown.ifBlank { "0" }.toIntOrNull()
    val cooldownValid: Boolean = cooldownValue != null && cooldownValue >= 0
    val startingCountValid: Boolean = editor.isEdit || (startingCountValue != null && startingCountValue >= 0)
    val canSubmit: Boolean = word.isNotBlank() && cooldownValid && startingCountValid

    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.voicetriggers_dialog_edit_title
            else Res.string.voicetriggers_dialog_create_title
        )
    val submitLabel: String =
        stringResource(if (editor.isEdit) Res.string.voicetriggers_dialog_save else Res.string.voicetriggers_dialog_create)
    val enabledLabel: String = stringResource(Res.string.voicetriggers_dialog_enabled_label)
    val stickerNoneLabel: String = stringResource(Res.string.voicetriggers_dialog_sticker_none)
    val selectedAssetLabel: String =
        assets.firstOrNull { it.id == stickerAssetId }?.displayName ?: stickerNoneLabel

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(
                modifier = Modifier.heightIn(max = spacing.s24 * 4).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = word,
                    onValueChange = { word = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.voicetriggers_dialog_word_label),
                )

                if (!editor.isEdit) {
                    AppTextField(
                        value = startingCount,
                        onValueChange = { input -> startingCount = input.filter { it.isDigit() } },
                        isError = !startingCountValid,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.voicetriggers_dialog_starting_count_label),
                        supportingText = stringResource(Res.string.voicetriggers_dialog_starting_count_help),
                    )
                }

                AppTextField(
                    value = cooldown,
                    onValueChange = { input -> cooldown = input.filter { it.isDigit() } },
                    isError = !cooldownValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.voicetriggers_dialog_cooldown_label),
                )

                AppSelectField(
                    label = stringResource(Res.string.voicetriggers_dialog_sticker_label),
                    value = selectedAssetLabel,
                    expanded = stickerMenuOpen,
                    onExpandedChange = { stickerMenuOpen = it },
                    modifier = Modifier.fillMaxWidth(),
                    menu = {
                        DropdownMenuItem(
                            text = { Text(stickerNoneLabel, color = tokens.cardForeground) },
                            onClick = {
                                stickerAssetId = EMPTY_STICKER_ASSET_ID
                                stickerMenuOpen = false
                            },
                        )
                        assets.forEach { asset ->
                            DropdownMenuItem(
                                text = { Text(asset.displayName, color = tokens.cardForeground) },
                                onClick = {
                                    stickerAssetId = asset.id
                                    stickerMenuOpen = false
                                },
                            )
                        }
                    },
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = enabledLabel, color = tokens.cardForeground)
                    Switch(
                        checked = isEnabled,
                        onCheckedChange = { isEnabled = it },
                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSubmit(
                        TriggerForm(
                            word = word,
                            startingCount = startingCountValue ?: 0,
                            cooldownSeconds = cooldownValue ?: 0,
                            isEnabled = isEnabled,
                            stickerAssetId = stickerAssetId,
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
                Text(text = stringResource(Res.string.voicetriggers_dialog_cancel), color = tokens.mutedForeground)
            }
        },
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
                text = stringResource(Res.string.voicetriggers_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.voicetriggers_retry)) }
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

// The dialog's submitted values, bundled so the create/edit callback has one clean parameter.
private data class TriggerForm(
    val word: String,
    val startingCount: Int,
    val cooldownSeconds: Int,
    val isEnabled: Boolean,
    val stickerAssetId: String?,
)

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a trigger opens a
// pre-filled edit form.
private data class TriggerEditor(
    val isEdit: Boolean,
    val id: String,
    val word: String,
    val startingCount: Int,
    val cooldownSeconds: Int,
    val isEnabled: Boolean,
    val stickerAssetId: String?,
) {
    companion object {
        fun create(): TriggerEditor =
            TriggerEditor(
                isEdit = false,
                id = "",
                word = "",
                startingCount = 0,
                cooldownSeconds = 5,
                isEnabled = true,
                stickerAssetId = null,
            )

        fun edit(trigger: VoiceTrigger): TriggerEditor =
            TriggerEditor(
                isEdit = true,
                id = trigger.id,
                word = trigger.word,
                startingCount = trigger.startingCount,
                cooldownSeconds = trigger.cooldownSeconds,
                isEnabled = trigger.isEnabled,
                stickerAssetId = trigger.stickerAssetId,
            )
    }
}
