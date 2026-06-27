// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.feature.settings.state.JournalPortabilityController
import bot.nomnomz.dashboard.feature.settings.state.JournalPortabilityState
import bot.nomnomz.dashboard.feature.settings.state.SettingsController
import bot.nomnomz.dashboard.feature.settings.state.SettingsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.journal_dismiss
import nomnomzbot.composeapp.generated.resources.journal_error
import nomnomzbot.composeapp.generated.resources.journal_export
import nomnomzbot.composeapp.generated.resources.journal_exported
import nomnomzbot.composeapp.generated.resources.journal_import
import nomnomzbot.composeapp.generated.resources.journal_import_confirm_cancel
import nomnomzbot.composeapp.generated.resources.journal_import_confirm_message
import nomnomzbot.composeapp.generated.resources.journal_import_confirm_ok
import nomnomzbot.composeapp.generated.resources.journal_import_confirm_title
import nomnomzbot.composeapp.generated.resources.journal_imported
import nomnomzbot.composeapp.generated.resources.journal_section_description
import nomnomzbot.composeapp.generated.resources.journal_section_title
import nomnomzbot.composeapp.generated.resources.journal_rebuild
import nomnomzbot.composeapp.generated.resources.journal_rebuild_confirm_cancel
import nomnomzbot.composeapp.generated.resources.journal_rebuild_confirm_message
import nomnomzbot.composeapp.generated.resources.journal_rebuild_confirm_ok
import nomnomzbot.composeapp.generated.resources.journal_rebuild_confirm_title
import nomnomzbot.composeapp.generated.resources.journal_rebuilding
import nomnomzbot.composeapp.generated.resources.journal_working
import nomnomzbot.composeapp.generated.resources.settings_error
import nomnomzbot.composeapp.generated.resources.settings_label_category
import nomnomzbot.composeapp.generated.resources.settings_label_tags
import nomnomzbot.composeapp.generated.resources.settings_label_title
import nomnomzbot.composeapp.generated.resources.settings_loading
import nomnomzbot.composeapp.generated.resources.settings_retry
import nomnomzbot.composeapp.generated.resources.settings_save
import nomnomzbot.composeapp.generated.resources.settings_save_error
import nomnomzbot.composeapp.generated.resources.settings_saved
import nomnomzbot.composeapp.generated.resources.settings_saving
import nomnomzbot.composeapp.generated.resources.settings_status_live
import nomnomzbot.composeapp.generated.resources.settings_status_offline
import nomnomzbot.composeapp.generated.resources.settings_tags_hint
import nomnomzbot.composeapp.generated.resources.settings_title_invalid
import nomnomzbot.composeapp.generated.resources.settings_channel_section
import nomnomzbot.composeapp.generated.resources.settings_bot_join
import nomnomzbot.composeapp.generated.resources.settings_bot_join_desc
import nomnomzbot.composeapp.generated.resources.settings_bot_leave
import nomnomzbot.composeapp.generated.resources.settings_bot_leave_desc
import nomnomzbot.composeapp.generated.resources.settings_bot_leave_confirm_title
import nomnomzbot.composeapp.generated.resources.settings_bot_leave_confirm_message
import nomnomzbot.composeapp.generated.resources.settings_bot_leave_confirm_ok
import nomnomzbot.composeapp.generated.resources.settings_bot_leave_confirm_cancel
import nomnomzbot.composeapp.generated.resources.settings_reset_config
import nomnomzbot.composeapp.generated.resources.settings_reset_config_desc
import nomnomzbot.composeapp.generated.resources.settings_reset_confirm_title
import nomnomzbot.composeapp.generated.resources.settings_reset_confirm_message
import nomnomzbot.composeapp.generated.resources.settings_reset_confirm_ok
import nomnomzbot.composeapp.generated.resources.settings_reset_confirm_cancel
import nomnomzbot.composeapp.generated.resources.settings_channel_action_error
import nomnomzbot.composeapp.generated.resources.settings_delete_channel
import nomnomzbot.composeapp.generated.resources.settings_delete_channel_desc
import nomnomzbot.composeapp.generated.resources.settings_delete_confirm_cancel
import nomnomzbot.composeapp.generated.resources.settings_delete_confirm_message
import nomnomzbot.composeapp.generated.resources.settings_delete_confirm_ok
import nomnomzbot.composeapp.generated.resources.settings_delete_confirm_title
import org.jetbrains.compose.resources.stringResource

// The Settings page: an editable form over the channel's stream metadata — the live status plus the editable
// broadcast title, category/game, and tags, all real data from [SettingsController] — followed by the Event
// Journal export/import section ([JournalPortabilityController]). The screen seeds a local form from the
// controller's loaded info; Save persists the editable fields (a real, non-destructive Helix write) and the
// controller echoes the saved values back. It loads on first composition and offers a retry on failure. The
// journal section is independent of the stream-info load state, so it renders even when the form is loading/errored.
@Composable
fun SettingsScreen(
    controller: SettingsController,
    journalController: JournalPortabilityController,
    role: ManagementRole?,
    onChannelDeleted: () -> Unit = {},
) {
    val state: SettingsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Navigate to onboarding as a side-effect of the ChannelDeleted terminal state.
    LaunchedEffect(state) {
        if (state is SettingsState.ChannelDeleted) onChannelDeleted()
    }

    // Settings gates PER SECTION, each at its own floor (frontend-ia.md §5) — the page has no single manage
    // floor. Stream info (the broadcast title/category/tags, "Bot basics" tier) is an Editor write; the Event
    // Journal export/import (Danger zone) is a Broadcaster write. Each section gates at its floor; below it the
    // controls disable-with-reason (§7). The backend re-checks writes. (The Twitch app credentials moved to the
    // Integrations area — they are an integration like the bot account, not a Settings concern.)
    val streamInfoManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Editor)
    val ownerManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Broadcaster)

    var pendingLeave: Boolean by remember { mutableStateOf(false) }
    var pendingReset: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s6),
    ) {
        Box(modifier = Modifier.fillMaxWidth().weight(1f)) {
            when (val current: SettingsState = state) {
                is SettingsState.Loading ->
                    CenteredMessage(stringResource(Res.string.settings_loading))
                is SettingsState.Error ->
                    ErrorContent(
                        detail = current.detail,
                        onRetry = { scope.launch { controller.load() } },
                    )
                is SettingsState.Ready ->
                    ReadyContent(
                        state = current,
                        manage = streamInfoManage,
                        onSave = { title, gameName, tags ->
                            scope.launch { controller.save(title, gameName, tags) }
                        },
                    )
                is SettingsState.ChannelDeleted ->
                    CenteredMessage(stringResource(Res.string.settings_loading))
            }
        }

        // Channel management — join/leave/reset; Broadcaster floor (setup:write).
        if (state is SettingsState.Ready) {
            ChannelManagementSection(
                actionError = (state as SettingsState.Ready).channelActionError,
                manage = ownerManage,
                onJoin = { scope.launch { controller.joinBot() } },
                onLeave = { pendingLeave = true },
                onReset = { pendingReset = true },
                onDelete = { pendingDelete = true },
            )
        }

        EventJournalSection(controller = journalController, manage = ownerManage)
    }

    if (pendingLeave) {
        ConfirmDialog(
            title = stringResource(Res.string.settings_bot_leave_confirm_title),
            message = stringResource(Res.string.settings_bot_leave_confirm_message),
            confirmLabel = stringResource(Res.string.settings_bot_leave_confirm_ok),
            dismissLabel = stringResource(Res.string.settings_bot_leave_confirm_cancel),
            destructive = false,
            onConfirm = {
                pendingLeave = false
                scope.launch { controller.leaveBot() }
            },
            onDismiss = { pendingLeave = false },
        )
    }

    if (pendingReset) {
        ConfirmDialog(
            title = stringResource(Res.string.settings_reset_confirm_title),
            message = stringResource(Res.string.settings_reset_confirm_message),
            confirmLabel = stringResource(Res.string.settings_reset_confirm_ok),
            dismissLabel = stringResource(Res.string.settings_reset_confirm_cancel),
            destructive = true,
            onConfirm = {
                pendingReset = false
                scope.launch { controller.resetConfig() }
            },
            onDismiss = { pendingReset = false },
        )
    }

    if (pendingDelete) {
        ConfirmDialog(
            title = stringResource(Res.string.settings_delete_confirm_title),
            message = stringResource(Res.string.settings_delete_confirm_message),
            confirmLabel = stringResource(Res.string.settings_delete_confirm_ok),
            dismissLabel = stringResource(Res.string.settings_delete_confirm_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = false
                scope.launch { controller.deleteChannel() }
            },
            onDismiss = { pendingDelete = false },
        )
    }
}

// Tags are edited as a single comma-separated field — the natural shape for a short list — and normalized to the
// list the API wants: split on commas, trim each, drop blanks. Round-trips with [joinTags] so an unchanged field
// is byte-for-byte the loaded list and the "differs from loaded" check below stays exact.
private fun parseTags(raw: String): List<String> =
    raw.split(',').map { it.trim() }.filter { it.isNotEmpty() }

private fun joinTags(tags: List<String>): String = tags.joinToString(", ")

@Composable
private fun ReadyContent(
    state: SettingsState.Ready,
    manage: ManageDecision,
    onSave: (title: String, gameName: String, tags: List<String>) -> Unit,
) {
    val spacing = LocalSpacing.current
    val loaded: StreamInfo = state.info

    // Local editable form, re-seeded whenever new info loads (initial load or a successful save). Holding it
    // screen-side keeps the controller a thin persistence boundary; `remember(loaded)` resets every field to the
    // saved baseline so the "differs from loaded" check below is exact.
    var title: String by remember(loaded) { mutableStateOf(loaded.title.orEmpty()) }
    var gameName: String by remember(loaded) { mutableStateOf(loaded.gameName.orEmpty()) }
    var tagsText: String by remember(loaded) { mutableStateOf(joinTags(loaded.tags)) }

    val editedTags: List<String> = parseTags(tagsText)

    // Twitch rejects an empty broadcast title, so the form requires a non-blank title before it lets a save go.
    val titleValid: Boolean = title.isNotBlank()

    // Save is offered only when the form is valid AND actually differs from the saved baseline — saving an
    // unchanged stream info is a no-op the user shouldn't be invited to make. Each input is a State-backed local
    // recomputed every recomposition, so this re-evaluates as fields change.
    val dirty: Boolean =
        title != loaded.title.orEmpty() ||
            gameName != loaded.gameName.orEmpty() ||
            editedTags != loaded.tags
    val canSave: Boolean = titleValid && dirty && !state.saving

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatusBanner(isLive = loaded.isLive)

        EditCard(
            title = title,
            onTitleChange = { title = it },
            titleValid = titleValid,
            gameName = gameName,
            onGameNameChange = { gameName = it },
            tagsText = tagsText,
            onTagsChange = { tagsText = it },
            // Editing the broadcast metadata gates at the stream-info (Editor) floor; below it the fields go
            // read-only with reason via the gated SaveBar below.
            enabled = !state.saving && manage.isAllowed,
        )

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            manage = manage,
            canSave = canSave && manage.isAllowed,
            onSave = { onSave(title.trim(), gameName.trim(), editedTags) },
        )
    }
}

@Composable
private fun StatusBanner(isLive: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusText: String =
        stringResource(if (isLive) Res.string.settings_status_live else Res.string.settings_status_offline)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "Live" rather than a disconnected dot + label.
            .clearAndSetSemantics { contentDescription = statusText },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Box(
            modifier = Modifier
                .size(spacing.s2)
                .clip(CircleShape)
                .background(if (isLive) tokens.primary else tokens.mutedForeground),
        )
        Text(text = statusText, style = typography.xl, color = tokens.cardForeground)
    }
}

@Composable
private fun EditCard(
    title: String,
    onTitleChange: (String) -> Unit,
    titleValid: Boolean,
    gameName: String,
    onGameNameChange: (String) -> Unit,
    tagsText: String,
    onTagsChange: (String) -> Unit,
    enabled: Boolean,
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
        TitleField(value = title, onValueChange = onTitleChange, valid = titleValid, enabled = enabled)
        CategoryField(value = gameName, onValueChange = onGameNameChange, enabled = enabled)
        TagsField(value = tagsText, onValueChange = onTagsChange, enabled = enabled)
    }
}

@Composable
private fun TitleField(value: String, onValueChange: (String) -> Unit, valid: Boolean, enabled: Boolean) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        isError = !valid,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(stringResource(Res.string.settings_label_title)) },
        supportingText =
            if (!valid) {
                { Text(stringResource(Res.string.settings_title_invalid)) }
            } else {
                null
            },
    )
}

@Composable
private fun CategoryField(value: String, onValueChange: (String) -> Unit, enabled: Boolean) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(stringResource(Res.string.settings_label_category)) },
    )
}

@Composable
private fun TagsField(value: String, onValueChange: (String) -> Unit, enabled: Boolean) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(stringResource(Res.string.settings_label_tags)) },
        supportingText = { Text(stringResource(Res.string.settings_tags_hint)) },
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
                    text = stringResource(Res.string.settings_save_error, saveError),
                    style = typography.sm,
                    color = tokens.destructive,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
            justSaved ->
                Text(
                    text = stringResource(Res.string.settings_saved),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
            else -> Box(modifier = Modifier.weight(1f))
        }

        if (saving) {
            val savingLabel: String = stringResource(Res.string.settings_saving)
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
                    Text(stringResource(Res.string.settings_save))
                }
            }
        }
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
                text = stringResource(Res.string.settings_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.settings_retry)) }
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

// Channel management section: join/leave/reset/delete actions, all gated at the Broadcaster floor (setup:write).
// Join is non-destructive and shows no confirm dialog. Leave, Reset, and Delete are confirmed before executing.
@Composable
private fun ChannelManagementSection(
    actionError: String?,
    manage: ManageDecision,
    onJoin: () -> Unit,
    onLeave: () -> Unit,
    onReset: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.settings_channel_section),
            style = typography.lg,
            color = tokens.cardForeground,
        )

        actionError?.let { err ->
            Text(
                text = stringResource(Res.string.settings_channel_action_error, err),
                style = typography.sm,
                color = tokens.destructiveForeground,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(tokens.radius.md))
                    .background(tokens.destructive)
                    .padding(horizontal = spacing.s3, vertical = spacing.s2),
            )
        }

        // Join
        ChannelActionRow(
            label = stringResource(Res.string.settings_bot_join),
            description = stringResource(Res.string.settings_bot_join_desc),
            buttonLabel = stringResource(Res.string.settings_bot_join),
            destructive = false,
            manage = manage,
            onClick = onJoin,
        )

        // Leave
        ChannelActionRow(
            label = stringResource(Res.string.settings_bot_leave),
            description = stringResource(Res.string.settings_bot_leave_desc),
            buttonLabel = stringResource(Res.string.settings_bot_leave),
            destructive = false,
            manage = manage,
            onClick = onLeave,
        )

        // Reset config
        ChannelActionRow(
            label = stringResource(Res.string.settings_reset_config),
            description = stringResource(Res.string.settings_reset_config_desc),
            buttonLabel = stringResource(Res.string.settings_reset_config),
            destructive = true,
            manage = manage,
            onClick = onReset,
        )

        // Delete channel
        ChannelActionRow(
            label = stringResource(Res.string.settings_delete_channel),
            description = stringResource(Res.string.settings_delete_channel_desc),
            buttonLabel = stringResource(Res.string.settings_delete_channel),
            destructive = true,
            manage = manage,
            onClick = onDelete,
        )
    }
}

@Composable
private fun ChannelActionRow(
    label: String,
    description: String,
    buttonLabel: String,
    destructive: Boolean,
    manage: ManageDecision,
    onClick: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = label, style = typography.base, color = tokens.cardForeground)
            Text(text = description, style = typography.sm, color = tokens.mutedForeground)
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onClick,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = buttonLabel },
            ) {
                Text(
                    text = buttonLabel,
                    color = when {
                        !enabled -> tokens.mutedForeground
                        destructive -> tokens.destructive
                        else -> tokens.primary
                    },
                )
            }
        }
    }
}

// The Event Journal export/import card: Export pulls the channel's whole ledger to an OS-saved file; Import picks
// a file and uploads it behind a ConfirmDialog (it mutates the journal). The last action's outcome — exported,
// the import counts, or an error — shows as a status line the user can dismiss.
@Composable
private fun EventJournalSection(controller: JournalPortabilityController, manage: ManageDecision) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    val state: JournalPortabilityState by controller.state.collectAsStateWithLifecycle()
    var confirmImport: Boolean by remember { mutableStateOf(false) }
    var confirmRebuild: Boolean by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.journal_section_title),
            style = typography.xl,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.journal_section_description),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s4),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { scope.launch { controller.export() } },
                    enabled = !state.busy && enabled,
                    colors = ButtonDefaults.buttonColors(
                        disabledContainerColor = tokens.muted,
                        disabledContentColor = tokens.mutedForeground,
                    ),
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(stringResource(Res.string.journal_export))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = { confirmImport = true },
                    enabled = !state.busy && enabled,
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(
                        text = stringResource(Res.string.journal_import),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = { confirmRebuild = true },
                    enabled = !state.busy && enabled,
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(
                        text = stringResource(Res.string.journal_rebuild),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    )
                }
            }

            JournalStatus(state = state, onDismiss = { controller.dismiss() })
        }
    }

    if (confirmImport) {
        ConfirmDialog(
            title = stringResource(Res.string.journal_import_confirm_title),
            message = stringResource(Res.string.journal_import_confirm_message),
            confirmLabel = stringResource(Res.string.journal_import_confirm_ok),
            dismissLabel = stringResource(Res.string.journal_import_confirm_cancel),
            destructive = true,
            onConfirm = {
                confirmImport = false
                scope.launch { controller.import() }
            },
            onDismiss = { confirmImport = false },
        )
    }

    if (confirmRebuild) {
        ConfirmDialog(
            title = stringResource(Res.string.journal_rebuild_confirm_title),
            message = stringResource(Res.string.journal_rebuild_confirm_message),
            confirmLabel = stringResource(Res.string.journal_rebuild_confirm_ok),
            dismissLabel = stringResource(Res.string.journal_rebuild_confirm_cancel),
            destructive = true,
            onConfirm = {
                confirmRebuild = false
                scope.launch { controller.rebuildProjections() }
            },
            onDismiss = { confirmRebuild = false },
        )
    }
}

// The journal section's status line: a spinner while busy, then the export confirmation, the import counts, or an
// error — each dismissible. Mirrors the SaveBar feedback pattern so the page reads consistently.
@Composable
private fun RowScope.JournalStatus(state: JournalPortabilityState, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    when {
        state.busy -> {
            val workingLabel: String = stringResource(Res.string.journal_working)
            CircularProgressIndicator(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = workingLabel },
            )
            Box(modifier = Modifier.weight(1f))
        }
        state.error != null -> {
            Text(
                text = stringResource(Res.string.journal_error, state.error),
                style = typography.sm,
                color = tokens.destructive,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.journal_dismiss)) }
        }
        state.imported != null -> {
            Text(
                text = stringResource(
                    Res.string.journal_imported,
                    state.imported.imported,
                    state.imported.skippedDuplicate,
                    state.imported.upcast,
                ),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.journal_dismiss)) }
        }
        state.exported -> {
            Text(
                text = stringResource(Res.string.journal_exported),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.journal_dismiss)) }
        }
        state.rebuildTaskId != null -> {
            Text(
                text = stringResource(Res.string.journal_rebuilding, state.rebuildTaskId),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.journal_dismiss)) }
        }
        else -> Box(modifier = Modifier.weight(1f))
    }
}
