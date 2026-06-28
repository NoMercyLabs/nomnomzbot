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
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
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
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.network.DiscordConfigPreview
import bot.nomnomz.dashboard.core.network.DiscordDispatchLogEntry
import bot.nomnomz.dashboard.core.network.DiscordGuildConnection
import bot.nomnomz.dashboard.core.network.DiscordNotificationConfig
import bot.nomnomz.dashboard.core.network.DiscordNotificationRole
import bot.nomnomz.dashboard.feature.discord.state.DiscordController
import bot.nomnomz.dashboard.feature.discord.state.DiscordState
import bot.nomnomz.dashboard.feature.discord.state.GuildNotifications
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
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
import nomnomzbot.composeapp.generated.resources.shell_nav_discord
import nomnomzbot.composeapp.generated.resources.discord_toggle_action
import nomnomzbot.composeapp.generated.resources.discord_consent_approved
import nomnomzbot.composeapp.generated.resources.discord_consent_pending
import nomnomzbot.composeapp.generated.resources.discord_consent_approve
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke
import nomnomzbot.composeapp.generated.resources.discord_consent_approve_title
import nomnomzbot.composeapp.generated.resources.discord_consent_discord_user_id
import nomnomzbot.composeapp.generated.resources.discord_consent_approve_action
import nomnomzbot.composeapp.generated.resources.discord_consent_cancel
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_title
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_message
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_confirm
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_dismiss
import nomnomzbot.composeapp.generated.resources.discord_roles_title
import nomnomzbot.composeapp.generated.resources.discord_roles_empty
import nomnomzbot.composeapp.generated.resources.discord_roles_add
import nomnomzbot.composeapp.generated.resources.discord_roles_create_title
import nomnomzbot.composeapp.generated.resources.discord_roles_discord_role_id
import nomnomzbot.composeapp.generated.resources.discord_roles_role_name
import nomnomzbot.composeapp.generated.resources.discord_roles_self_assign
import nomnomzbot.composeapp.generated.resources.discord_roles_role_id_required
import nomnomzbot.composeapp.generated.resources.discord_roles_create
import nomnomzbot.composeapp.generated.resources.discord_roles_cancel
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_title
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_message
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_confirm
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_cancel
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_action
import nomnomzbot.composeapp.generated.resources.discord_roles_post_button
import nomnomzbot.composeapp.generated.resources.discord_roles_post_button_title
import nomnomzbot.composeapp.generated.resources.discord_roles_button_channel_id
import nomnomzbot.composeapp.generated.resources.discord_roles_button_channel_required
import nomnomzbot.composeapp.generated.resources.discord_roles_button_post
import nomnomzbot.composeapp.generated.resources.discord_roles_opt_in_count
import nomnomzbot.composeapp.generated.resources.discord_log_title
import nomnomzbot.composeapp.generated.resources.discord_log_empty
import nomnomzbot.composeapp.generated.resources.discord_log_load
import nomnomzbot.composeapp.generated.resources.discord_log_loading
import nomnomzbot.composeapp.generated.resources.discord_preview_action
import nomnomzbot.composeapp.generated.resources.discord_preview_title
import nomnomzbot.composeapp.generated.resources.discord_preview_ping_label
import nomnomzbot.composeapp.generated.resources.discord_preview_close
import org.jetbrains.compose.resources.stringResource

// The Discord page (frontend-ia.md, Stream group): the channel's linked Discord guild(s) and, per guild, the
// notification rules — which channel-event trigger (stream.online, channel.follow, …) posts to which Discord
// channel, with what message, on or off. All real data from [DiscordController]. The screen is a pure
// projection of the controller's state; it loads on first composition. This is the full management surface —
// create, edit, enable/disable, and delete a rule — each routed back through the controller, which re-lists
// after every successful write so the page reflects the backend. When no guild is linked, it shows a clear
// empty state pointing the operator at the Integrations page to connect Discord.
@Composable
fun DiscordScreen(controller: DiscordController, role: ManagementRole?) {
    val state: DiscordState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Discord gates every write control at its single Broadcaster manage floor
    // (frontend-ia.md §3). A caller below it sees each guild's rules but the new-rule / toggle / edit / delete
    // controls disabled with "Requires Broadcaster" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Discord)

    // Rule create/edit dialog. Delete-confirm. Consent dialogs. Role create / delete / post-button. Preview.
    var editor: RuleEditor? by remember { mutableStateOf(null) }
    var pendingDelete: PendingDelete? by remember { mutableStateOf(null) }
    var pendingConsentApprove: String? by remember { mutableStateOf(null) }  // connectionId
    var pendingConsentRevoke: String? by remember { mutableStateOf(null) }   // connectionId
    var pendingRoleCreate: String? by remember { mutableStateOf(null) }      // connectionId
    var pendingRoleDelete: PendingRoleDelete? by remember { mutableStateOf(null) }
    var pendingPostButton: String? by remember { mutableStateOf(null) }      // roleId
    var preview: DiscordConfigPreview? by remember { mutableStateOf(null) }

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
                    manage = manage,
                    controller = controller,
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
                    onPreviewRule = { rule ->
                        scope.launch { preview = controller.previewConfig(rule.id) }
                    },
                    onApproveConsent = { connectionId -> pendingConsentApprove = connectionId },
                    onRevokeConsent = { connectionId -> pendingConsentRevoke = connectionId },
                    onAddRole = { connectionId -> pendingRoleCreate = connectionId },
                    onDeleteRole = { role ->
                        pendingRoleDelete = PendingRoleDelete(role.id, role.roleName ?: role.discordRoleId)
                    },
                    onPostRoleButton = { role -> pendingPostButton = role.id },
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

    pendingConsentApprove?.let { connectionId ->
        ApproveConsentDialog(
            onDismiss = { pendingConsentApprove = null },
            onApprove = { discordUserId ->
                pendingConsentApprove = null
                scope.launch { controller.approveServerConsent(connectionId, discordUserId) }
            },
        )
    }

    pendingConsentRevoke?.let { connectionId ->
        ConfirmDialog(
            title = stringResource(Res.string.discord_consent_revoke_title),
            message = stringResource(Res.string.discord_consent_revoke_message),
            confirmLabel = stringResource(Res.string.discord_consent_revoke_confirm),
            dismissLabel = stringResource(Res.string.discord_consent_revoke_dismiss),
            destructive = true,
            onConfirm = {
                pendingConsentRevoke = null
                scope.launch { controller.revokeServerConsent(connectionId) }
            },
            onDismiss = { pendingConsentRevoke = null },
        )
    }

    pendingRoleCreate?.let { connectionId ->
        CreateRoleDialog(
            onDismiss = { pendingRoleCreate = null },
            onCreate = { discordRoleId, roleName, selfAssign ->
                pendingRoleCreate = null
                scope.launch { controller.createRole(connectionId, discordRoleId, roleName, selfAssign) }
            },
        )
    }

    pendingRoleDelete?.let { target ->
        ConfirmDialog(
            title = stringResource(Res.string.discord_roles_delete_title),
            message = stringResource(Res.string.discord_roles_delete_message, target.displayName),
            confirmLabel = stringResource(Res.string.discord_roles_delete_confirm),
            dismissLabel = stringResource(Res.string.discord_roles_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingRoleDelete = null
                scope.launch { controller.deleteRole(target.roleId) }
            },
            onDismiss = { pendingRoleDelete = null },
        )
    }

    pendingPostButton?.let { roleId ->
        PostButtonDialog(
            onDismiss = { pendingPostButton = null },
            onPost = { channelId ->
                pendingPostButton = null
                scope.launch { controller.postRoleButton(roleId, channelId) }
            },
        )
    }

    preview?.let { p ->
        PreviewDialog(preview = p, onDismiss = { preview = null })
    }
}

// The guild-bearing content: the page header, an optional write-failure banner, then one card per linked guild
// holding its rules + a per-guild "+ New rule" action.
@Composable
private fun ReadyContent(
    guilds: List<GuildNotifications>,
    actionError: String?,
    manage: ManageDecision,
    controller: DiscordController,
    onNewRule: (connectionId: String) -> Unit,
    onEditRule: (DiscordNotificationConfig) -> Unit,
    onToggleRule: (DiscordNotificationConfig, Boolean) -> Unit,
    onDeleteRule: (DiscordNotificationConfig) -> Unit,
    onPreviewRule: (DiscordNotificationConfig) -> Unit,
    onApproveConsent: (connectionId: String) -> Unit,
    onRevokeConsent: (connectionId: String) -> Unit,
    onAddRole: (connectionId: String) -> Unit,
    onDeleteRole: (DiscordNotificationRole) -> Unit,
    onPostRoleButton: (DiscordNotificationRole) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_discord))
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.discord_action_error, it)) }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(vertical = spacing.s1),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            items(items = guilds, key = { guild -> guild.connection.id }) { guild ->
                GuildCard(
                    guild = guild,
                    manage = manage,
                    controller = controller,
                    onNewRule = { onNewRule(guild.connection.id) },
                    onEditRule = onEditRule,
                    onToggleRule = onToggleRule,
                    onDeleteRule = onDeleteRule,
                    onPreviewRule = onPreviewRule,
                    onApproveConsent = { onApproveConsent(guild.connection.id) },
                    onRevokeConsent = { onRevokeConsent(guild.connection.id) },
                    onAddRole = { onAddRole(guild.connection.id) },
                    onDeleteRole = onDeleteRole,
                    onPostRoleButton = onPostRoleButton,
                )
            }
        }
    }
}

@Composable
private fun GuildCard(
    guild: GuildNotifications,
    manage: ManageDecision,
    controller: DiscordController,
    onNewRule: () -> Unit,
    onEditRule: (DiscordNotificationConfig) -> Unit,
    onToggleRule: (DiscordNotificationConfig, Boolean) -> Unit,
    onDeleteRule: (DiscordNotificationConfig) -> Unit,
    onPreviewRule: (DiscordNotificationConfig) -> Unit,
    onApproveConsent: () -> Unit,
    onRevokeConsent: () -> Unit,
    onAddRole: () -> Unit,
    onDeleteRole: (DiscordNotificationRole) -> Unit,
    onPostRoleButton: (DiscordNotificationRole) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()

    // Roles and dispatch log are loaded on-demand to keep the initial card render fast.
    var roles: List<DiscordNotificationRole>? by remember { mutableStateOf(null) }
    var rolesLoading: Boolean by remember { mutableStateOf(false) }
    var logEntries: List<DiscordDispatchLogEntry>? by remember { mutableStateOf(null) }
    var logLoading: Boolean by remember { mutableStateOf(false) }

    // Load roles once, automatically, when the card first composes.
    LaunchedEffect(guild.connection.id) {
        rolesLoading = true
        when (val result = controller.roles(guild.connection.id)) {
            is bot.nomnomz.dashboard.core.network.ApiResult.Ok -> roles = result.value
            is bot.nomnomz.dashboard.core.network.ApiResult.Failure -> roles = emptyList()
        }
        rolesLoading = false
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        GuildHeader(
            connection = guild.connection,
            manage = manage,
            onNewRule = onNewRule,
            onApproveConsent = onApproveConsent,
            onRevokeConsent = onRevokeConsent,
        )

        guild.loadError?.let {
            ActionErrorBanner(message = stringResource(Res.string.discord_config_load_error, it))
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
                        manage = manage,
                        onEdit = { onEditRule(rule) },
                        onToggle = { enabled -> onToggleRule(rule, enabled) },
                        onDelete = { onDeleteRule(rule) },
                        onPreview = { onPreviewRule(rule) },
                    )
                }
            }
        }

        // ── Notification roles ─────────────────────────────────────────────
        HorizontalDivider(color = tokens.border)
        RolesSection(
            roles = roles,
            loading = rolesLoading,
            manage = manage,
            onAdd = onAddRole,
            onDelete = onDeleteRole,
            onPostButton = onPostRoleButton,
        )

        // ── Dispatch log (load on demand) ──────────────────────────────────
        HorizontalDivider(color = tokens.border)
        DispatchLogSection(
            entries = logEntries,
            loading = logLoading,
            onLoad = {
                scope.launch {
                    logLoading = true
                    when (val result = controller.dispatchLog(guild.connection.id)) {
                        is bot.nomnomz.dashboard.core.network.ApiResult.Ok -> logEntries = result.value
                        is bot.nomnomz.dashboard.core.network.ApiResult.Failure -> logEntries = emptyList()
                    }
                    logLoading = false
                }
            },
        )
    }
}

@Composable
private fun GuildHeader(
    connection: DiscordGuildConnection,
    manage: ManageDecision,
    onNewRule: () -> Unit,
    onApproveConsent: () -> Unit,
    onRevokeConsent: () -> Unit,
) {
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
    val consentLabel: String =
        stringResource(
            if (connection.serverConsentStatus == "approved") Res.string.discord_consent_approved
            else Res.string.discord_consent_pending
        )
    val newLabel: String = stringResource(Res.string.discord_new_rule_action)

    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
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
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = onNewRule,
                    enabled = enabled,
                    colors = ButtonDefaults.buttonColors(
                        containerColor = tokens.primary,
                        contentColor = tokens.primaryForeground,
                        disabledContainerColor = tokens.muted,
                        disabledContentColor = tokens.mutedForeground,
                    ),
                    modifier = Modifier.semantics { contentDescription = newLabel },
                ) {
                    Text(text = newLabel)
                }
            }
        }
        // Server consent status + approve/revoke action.
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = consentLabel,
                style = typography.xs,
                color = if (connection.serverConsentStatus == "approved") tokens.mutedForeground
                        else tokens.destructiveForeground,
            )
            ManageGate(decision = manage) { enabled ->
                if (connection.serverConsentStatus == "approved") {
                    TextButton(onClick = onRevokeConsent, enabled = enabled) {
                        Text(
                            text = stringResource(Res.string.discord_consent_revoke),
                            style = typography.xs,
                            color = if (enabled) tokens.destructive else tokens.mutedForeground,
                        )
                    }
                } else {
                    TextButton(onClick = onApproveConsent, enabled = enabled) {
                        Text(
                            text = stringResource(Res.string.discord_consent_approve),
                            style = typography.xs,
                            color = if (enabled) tokens.primary else tokens.mutedForeground,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun RuleRow(
    rule: DiscordNotificationConfig,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
    onPreview: () -> Unit,
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

        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = rule.enabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                colors = switchColors(),
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onEdit,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = editLabel },
            ) {
                Text(
                    text = stringResource(Res.string.discord_edit_action_short),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onDelete,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = deleteLabel },
            ) {
                Text(
                    text = stringResource(Res.string.discord_delete_action_short),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        TextButton(onClick = onPreview) {
            Text(
                text = stringResource(Res.string.discord_preview_action),
                color = tokens.mutedForeground,
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

// ── Roles section ────────────────────────────────────────────────────────────

@Composable
private fun RolesSection(
    roles: List<DiscordNotificationRole>?,
    loading: Boolean,
    manage: ManageDecision,
    onAdd: () -> Unit,
    onDelete: (DiscordNotificationRole) -> Unit,
    onPostButton: (DiscordNotificationRole) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(Res.string.discord_roles_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onAdd, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.discord_roles_add),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                    )
                }
            }
        }
        when {
            loading -> CenteredMessage(stringResource(Res.string.discord_log_loading))
            roles == null || roles.isEmpty() ->
                Text(
                    text = stringResource(Res.string.discord_roles_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            else ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    roles.forEach { role -> RoleRow(role = role, manage = manage, onDelete = onDelete, onPostButton = onPostButton) }
                }
        }
    }
}

@Composable
private fun RoleRow(
    role: DiscordNotificationRole,
    manage: ManageDecision,
    onDelete: (DiscordNotificationRole) -> Unit,
    onPostButton: (DiscordNotificationRole) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val displayName: String = role.roleName?.takeIf { it.isNotBlank() } ?: role.discordRoleId
    val deleteLabel: String = stringResource(Res.string.discord_roles_delete_action, displayName)

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
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = displayName, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                text = stringResource(Res.string.discord_roles_opt_in_count, role.optInCount),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        if (role.selfAssignEnabled) {
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = { onPostButton(role) }, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.discord_roles_post_button),
                        style = typography.xs,
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onDelete(role) },
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = deleteLabel },
            ) {
                Text(
                    text = stringResource(Res.string.discord_delete_action_short),
                    style = typography.xs,
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// ── Dispatch log section ──────────────────────────────────────────────────────

@Composable
private fun DispatchLogSection(
    entries: List<DiscordDispatchLogEntry>?,
    loading: Boolean,
    onLoad: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(Res.string.discord_log_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            if (entries == null && !loading) {
                TextButton(onClick = onLoad) {
                    Text(text = stringResource(Res.string.discord_log_load), color = tokens.primary)
                }
            }
        }
        when {
            loading -> CenteredMessage(stringResource(Res.string.discord_log_loading))
            entries == null -> {}  // not yet requested — "View log" button shown above
            entries.isEmpty() ->
                Text(
                    text = stringResource(Res.string.discord_log_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            else ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    entries.forEach { entry -> DispatchLogRow(entry = entry) }
                }
        }
    }
}

@Composable
private fun DispatchLogRow(entry: DiscordDispatchLogEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val isOk: Boolean = entry.status.lowercase() == "sent" || entry.status.lowercase() == "ok"

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(tokens.muted)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.Top,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = entry.triggerType, style = typography.sm, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
            entry.error?.let { Text(text = it, style = typography.xs, color = tokens.destructiveForeground, maxLines = 2, overflow = TextOverflow.Ellipsis) }
        }
        Text(
            text = entry.status,
            style = typography.xs,
            color = if (isOk) tokens.mutedForeground else tokens.destructiveForeground,
        )
    }
}

// ── New dialogs ───────────────────────────────────────────────────────────────

@Composable
private fun ApproveConsentDialog(onDismiss: () -> Unit, onApprove: (discordUserId: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var userId: String by remember { mutableStateOf("") }
    val userIdError: Boolean = userId.isBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(stringResource(Res.string.discord_consent_approve_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = userId,
                    onValueChange = { userId = it },
                    label = stringResource(Res.string.discord_consent_discord_user_id),
                    isError = userIdError && userId.isNotEmpty(),
                    errorText = stringResource(Res.string.discord_roles_role_id_required),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onApprove(userId) }, enabled = userId.isNotBlank()) {
                Text(
                    text = stringResource(Res.string.discord_consent_approve_action),
                    color = if (userId.isNotBlank()) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.discord_consent_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun CreateRoleDialog(
    onDismiss: () -> Unit,
    onCreate: (discordRoleId: String, roleName: String?, selfAssign: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var discordRoleId: String by remember { mutableStateOf("") }
    var roleName: String by remember { mutableStateOf("") }
    var selfAssign: Boolean by remember { mutableStateOf(false) }
    val roleIdError: Boolean = discordRoleId.isBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(stringResource(Res.string.discord_roles_create_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = discordRoleId,
                    onValueChange = { discordRoleId = it },
                    label = stringResource(Res.string.discord_roles_discord_role_id),
                    isError = roleIdError && discordRoleId.isNotEmpty(),
                    errorText = stringResource(Res.string.discord_roles_role_id_required),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = roleName,
                    onValueChange = { roleName = it },
                    label = stringResource(Res.string.discord_roles_role_name),
                    isError = false,
                    errorText = null,
                    modifier = Modifier.fillMaxWidth(),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(stringResource(Res.string.discord_roles_self_assign), color = tokens.cardForeground)
                    Switch(
                        checked = selfAssign,
                        onCheckedChange = { selfAssign = it },
                        colors = switchColors(),
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onCreate(discordRoleId, roleName.ifBlank { null }, selfAssign) }, enabled = discordRoleId.isNotBlank()) {
                Text(
                    text = stringResource(Res.string.discord_roles_create),
                    color = if (discordRoleId.isNotBlank()) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.discord_roles_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun PostButtonDialog(onDismiss: () -> Unit, onPost: (channelId: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var channelId: String by remember { mutableStateOf("") }
    val channelError: Boolean = channelId.isBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(stringResource(Res.string.discord_roles_post_button_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = channelId,
                    onValueChange = { channelId = it },
                    label = stringResource(Res.string.discord_roles_button_channel_id),
                    isError = channelError && channelId.isNotEmpty(),
                    errorText = stringResource(Res.string.discord_roles_button_channel_required),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onPost(channelId) }, enabled = channelId.isNotBlank()) {
                Text(
                    text = stringResource(Res.string.discord_roles_button_post),
                    color = if (channelId.isNotBlank()) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.discord_roles_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun PreviewDialog(preview: DiscordConfigPreview, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(stringResource(Res.string.discord_preview_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                if (preview.renderedContent.isNotBlank()) {
                    Text(text = preview.renderedContent, style = typography.sm, color = tokens.cardForeground)
                }
                preview.pingRoleMention?.let {
                    Spacer(Modifier.height(spacing.s1))
                    Text(
                        text = stringResource(Res.string.discord_preview_ping_label, it),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.discord_preview_close), color = tokens.primary)
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

// Pending role-delete confirm target.
private data class PendingRoleDelete(val roleId: String, val displayName: String)
