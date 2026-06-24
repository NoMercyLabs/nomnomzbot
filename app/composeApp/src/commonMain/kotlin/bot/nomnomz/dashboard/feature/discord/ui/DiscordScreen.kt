// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.discord.ui

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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.DiscordGuildConnection
import bot.nomnomz.dashboard.core.network.DiscordNotificationConfig
import bot.nomnomz.dashboard.feature.discord.state.DiscordController
import bot.nomnomz.dashboard.feature.discord.state.DiscordState
import bot.nomnomz.dashboard.feature.discord.state.GuildNotifications
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.discord_action_error
import nomnomzbot.composeapp.generated.resources.discord_config_load_error
import nomnomzbot.composeapp.generated.resources.discord_delete_cancel
import nomnomzbot.composeapp.generated.resources.discord_delete_confirm
import nomnomzbot.composeapp.generated.resources.discord_delete_message
import nomnomzbot.composeapp.generated.resources.discord_delete_title
import nomnomzbot.composeapp.generated.resources.discord_dialog_cancel
import nomnomzbot.composeapp.generated.resources.discord_dialog_channel_label
import nomnomzbot.composeapp.generated.resources.discord_dialog_create
import nomnomzbot.composeapp.generated.resources.discord_dialog_create_title
import nomnomzbot.composeapp.generated.resources.discord_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.discord_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.discord_dialog_message_label
import nomnomzbot.composeapp.generated.resources.discord_dialog_save
import nomnomzbot.composeapp.generated.resources.discord_dialog_trigger_label
import nomnomzbot.composeapp.generated.resources.discord_edit_action
import nomnomzbot.composeapp.generated.resources.discord_edit_action_short
import nomnomzbot.composeapp.generated.resources.discord_delete_action
import nomnomzbot.composeapp.generated.resources.discord_delete_action_short
import nomnomzbot.composeapp.generated.resources.discord_empty_body
import nomnomzbot.composeapp.generated.resources.discord_empty_title
import nomnomzbot.composeapp.generated.resources.discord_error
import nomnomzbot.composeapp.generated.resources.discord_guild_inactive
import nomnomzbot.composeapp.generated.resources.discord_guild_active
import nomnomzbot.composeapp.generated.resources.discord_guild_unnamed
import nomnomzbot.composeapp.generated.resources.discord_loading
import nomnomzbot.composeapp.generated.resources.discord_new_rule_action
import nomnomzbot.composeapp.generated.resources.discord_no_rules
import nomnomzbot.composeapp.generated.resources.discord_retry
import nomnomzbot.composeapp.generated.resources.discord_rule_channel
import nomnomzbot.composeapp.generated.resources.discord_rule_no_message
import nomnomzbot.composeapp.generated.resources.discord_title
import nomnomzbot.composeapp.generated.resources.discord_toggle_action
import org.jetbrains.compose.resources.stringResource

// The Discord page (frontend-ia.md, Stream group): the channel's linked Discord guild(s) and, per guild, the
// notification rules — which channel-event trigger (stream.online, channel.follow, …) posts to which Discord
// channel, with what message, on or off. All real data from [DiscordController]. The screen is a pure
// projection of the controller's state; it loads on first composition. This is the full management surface —
// create, edit, enable/disable, and delete a rule — each routed back through the controller, which re-lists
// after every successful write so the page reflects the backend. When no guild is linked, it shows a clear
// empty state pointing the operator at the Integrations page to connect Discord.
@Composable
fun DiscordScreen(controller: DiscordController) {
    val state: DiscordState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // The create/edit dialog target: null = closed; a value = open (empty = create under a guild, pre-filled =
    // edit a rule). The delete-confirm target is the rule pending confirmation, or null when none.
    var editor: RuleEditor? by remember { mutableStateOf(null) }
    var pendingDelete: PendingDelete? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: DiscordState = state) {
            is DiscordState.Loading -> CenteredMessage(stringResource(Res.string.discord_loading))
            is DiscordState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is DiscordState.Empty -> EmptyContent()
            is DiscordState.Ready ->
                ReadyContent(
                    guilds = current.guilds,
                    actionError = current.actionError,
                    onNewRule = { connectionId -> editor = RuleEditor.create(connectionId) },
                    onEditRule = { rule ->
                        editor =
                            RuleEditor.edit(
                                configId = rule.id,
                                triggerType = rule.triggerType,
                                targetChannelId = rule.targetChannelId,
                                message = rule.messageTemplate.orEmpty(),
                                enabled = rule.enabled,
                            )
                    },
                    onToggleRule = { rule, enabled ->
                        scope.launch { controller.toggleConfig(rule.id, enabled) }
                    },
                    onDeleteRule = { rule -> pendingDelete = PendingDelete(rule.id, rule.triggerType) },
                )
        }
    }

    editor?.let { open ->
        RuleFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { triggerType, channelId, message, enabled ->
                editor = null
                scope.launch {
                    if (open.isEdit) {
                        controller.updateConfig(open.configId, channelId, message, enabled)
                    } else {
                        controller.createConfig(open.connectionId, triggerType, channelId, message, enabled)
                    }
                }
            },
        )
    }

    pendingDelete?.let { target ->
        ConfirmDialog(
            title = stringResource(Res.string.discord_delete_title),
            message = stringResource(Res.string.discord_delete_message, target.triggerType),
            confirmLabel = stringResource(Res.string.discord_delete_confirm),
            dismissLabel = stringResource(Res.string.discord_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteConfig(target.configId) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The guild-bearing content: the page header, an optional write-failure banner, then one card per linked guild
// holding its rules + a per-guild "+ New rule" action.
@Composable
private fun ReadyContent(
    guilds: List<GuildNotifications>,
    actionError: String?,
    onNewRule: (connectionId: String) -> Unit,
    onEditRule: (DiscordNotificationConfig) -> Unit,
    onToggleRule: (DiscordNotificationConfig, Boolean) -> Unit,
    onDeleteRule: (DiscordNotificationConfig) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.discord_title),
            style = typography.xl2,
            color = tokens.foreground,
        )
        actionError?.let { ActionErrorBanner(detail = it) }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(vertical = spacing.s1),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            items(items = guilds, key = { guild -> guild.connection.id }) { guild ->
                GuildCard(
                    guild = guild,
                    onNewRule = { onNewRule(guild.connection.id) },
                    onEditRule = onEditRule,
                    onToggleRule = onToggleRule,
                    onDeleteRule = onDeleteRule,
                )
            }
        }
    }
}

@Composable
private fun GuildCard(
    guild: GuildNotifications,
    onNewRule: () -> Unit,
    onEditRule: (DiscordNotificationConfig) -> Unit,
    onToggleRule: (DiscordNotificationConfig, Boolean) -> Unit,
    onDeleteRule: (DiscordNotificationConfig) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        GuildHeader(connection = guild.connection, onNewRule = onNewRule)

        guild.loadError?.let {
            Text(
                text = stringResource(Res.string.discord_config_load_error, it),
                style = LocalTypography.current.sm,
                color = tokens.destructiveForeground,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(tokens.radius.md))
                    .background(tokens.destructive)
                    .padding(horizontal = spacing.s3, vertical = spacing.s2),
            )
        }

        if (guild.configs.isEmpty()) {
            Text(
                text = stringResource(Res.string.discord_no_rules),
                style = LocalTypography.current.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                guild.configs.forEach { rule ->
                    RuleRow(
                        rule = rule,
                        onEdit = { onEditRule(rule) },
                        onToggle = { enabled -> onToggleRule(rule, enabled) },
                        onDelete = { onDeleteRule(rule) },
                    )
                }
            }
        }
    }
}

@Composable
private fun GuildHeader(connection: DiscordGuildConnection, onNewRule: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val guildName: String =
        connection.guildName?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.discord_guild_unnamed)
    val statusLabel: String =
        stringResource(
            if (connection.isLinkActive) Res.string.discord_guild_active
            else Res.string.discord_guild_inactive
        )
    val newLabel: String = stringResource(Res.string.discord_new_rule_action)

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = "$guildName, $statusLabel" },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = guildName,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = statusLabel,
                style = typography.sm,
                color = if (connection.isLinkActive) tokens.mutedForeground else tokens.destructiveForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Button(
            onClick = onNewRule,
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
private fun RuleRow(
    rule: DiscordNotificationConfig,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val channelLine: String = stringResource(Res.string.discord_rule_channel, rule.targetChannelId)
    val message: String =
        rule.messageTemplate?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.discord_rule_no_message)
    val toggleLabel: String = stringResource(Res.string.discord_toggle_action, rule.triggerType)
    val editLabel: String = stringResource(Res.string.discord_edit_action, rule.triggerType)
    val deleteLabel: String = stringResource(Res.string.discord_delete_action, rule.triggerType)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .padding(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the whole rule: "stream.online → channel 111. We are LIVE!".
                .clearAndSetSemantics {
                    contentDescription = "${rule.triggerType}, $channelLine. $message"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = rule.triggerType,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = channelLine,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = message,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }

        Switch(
            checked = rule.enabled,
            onCheckedChange = onToggle,
            colors = switchColors(),
            modifier = Modifier.semantics { contentDescription = toggleLabel },
        )
        TextButton(onClick = onEdit, modifier = Modifier.semantics { contentDescription = editLabel }) {
            Text(text = stringResource(Res.string.discord_edit_action_short), color = tokens.primary, maxLines = 1)
        }
        TextButton(onClick = onDelete, modifier = Modifier.semantics { contentDescription = deleteLabel }) {
            Text(
                text = stringResource(Res.string.discord_delete_action_short),
                color = tokens.destructive,
                maxLines = 1,
            )
        }
    }
}

// One composable for both create and edit (DRY): a [RuleEditor] without a config id = create (the trigger +
// channel are editable), with one = edit (the trigger is read-only — the backend treats it as immutable on the
// row). The affirmative button is disabled until the trigger, channel and message are all non-blank.
@Composable
private fun RuleFormDialog(
    editor: RuleEditor,
    onDismiss: () -> Unit,
    onSubmit: (triggerType: String, channelId: String, message: String, enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var triggerType: String by remember { mutableStateOf(editor.triggerType) }
    var channelId: String by remember { mutableStateOf(editor.targetChannelId) }
    var message: String by remember { mutableStateOf(editor.message) }
    var enabled: Boolean by remember { mutableStateOf(editor.enabled) }

    val canSubmit: Boolean = triggerType.isNotBlank() && channelId.isNotBlank() && message.isNotBlank()
    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.discord_dialog_edit_title
            else Res.string.discord_dialog_create_title
        )
    val submitLabel: String =
        stringResource(if (editor.isEdit) Res.string.discord_dialog_save else Res.string.discord_dialog_create)
    val enabledLabel: String = stringResource(Res.string.discord_dialog_enabled_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = triggerType,
                    onValueChange = { triggerType = it },
                    enabled = !editor.isEdit,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.discord_dialog_trigger_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = channelId,
                    onValueChange = { channelId = it },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.discord_dialog_channel_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = message,
                    onValueChange = { message = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.discord_dialog_message_label)) },
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
                        colors = switchColors(),
                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onSubmit(triggerType, channelId, message, enabled) },
                enabled = canSubmit,
            ) {
                Text(text = submitLabel, color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.discord_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// The not-connected state: Discord has no linked guild for this channel. Points the operator at the
// Integrations page (where the bot-install OAuth lives) rather than offering a connect here — the connect
// surface is owned by Integrations.
@Composable
private fun EmptyContent() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.discord_empty_title),
                style = typography.lg,
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(Res.string.discord_empty_body),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
        }
    }
}

@Composable
private fun ActionErrorBanner(detail: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.discord_action_error, detail),
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
                text = stringResource(Res.string.discord_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.discord_retry)) }
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
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
        cursorColor = tokens.primary,
    )
}

// The create/edit dialog's seed: an editor without a [configId] opens a blank create form scoped to a guild
// [connectionId]; one seeded from a rule opens a pre-filled edit form. [isEdit] decides create-vs-update on
// submit and locks the trigger field (the backend addresses + fixes a rule's trigger once created).
private data class RuleEditor(
    val isEdit: Boolean,
    val connectionId: String,
    val configId: String,
    val triggerType: String,
    val targetChannelId: String,
    val message: String,
    val enabled: Boolean,
) {
    companion object {
        fun create(connectionId: String): RuleEditor =
            RuleEditor(
                isEdit = false,
                connectionId = connectionId,
                configId = "",
                triggerType = "",
                targetChannelId = "",
                message = "",
                enabled = true,
            )

        fun edit(
            configId: String,
            triggerType: String,
            targetChannelId: String,
            message: String,
            enabled: Boolean,
        ): RuleEditor =
            RuleEditor(
                isEdit = true,
                connectionId = "",
                configId = configId,
                triggerType = triggerType,
                targetChannelId = targetChannelId,
                message = message,
                enabled = enabled,
            )
    }
}

// The delete-confirm target: which rule (by id) is pending, plus its trigger for the confirm copy.
private data class PendingDelete(val configId: String, val triggerType: String)
