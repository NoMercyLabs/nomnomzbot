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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.DiscordConfigPreview
import bot.nomnomz.dashboard.core.network.DiscordDispatchLogEntry
import bot.nomnomz.dashboard.core.network.DiscordGuildChannel
import bot.nomnomz.dashboard.core.network.DiscordGuildConnection
import bot.nomnomz.dashboard.core.network.DiscordGuildRole
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
import nomnomzbot.composeapp.generated.resources.discord_consent_approve
import nomnomzbot.composeapp.generated.resources.discord_consent_approve_action
import nomnomzbot.composeapp.generated.resources.discord_consent_approve_title
import nomnomzbot.composeapp.generated.resources.discord_consent_approved
import nomnomzbot.composeapp.generated.resources.discord_consent_cancel
import nomnomzbot.composeapp.generated.resources.discord_consent_discord_user_id
import nomnomzbot.composeapp.generated.resources.discord_consent_pending
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_confirm
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_dismiss
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_message
import nomnomzbot.composeapp.generated.resources.discord_consent_revoke_title
import nomnomzbot.composeapp.generated.resources.discord_delete_action
import nomnomzbot.composeapp.generated.resources.discord_delete_action_short
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
import nomnomzbot.composeapp.generated.resources.discord_empty_body
import nomnomzbot.composeapp.generated.resources.discord_empty_title
import nomnomzbot.composeapp.generated.resources.discord_error
import nomnomzbot.composeapp.generated.resources.discord_guild_active
import nomnomzbot.composeapp.generated.resources.discord_guild_inactive
import nomnomzbot.composeapp.generated.resources.discord_guild_unnamed
import nomnomzbot.composeapp.generated.resources.discord_loading
import nomnomzbot.composeapp.generated.resources.discord_log_empty
import nomnomzbot.composeapp.generated.resources.discord_log_load
import nomnomzbot.composeapp.generated.resources.discord_log_loading
import nomnomzbot.composeapp.generated.resources.discord_log_title
import nomnomzbot.composeapp.generated.resources.discord_new_rule_action
import nomnomzbot.composeapp.generated.resources.discord_no_rules
import nomnomzbot.composeapp.generated.resources.discord_preview_action
import nomnomzbot.composeapp.generated.resources.discord_preview_close
import nomnomzbot.composeapp.generated.resources.discord_preview_ping_label
import nomnomzbot.composeapp.generated.resources.discord_preview_title
import nomnomzbot.composeapp.generated.resources.discord_retry
import nomnomzbot.composeapp.generated.resources.discord_roles_add
import nomnomzbot.composeapp.generated.resources.discord_roles_button_channel_id
import nomnomzbot.composeapp.generated.resources.discord_roles_channel_picker
import nomnomzbot.composeapp.generated.resources.discord_roles_button_channel_required
import nomnomzbot.composeapp.generated.resources.discord_roles_button_post
import nomnomzbot.composeapp.generated.resources.discord_roles_cancel
import nomnomzbot.composeapp.generated.resources.discord_roles_create
import nomnomzbot.composeapp.generated.resources.discord_roles_create_title
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_action
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_cancel
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_confirm
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_message
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_title
import nomnomzbot.composeapp.generated.resources.discord_roles_discord_role_id
import nomnomzbot.composeapp.generated.resources.discord_roles_role_picker
import nomnomzbot.composeapp.generated.resources.discord_roles_empty
import nomnomzbot.composeapp.generated.resources.discord_roles_opt_in_count
import nomnomzbot.composeapp.generated.resources.discord_roles_post_button
import nomnomzbot.composeapp.generated.resources.discord_roles_post_button_title
import nomnomzbot.composeapp.generated.resources.discord_roles_role_id_required
import nomnomzbot.composeapp.generated.resources.discord_roles_role_name
import nomnomzbot.composeapp.generated.resources.discord_roles_self_assign
import nomnomzbot.composeapp.generated.resources.discord_roles_title
import nomnomzbot.composeapp.generated.resources.discord_rule_channel
import nomnomzbot.composeapp.generated.resources.discord_rule_no_message
import nomnomzbot.composeapp.generated.resources.discord_toggle_action
import nomnomzbot.composeapp.generated.resources.shell_nav_discord
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
    var pendingPostButton: PendingPostButton? by remember { mutableStateOf(null) }  // roleId + its guild connection
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
                    onPostRoleButton = { role ->
                        pendingPostButton = PendingPostButton(role.id, role.guildConnectionId)
                    },
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
            connectionId = connectionId,
            loadRoles = { cid -> controller.guildRoles(cid) },
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

    pendingPostButton?.let { target ->
        PostButtonDialog(
            connectionId = target.connectionId,
            loadChannels = { cid -> controller.guildChannels(cid) },
            onDismiss = { pendingPostButton = null },
            onPost = { channelId ->
                pendingPostButton = null
                scope.launch { controller.postRoleButton(target.roleId, channelId) }
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
            itemsIndexed(items = guilds, key = { _, guild -> guild.connection.id }) { _, guild ->
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

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
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
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        guild.configs.forEachIndexed { index, rule ->
                            RuleRow(
                                rule = rule,
                                manage = manage,
                                onEdit = { onEditRule(rule) },
                                onToggle = { enabled -> onToggleRule(rule, enabled) },
                                onDelete = { onDeleteRule(rule) },
                                onPreview = { onPreviewRule(rule) },
                            )
                            if (index < guild.configs.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }

            // ── Notification roles ─────────────────────────────────────────────
            Separator()
            RolesSection(
                roles = roles,
                loading = rolesLoading,
                manage = manage,
                onAdd = onAddRole,
                onDelete = onDeleteRole,
                onPostButton = onPostRoleButton,
            )

            // ── Dispatch log (load on demand) ──────────────────────────────────
            Separator()
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
                    GlyphButton(
                        imageVector = RemoveGlyph,
                        label = stringResource(Res.string.discord_consent_revoke),
                        onClick = onRevokeConsent,
                        enabled = enabled,
                        tint = tokens.destructive,
                    )
                } else {
                    GlyphButton(
                        imageVector = CheckCircleGlyph,
                        label = stringResource(Res.string.discord_consent_approve),
                        onClick = onApproveConsent,
                        enabled = enabled,
                        tint = tokens.primary,
                    )
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
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
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
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
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
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = triggerType,
                    onValueChange = { triggerType = it },
                    enabled = !editor.isEdit,
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.discord_dialog_trigger_label),
                )
                AppTextField(
                    value = channelId,
                    onValueChange = { channelId = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.discord_dialog_channel_label),
                )
                AppTextField(
                    value = message,
                    onValueChange = { message = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.discord_dialog_message_label),
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
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        roles.forEachIndexed { index, role ->
                            RoleRow(role = role, manage = manage, onDelete = onDelete, onPostButton = onPostButton)
                            if (index < roles.lastIndex) {
                                Separator()
                            }
                        }
                    }
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
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
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
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        entries.forEachIndexed { index, entry ->
                            DispatchLogRow(entry = entry)
                            if (index < entries.lastIndex) {
                                Separator()
                            }
                        }
                    }
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
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
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
    connectionId: String,
    loadRoles: suspend (connectionId: String) -> ApiResult<List<DiscordGuildRole>>,
    onDismiss: () -> Unit,
    onCreate: (discordRoleId: String, roleName: String?, selfAssign: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var discordRoleId: String by remember { mutableStateOf("") }
    var roleName: String by remember { mutableStateOf("") }
    var selfAssign: Boolean by remember { mutableStateOf(false) }
    val roleIdError: Boolean = discordRoleId.isBlank()

    // The guild's assignable roles, so the operator picks instead of pasting a snowflake. Managed (bot/integration)
    // roles are filtered out — they can't be self-assigned. Empty until loaded, or when the fetch fails (missing
    // permission / bot not in the guild) — in which case the dialog falls back to manual id entry.
    var guildRoles: List<DiscordGuildRole> by remember(connectionId) { mutableStateOf(emptyList()) }
    LaunchedEffect(connectionId) {
        guildRoles =
            when (val result: ApiResult<List<DiscordGuildRole>> = loadRoles(connectionId)) {
                is ApiResult.Ok -> result.value.filter { !it.managed }
                is ApiResult.Failure -> emptyList()
            }
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.discord_roles_create_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                if (guildRoles.isNotEmpty()) {
                    GuildPickerField(
                        label = stringResource(Res.string.discord_roles_role_picker),
                        options = guildRoles.map { it.id to it.name },
                        selectedId = discordRoleId,
                        onSelect = { id ->
                            discordRoleId = id
                            // Seed the display name from the picked role unless the operator already typed one.
                            if (roleName.isBlank()) {
                                roleName = guildRoles.firstOrNull { it.id == id }?.name.orEmpty()
                            }
                        },
                    )
                } else {
                    AppTextField(
                        value = discordRoleId,
                        onValueChange = { discordRoleId = it },
                        label = stringResource(Res.string.discord_roles_discord_role_id),
                        isError = roleIdError && discordRoleId.isNotEmpty(),
                        errorText = stringResource(Res.string.discord_roles_role_id_required),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
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
private fun PostButtonDialog(
    connectionId: String,
    loadChannels: suspend (connectionId: String) -> ApiResult<List<DiscordGuildChannel>>,
    onDismiss: () -> Unit,
    onPost: (channelId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var channelId: String by remember { mutableStateOf("") }
    val channelError: Boolean = channelId.isBlank()

    // The guild's TEXT channels (type 0) — the button can only be posted to a text channel. Empty until loaded,
    // or when the fetch fails — then the dialog falls back to manual channel-id entry.
    var textChannels: List<DiscordGuildChannel> by remember(connectionId) { mutableStateOf(emptyList()) }
    LaunchedEffect(connectionId) {
        textChannels =
            when (val result: ApiResult<List<DiscordGuildChannel>> = loadChannels(connectionId)) {
                is ApiResult.Ok -> result.value.filter { it.type == 0 }
                is ApiResult.Failure -> emptyList()
            }
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.discord_roles_post_button_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                if (textChannels.isNotEmpty()) {
                    GuildPickerField(
                        label = stringResource(Res.string.discord_roles_channel_picker),
                        options = textChannels.map { it.id to ("# " + (it.name ?: it.id)) },
                        selectedId = channelId,
                        onSelect = { channelId = it },
                    )
                } else {
                    AppTextField(
                        value = channelId,
                        onValueChange = { channelId = it },
                        label = stringResource(Res.string.discord_roles_button_channel_id),
                        isError = channelError && channelId.isNotEmpty(),
                        errorText = stringResource(Res.string.discord_roles_button_channel_required),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
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

// A labelled dropdown over guild [options] (id to display label) — the shared affordance behind the role and
// channel pickers. Shows the selected option's label, or the [label] prompt when nothing is picked yet.
@Composable
private fun GuildPickerField(
    label: String,
    options: List<Pair<String, String>>,
    selectedId: String,
    onSelect: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    val selectedLabel: String? = options.firstOrNull { it.first == selectedId }?.second

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        Box {
            TextButton(onClick = { expanded = true }) {
                Text(
                    text = selectedLabel ?: label,
                    color = if (selectedLabel != null) tokens.cardForeground else tokens.mutedForeground,
                )
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                options.forEach { (id, optionLabel) ->
                    DropdownMenuItem(
                        text = { Text(text = optionLabel, style = typography.sm, color = tokens.cardForeground) },
                        onClick = {
                            onSelect(id)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}

@Composable
private fun PreviewDialog(preview: DiscordConfigPreview, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
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

// The role whose opt-in button is being posted, plus its guild connection id — needed so the channel picker can
// fetch that guild's channels.
private data class PendingPostButton(val roleId: String, val connectionId: String)
