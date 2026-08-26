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
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
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
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.DiscordConfigPreview
import bot.nomnomz.dashboard.core.network.DiscordDispatchLogEntry
import bot.nomnomz.dashboard.core.network.DiscordEmbed
import bot.nomnomz.dashboard.core.network.DiscordGuildChannel
import bot.nomnomz.dashboard.core.network.DiscordGuildConnection
import bot.nomnomz.dashboard.core.network.DiscordGuildRole
import bot.nomnomz.dashboard.core.network.DiscordNotificationConfig
import bot.nomnomz.dashboard.core.network.DiscordNotificationRole
import bot.nomnomz.dashboard.feature.discord.state.DiscordController
import bot.nomnomz.dashboard.feature.discord.state.DiscordState
import bot.nomnomz.dashboard.feature.discord.state.FieldUpdate
import bot.nomnomz.dashboard.feature.discord.state.GuildNotifications
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.discord_action_error
import nomnomzbot.composeapp.generated.resources.discord_channel_category_none
import nomnomzbot.composeapp.generated.resources.discord_channel_not_postable
import nomnomzbot.composeapp.generated.resources.discord_channel_type_announcement
import nomnomzbot.composeapp.generated.resources.discord_channel_type_category
import nomnomzbot.composeapp.generated.resources.discord_channel_type_forum
import nomnomzbot.composeapp.generated.resources.discord_channel_type_other
import nomnomzbot.composeapp.generated.resources.discord_channel_type_stage
import nomnomzbot.composeapp.generated.resources.discord_channel_type_text
import nomnomzbot.composeapp.generated.resources.discord_channel_type_thread
import nomnomzbot.composeapp.generated.resources.discord_channel_type_voice
import nomnomzbot.composeapp.generated.resources.discord_config_load_error
import nomnomzbot.composeapp.generated.resources.discord_dialog_channel_hint
import nomnomzbot.composeapp.generated.resources.discord_dialog_channel_picker
import nomnomzbot.composeapp.generated.resources.discord_dialog_embed_description_label
import nomnomzbot.composeapp.generated.resources.discord_dialog_embed_title_label
import nomnomzbot.composeapp.generated.resources.discord_dialog_helper_hint
import nomnomzbot.composeapp.generated.resources.discord_dialog_ping_role_label
import nomnomzbot.composeapp.generated.resources.discord_dialog_ping_role_none
import nomnomzbot.composeapp.generated.resources.discord_dialog_trigger_hint
import nomnomzbot.composeapp.generated.resources.discord_dialog_trigger_locked_hint
import nomnomzbot.composeapp.generated.resources.discord_picker_empty_channels
import nomnomzbot.composeapp.generated.resources.discord_picker_empty_roles
import nomnomzbot.composeapp.generated.resources.discord_picker_error
import nomnomzbot.composeapp.generated.resources.discord_picker_loading
import nomnomzbot.composeapp.generated.resources.discord_picker_retry
import nomnomzbot.composeapp.generated.resources.discord_role_row_type
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
import nomnomzbot.composeapp.generated.resources.discord_roles_channel_picker
import nomnomzbot.composeapp.generated.resources.discord_roles_button_post
import nomnomzbot.composeapp.generated.resources.discord_roles_cancel
import nomnomzbot.composeapp.generated.resources.discord_roles_create
import nomnomzbot.composeapp.generated.resources.discord_roles_create_title
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_action
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_cancel
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_confirm
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_message
import nomnomzbot.composeapp.generated.resources.discord_roles_delete_title
import nomnomzbot.composeapp.generated.resources.discord_roles_dm_badge
import nomnomzbot.composeapp.generated.resources.discord_roles_dm_hint
import nomnomzbot.composeapp.generated.resources.discord_roles_dm_label
import nomnomzbot.composeapp.generated.resources.discord_roles_edit_action
import nomnomzbot.composeapp.generated.resources.discord_roles_edit_short
import nomnomzbot.composeapp.generated.resources.discord_roles_edit_title
import nomnomzbot.composeapp.generated.resources.discord_roles_save
import nomnomzbot.composeapp.generated.resources.discord_roles_role_picker
import nomnomzbot.composeapp.generated.resources.discord_roles_empty
import nomnomzbot.composeapp.generated.resources.discord_roles_opt_in_count
import nomnomzbot.composeapp.generated.resources.discord_roles_post_button
import nomnomzbot.composeapp.generated.resources.discord_roles_post_button_title
import nomnomzbot.composeapp.generated.resources.discord_roles_role_id_required
import nomnomzbot.composeapp.generated.resources.discord_roles_role_name
import nomnomzbot.composeapp.generated.resources.discord_roles_self_assign
import nomnomzbot.composeapp.generated.resources.discord_roles_title
import nomnomzbot.composeapp.generated.resources.discord_streamer_enabled_hint
import nomnomzbot.composeapp.generated.resources.discord_streamer_enabled_label
import nomnomzbot.composeapp.generated.resources.discord_rule_channel
import nomnomzbot.composeapp.generated.resources.discord_rule_no_message
import nomnomzbot.composeapp.generated.resources.discord_toggle_action
import nomnomzbot.composeapp.generated.resources.shell_nav_discord
import org.jetbrains.compose.resources.stringResource

// A live-from-Discord picker's honest load state. [Loading] and [Error] are distinct from a genuinely empty
// [Loaded] list — collapsing a failed fetch into an empty list would read as "this server has no channels",
// sending the operator hunting a bug that isn't there. See CLAUDE.md's honest-states rule for this slice.
private sealed interface PickerState<out T> {
    data object Loading : PickerState<Nothing>
    data class Error(val detail: String) : PickerState<Nothing>
    data class Loaded<T>(val value: T) : PickerState<T>
}

private fun <T> ApiResult<T>.toPickerState(): PickerState<T> =
    when (this) {
        is ApiResult.Ok -> PickerState.Loaded(value)
        is ApiResult.Failure -> PickerState.Error(error.message)
    }

// Discord channel type constants the picker cares about (Discord's numeric channel types).
private const val ChannelTypeText = 0
private const val ChannelTypeVoice = 2
private const val ChannelTypeCategory = 4
private const val ChannelTypeAnnouncement = 5
private const val ChannelTypeStage = 13
private const val ChannelTypeForum = 15

// A channel can host the bot's post only if it is a text-like channel (regular text or announcement). Every
// other type — voice, category, stage, forum, thread — is shown in the picker but disabled with a reason,
// per the slice's "unselectable with a reason, never hidden" rule.
private fun DiscordGuildChannel.isPostable(): Boolean = type == ChannelTypeText || type == ChannelTypeAnnouncement

@Composable
private fun channelTypeLabel(type: Int): String {
    val resource =
        when (type) {
            ChannelTypeText -> Res.string.discord_channel_type_text
            ChannelTypeAnnouncement -> Res.string.discord_channel_type_announcement
            ChannelTypeVoice -> Res.string.discord_channel_type_voice
            ChannelTypeCategory -> Res.string.discord_channel_type_category
            ChannelTypeForum -> Res.string.discord_channel_type_forum
            ChannelTypeStage -> Res.string.discord_channel_type_stage
            in 10..12 -> Res.string.discord_channel_type_thread
            else -> Res.string.discord_channel_type_other
        }
    return stringResource(resource)
}

// The category (parent) name for a channel, resolved from the same channel list — Discord channels carry a
// [DiscordGuildChannel.parentId] pointing at a category channel (type 4). Distinguishes "#general" in two
// categories, per the slice.
private fun categoryNameFor(channel: DiscordGuildChannel, allChannels: List<DiscordGuildChannel>): String? =
    channel.parentId?.let { parentId -> allChannels.firstOrNull { it.id == parentId }?.name }

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
    var pendingRoleEdit: DiscordNotificationRole? by remember { mutableStateOf(null) }
    var pendingRoleDelete: PendingRoleDelete? by remember { mutableStateOf(null) }
    var pendingPostButton: PendingPostButton? by remember { mutableStateOf(null) }  // roleId + its guild connection
    var preview: DiscordConfigPreview? by remember { mutableStateOf(null) }
    // Bumped after a role create/edit/delete completes so each guild card re-fetches its self-assign roles — the
    // roles are loaded per card on connection.id, which a role write never changes, leaving the card stale.
    var rolesVersion: Int by remember { mutableStateOf(0) }

    // The live Discord channel/role lists for every linked guild, keyed by connection id — loaded once the
    // guild list is known so the rule dialog, the role dialogs and the rule list's channel-name resolution all
    // share one honest (Loading / Error / Loaded) fetch instead of each re-fetching and re-guessing on failure.
    var channelsByConnection: Map<String, PickerState<List<DiscordGuildChannel>>> by
        remember { mutableStateOf(emptyMap()) }
    var guildRolesByConnection: Map<String, PickerState<List<DiscordGuildRole>>> by
        remember { mutableStateOf(emptyMap()) }

    LaunchedEffect(Unit) { controller.load() }

    val readyGuildIds: List<String> = (state as? DiscordState.Ready)?.guilds?.map { it.connection.id }.orEmpty()
    LaunchedEffect(readyGuildIds) {
        readyGuildIds.forEach { connectionId ->
            channelsByConnection = channelsByConnection + (connectionId to PickerState.Loading)
            channelsByConnection =
                channelsByConnection + (connectionId to controller.guildChannels(connectionId).toPickerState())
        }
    }
    LaunchedEffect(readyGuildIds) {
        readyGuildIds.forEach { connectionId ->
            guildRolesByConnection = guildRolesByConnection + (connectionId to PickerState.Loading)
            guildRolesByConnection =
                guildRolesByConnection + (connectionId to controller.guildRoles(connectionId).toPickerState())
        }
    }

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
                    rolesVersion = rolesVersion,
                    channelsByConnection = channelsByConnection,
                    onNewRule = { connectionId -> editor = RuleEditor.create(connectionId) },
                    onEditRule = { connectionId, rule ->
                        editor =
                            RuleEditor.edit(
                                connectionId = connectionId,
                                configId = rule.id,
                                triggerType = rule.triggerType,
                                targetChannelId = rule.targetChannelId,
                                message = rule.messageTemplate.orEmpty(),
                                pingRoleId = rule.pingRoleId,
                                embedTitle = rule.embedConfig?.title.orEmpty(),
                                embedDescription = rule.embedConfig?.description.orEmpty(),
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
                    onSetStreamerEnabled = { connectionId, enabled ->
                        scope.launch { controller.setStreamerEnabled(connectionId, enabled) }
                    },
                    onAddRole = { connectionId -> pendingRoleCreate = connectionId },
                    onEditRole = { role -> pendingRoleEdit = role },
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
            channels = channelsByConnection[open.connectionId] ?: PickerState.Loading,
            roles = guildRolesByConnection[open.connectionId] ?: PickerState.Loading,
            onRetryChannels = {
                scope.launch {
                    channelsByConnection =
                        channelsByConnection + (open.connectionId to PickerState.Loading)
                    channelsByConnection =
                        channelsByConnection +
                            (open.connectionId to controller.guildChannels(open.connectionId).toPickerState())
                }
            },
            onRetryRoles = {
                scope.launch {
                    guildRolesByConnection =
                        guildRolesByConnection + (open.connectionId to PickerState.Loading)
                    guildRolesByConnection =
                        guildRolesByConnection +
                            (open.connectionId to controller.guildRoles(open.connectionId).toPickerState())
                }
            },
            onDismiss = { editor = null },
            onSubmit = { triggerType, channelId, message, pingRoleId, embedTitle, embedDescription, enabled ->
                editor = null
                val embed: DiscordEmbed? =
                    if (embedTitle.isBlank() && embedDescription.isBlank()) null
                    else DiscordEmbed(
                        title = embedTitle.ifBlank { null },
                        description = embedDescription.ifBlank { null },
                    )
                scope.launch {
                    if (open.isEdit) {
                        controller.updateConfig(
                            open.configId,
                            channelId,
                            message,
                            enabled,
                            pingRoleId = FieldUpdate.Value(pingRoleId),
                            embedConfig = FieldUpdate.Value(embed),
                        )
                    } else {
                        controller.createConfig(
                            open.connectionId,
                            triggerType,
                            channelId,
                            message,
                            enabled,
                            pingRoleId = pingRoleId,
                            embedConfig = embed,
                        )
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
            onCreate = { discordRoleId, roleName, selfAssign, dmEnabled ->
                pendingRoleCreate = null
                scope.launch {
                    controller.createRole(connectionId, discordRoleId, roleName, selfAssign, dmEnabled)
                    rolesVersion++
                }
            },
        )
    }

    pendingRoleEdit?.let { role ->
        EditRoleDialog(
            role = role,
            onDismiss = { pendingRoleEdit = null },
            onSave = { roleName, selfAssign, dmEnabled ->
                pendingRoleEdit = null
                scope.launch {
                    controller.updateRole(role.id, roleName, selfAssign, dmEnabled)
                    rolesVersion++
                }
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
                scope.launch {
                    controller.deleteRole(target.roleId)
                    rolesVersion++
                }
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
    rolesVersion: Int,
    channelsByConnection: Map<String, PickerState<List<DiscordGuildChannel>>>,
    onNewRule: (connectionId: String) -> Unit,
    onEditRule: (connectionId: String, DiscordNotificationConfig) -> Unit,
    onToggleRule: (DiscordNotificationConfig, Boolean) -> Unit,
    onDeleteRule: (DiscordNotificationConfig) -> Unit,
    onPreviewRule: (DiscordNotificationConfig) -> Unit,
    onApproveConsent: (connectionId: String) -> Unit,
    onRevokeConsent: (connectionId: String) -> Unit,
    onSetStreamerEnabled: (connectionId: String, enabled: Boolean) -> Unit,
    onAddRole: (connectionId: String) -> Unit,
    onEditRole: (DiscordNotificationRole) -> Unit,
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
                    rolesVersion = rolesVersion,
                    channels = channelsByConnection[guild.connection.id] ?: PickerState.Loading,
                    onNewRule = { onNewRule(guild.connection.id) },
                    onEditRule = { rule -> onEditRule(guild.connection.id, rule) },
                    onToggleRule = onToggleRule,
                    onDeleteRule = onDeleteRule,
                    onPreviewRule = onPreviewRule,
                    onApproveConsent = { onApproveConsent(guild.connection.id) },
                    onRevokeConsent = { onRevokeConsent(guild.connection.id) },
                    onSetStreamerEnabled = { enabled -> onSetStreamerEnabled(guild.connection.id, enabled) },
                    onAddRole = { onAddRole(guild.connection.id) },
                    onEditRole = onEditRole,
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
    // Bumped by the parent after a role create/edit/delete so this card re-fetches its self-assign roles.
    rolesVersion: Int,
    channels: PickerState<List<DiscordGuildChannel>>,
    onNewRule: () -> Unit,
    onEditRule: (DiscordNotificationConfig) -> Unit,
    onToggleRule: (DiscordNotificationConfig, Boolean) -> Unit,
    onDeleteRule: (DiscordNotificationConfig) -> Unit,
    onPreviewRule: (DiscordNotificationConfig) -> Unit,
    onApproveConsent: () -> Unit,
    onRevokeConsent: () -> Unit,
    onSetStreamerEnabled: (enabled: Boolean) -> Unit,
    onAddRole: () -> Unit,
    onEditRole: (DiscordNotificationRole) -> Unit,
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

    // Load roles when the card first composes, and re-load whenever a role create/edit/delete bumps
    // [rolesVersion] — otherwise the card, keyed only on the (unchanging) connection id, stayed stale after a write.
    LaunchedEffect(guild.connection.id, rolesVersion) {
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
                onSetStreamerEnabled = onSetStreamerEnabled,
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
                // Resolve each rule's target channel id to its real Discord name (the slice's "no snowflake in
                // the list" requirement) — a lookup built from the same guild-channels fetch used by the picker.
                val channelNames: Map<String, String> =
                    (channels as? PickerState.Loaded)?.value?.associate { it.id to (it.name ?: it.id) }.orEmpty()
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        guild.configs.forEachIndexed { index, rule ->
                            RuleRow(
                                rule = rule,
                                channelName = channelNames[rule.targetChannelId] ?: rule.targetChannelId,
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
                onEdit = onEditRole,
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
    onSetStreamerEnabled: (enabled: Boolean) -> Unit,
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
        // The streamer-side master switch — the both-opt-in handshake's streamer half. Nothing posts to
        // Discord until this is on, so it reads as the guild's primary control with an explanatory hint.
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = stringResource(Res.string.discord_streamer_enabled_label),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(Res.string.discord_streamer_enabled_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            ManageGate(decision = manage) { enabled ->
                Switch(
                    checked = connection.streamerEnabled,
                    onCheckedChange = { onSetStreamerEnabled(it) },
                    enabled = enabled,
                )
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
                        icon = TrashGlyph,
                        label = stringResource(Res.string.discord_consent_revoke),
                        onClick = onRevokeConsent,
                        enabled = enabled,
                        tint = tokens.destructive,
                    )
                } else {
                    GlyphButton(
                        icon = CheckCircleGlyph,
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
    channelName: String,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
    onPreview: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val channelLine: String = stringResource(Res.string.discord_rule_channel, "#$channelName")
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
        TextButton(onClick = onPreview) {
            Text(
                text = stringResource(Res.string.discord_preview_action),
                color = tokens.mutedForeground,
                maxLines = 1,
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
    }
}

// Curated well-known EventSub triggers (CLAUDE.md's Twitch EventSub topic catalogue) offered in the trigger
// dropdown — real display names, never a bare technical string the operator has to guess at.
private val KnownTriggers: List<Pair<String, String>> =
    listOf(
        "stream.online" to "Stream goes live",
        "stream.offline" to "Stream ends",
        "channel.follow" to "New follower",
        "channel.subscribe" to "New subscriber",
        "channel.subscription.gift" to "Gifted sub",
        "channel.cheer" to "Cheer / bits",
        "channel.raid" to "Incoming raid",
        "channel.poll.begin" to "Poll starts",
        "channel.prediction.begin" to "Prediction starts",
    )

private fun triggerLabel(triggerType: String): String =
    KnownTriggers.firstOrNull { it.first == triggerType }?.second ?: triggerType

// One composable for both create and edit (DRY): a [RuleEditor] without a config id = create (the trigger +
// channel are editable), with one = edit (the trigger is read-only — the backend treats it as immutable on the
// row). The affirmative button is disabled until the trigger, channel and message are all non-blank. Channel
// and ping-role are real Discord pickers (never a typed snowflake) fed by the guild's live channel/role lists,
// which are shown honestly: loading, an unreachable-Discord error with retry, or the resolved list (possibly
// genuinely empty) — three visibly different states, per the slice.
@Composable
private fun RuleFormDialog(
    editor: RuleEditor,
    channels: PickerState<List<DiscordGuildChannel>>,
    roles: PickerState<List<DiscordGuildRole>>,
    onRetryChannels: () -> Unit,
    onRetryRoles: () -> Unit,
    onDismiss: () -> Unit,
    onSubmit: (
        triggerType: String,
        channelId: String,
        message: String,
        pingRoleId: String?,
        embedTitle: String,
        embedDescription: String,
        enabled: Boolean,
    ) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var triggerType: String by remember { mutableStateOf(editor.triggerType) }
    var channelId: String by remember { mutableStateOf(editor.targetChannelId) }
    var message: String by remember { mutableStateOf(editor.message) }
    var pingRoleId: String? by remember { mutableStateOf(editor.pingRoleId) }
    var embedTitle: String by remember { mutableStateOf(editor.embedTitle) }
    var embedDescription: String by remember { mutableStateOf(editor.embedDescription) }
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
                Text(
                    text = stringResource(Res.string.discord_dialog_trigger_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                if (editor.isEdit) {
                    // Immutable on the row — shown as its friendly label, not the raw snowflake-like string,
                    // with an explanation of why it can't be changed here.
                    Text(text = triggerLabel(triggerType), style = typography.base, color = tokens.cardForeground)
                    Text(
                        text = stringResource(Res.string.discord_dialog_trigger_locked_hint),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                } else {
                    GuildPickerField(
                        label = stringResource(Res.string.discord_dialog_trigger_label),
                        options = KnownTriggers,
                        selectedId = triggerType,
                        onSelect = { triggerType = it },
                    )
                }

                Text(
                    text = stringResource(Res.string.discord_dialog_channel_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                ChannelPickerField(
                    label = stringResource(Res.string.discord_dialog_channel_picker),
                    channels = channels,
                    selectedId = channelId,
                    onSelect = { channelId = it },
                    onRetry = onRetryChannels,
                )

                AppTextField(
                    value = message,
                    onValueChange = { message = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.discord_dialog_message_label),
                )
                Text(
                    text = stringResource(Res.string.discord_dialog_helper_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )

                RolePickerField(
                    label = stringResource(Res.string.discord_dialog_ping_role_label),
                    roles = roles,
                    selectedId = pingRoleId,
                    onSelect = { pingRoleId = it },
                    onRetry = onRetryRoles,
                )

                AppTextField(
                    value = embedTitle,
                    onValueChange = { embedTitle = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.discord_dialog_embed_title_label),
                )
                AppTextField(
                    value = embedDescription,
                    onValueChange = { embedDescription = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.discord_dialog_embed_description_label),
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
                onClick = {
                    onSubmit(triggerType, channelId, message, pingRoleId, embedTitle, embedDescription, enabled)
                },
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

// The channel picker: real Discord channel names with their type badge and category, backing the rule dialog's
// and the post-button dialog's channel field. A channel the bot can't post to (not a text/announcement type)
// stays visible but disabled, labelled with the reason — hiding it would look like a missing channel bug.
// [channels] carries the three honest load states: Loading / Error(detail, with retry) / Loaded(list, possibly
// genuinely empty).
@Composable
private fun ChannelPickerField(
    label: String,
    channels: PickerState<List<DiscordGuildChannel>>,
    selectedId: String,
    onSelect: (String) -> Unit,
    onRetry: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        when (channels) {
            is PickerState.Loading ->
                Text(
                    text = stringResource(Res.string.discord_picker_loading),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            is PickerState.Error ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Text(
                        text = stringResource(Res.string.discord_picker_error, channels.detail),
                        style = typography.sm,
                        color = tokens.destructiveForeground,
                        modifier = Modifier.weight(1f),
                    )
                    TextButton(onClick = onRetry) {
                        Text(text = stringResource(Res.string.discord_picker_retry), color = tokens.primary)
                    }
                }
            is PickerState.Loaded ->
                if (channels.value.isEmpty()) {
                    Text(
                        text = stringResource(Res.string.discord_picker_empty_channels),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                } else {
                    var expanded: Boolean by remember { mutableStateOf(false) }
                    val selected: DiscordGuildChannel? = channels.value.firstOrNull { it.id == selectedId }
                    val selectedLabel: String? = selected?.let { "#" + (it.name ?: it.id) }

                    Box {
                        TextButton(onClick = { expanded = true }) {
                            Text(
                                text = selectedLabel ?: label,
                                color = if (selectedLabel != null) tokens.cardForeground else tokens.mutedForeground,
                            )
                        }
                        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                            channels.value.sortedBy { it.position }.forEach { channel ->
                                val postable: Boolean = channel.isPostable()
                                val category: String? = categoryNameFor(channel, channels.value)
                                val typeText: String = channelTypeLabel(channel.type)
                                val categoryText: String =
                                    category ?: stringResource(Res.string.discord_channel_category_none)
                                DropdownMenuItem(
                                    text = {
                                        Column {
                                            Text(
                                                text = "#" + (channel.name ?: channel.id),
                                                style = typography.sm,
                                                color = if (postable) tokens.cardForeground else tokens.mutedForeground,
                                            )
                                            Text(
                                                text = "$typeText · $categoryText" +
                                                    if (!postable) " · " + stringResource(Res.string.discord_channel_not_postable) else "",
                                                style = typography.xs,
                                                color = tokens.mutedForeground,
                                            )
                                        }
                                    },
                                    enabled = postable,
                                    onClick = {
                                        onSelect(channel.id)
                                        expanded = false
                                    },
                                )
                            }
                        }
                    }
                }
        }
    }
}

// The optional ping-role picker: real Discord role names with their colour swatch, plus a "no ping" option.
// Same three honest states as [ChannelPickerField].
@Composable
private fun RolePickerField(
    label: String,
    roles: PickerState<List<DiscordGuildRole>>,
    selectedId: String?,
    onSelect: (String?) -> Unit,
    onRetry: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        when (roles) {
            is PickerState.Loading ->
                Text(
                    text = stringResource(Res.string.discord_picker_loading),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            is PickerState.Error ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Text(
                        text = stringResource(Res.string.discord_picker_error, roles.detail),
                        style = typography.sm,
                        color = tokens.destructiveForeground,
                        modifier = Modifier.weight(1f),
                    )
                    TextButton(onClick = onRetry) {
                        Text(text = stringResource(Res.string.discord_picker_retry), color = tokens.primary)
                    }
                }
            is PickerState.Loaded ->
                if (roles.value.isEmpty()) {
                    Text(
                        text = stringResource(Res.string.discord_picker_empty_roles),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                } else {
                    var expanded: Boolean by remember { mutableStateOf(false) }
                    val noPingLabel: String = stringResource(Res.string.discord_dialog_ping_role_none)
                    val roleTypeLabel: String = stringResource(Res.string.discord_role_row_type)
                    val selectedRole: DiscordGuildRole? = roles.value.firstOrNull { it.id == selectedId }
                    val selectedLabel: String =
                        selectedRole?.let {
                            resolveRowLabel(
                                primary = it.name,
                                typeLabel = roleTypeLabel,
                                discriminatorSource = it.id,
                            )
                        } ?: noPingLabel

                    Box {
                        TextButton(onClick = { expanded = true }) {
                            Text(text = selectedLabel, color = tokens.cardForeground)
                        }
                        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                            DropdownMenuItem(
                                text = { Text(text = noPingLabel, style = typography.sm, color = tokens.cardForeground) },
                                onClick = {
                                    onSelect(null)
                                    expanded = false
                                },
                            )
                            roles.value.sortedByDescending { it.position }.forEach { role ->
                                val roleLabel: String =
                                    resolveRowLabel(
                                        primary = role.name,
                                        typeLabel = roleTypeLabel,
                                        discriminatorSource = role.id,
                                    )
                                DropdownMenuItem(
                                    text = {
                                        Row(
                                            verticalAlignment = Alignment.CenterVertically,
                                            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                                        ) {
                                            // The role's own Discord colour (per-role data, like the user's Twitch
                                            // chat colour elsewhere in the app) — not a design-system token.
                                            Box(
                                                modifier = Modifier
                                                    .size(spacing.s2)
                                                    .clip(CircleShape)
                                                    .background(Color(role.color).copy(alpha = 1f))
                                            )
                                            Text(text = roleLabel, style = typography.sm, color = tokens.cardForeground)
                                        }
                                    },
                                    onClick = {
                                        onSelect(role.id)
                                        expanded = false
                                    },
                                )
                            }
                        }
                    }
                }
        }
    }
}

// ── Roles section ────────────────────────────────────────────────────────────

@Composable
private fun RolesSection(
    roles: List<DiscordNotificationRole>?,
    loading: Boolean,
    manage: ManageDecision,
    onAdd: () -> Unit,
    onEdit: (DiscordNotificationRole) -> Unit,
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
                            RoleRow(
                                role = role,
                                manage = manage,
                                onEdit = onEdit,
                                onDelete = onDelete,
                                onPostButton = onPostButton,
                            )
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
    onEdit: (DiscordNotificationRole) -> Unit,
    onDelete: (DiscordNotificationRole) -> Unit,
    onPostButton: (DiscordNotificationRole) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val displayName: String = role.roleName?.takeIf { it.isNotBlank() } ?: role.discordRoleId
    val deleteLabel: String = stringResource(Res.string.discord_roles_delete_action, displayName)
    val editLabel: String = stringResource(Res.string.discord_roles_edit_action, displayName)

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
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(Res.string.discord_roles_opt_in_count, role.optInCount),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                if (role.dmEnabled) {
                    Text(
                        text = stringResource(Res.string.discord_roles_dm_badge),
                        style = typography.xs,
                        color = tokens.primary,
                        maxLines = 1,
                    )
                }
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onEdit(role) },
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = editLabel },
            ) {
                Text(
                    text = stringResource(Res.string.discord_roles_edit_short),
                    style = typography.xs,
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
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
    onCreate: (discordRoleId: String, roleName: String?, selfAssign: Boolean, dmEnabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var discordRoleId: String by remember { mutableStateOf("") }
    var roleName: String by remember { mutableStateOf("") }
    var selfAssign: Boolean by remember { mutableStateOf(false) }
    var dmEnabled: Boolean by remember { mutableStateOf(false) }

    // The guild's assignable roles, so the operator picks instead of pasting a snowflake. Managed (bot/integration)
    // roles are filtered out — they can't be self-assigned. Loading/Error/Loaded are kept distinct so an upstream
    // failure (missing permission / bot not in the guild) never collapses to the same "no roles" look as a guild
    // that genuinely has none — there is no manual-id fallback, the operator can only pick or retry.
    var rolesState: PickerState<List<DiscordGuildRole>> by remember(connectionId) { mutableStateOf(PickerState.Loading) }
    suspend fun reload() {
        rolesState = PickerState.Loading
        rolesState = loadRoles(connectionId).toPickerState()
    }
    LaunchedEffect(connectionId) { reload() }
    val scope = rememberCoroutineScope()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.discord_roles_create_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                when (val state = rolesState) {
                    is PickerState.Loading -> CenteredMessage(stringResource(Res.string.discord_picker_loading))
                    is PickerState.Error ->
                        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            Text(
                                text = stringResource(Res.string.discord_error, state.detail),
                                style = LocalTypography.current.sm,
                                color = tokens.mutedForeground,
                            )
                            TextButton(onClick = { scope.launch { reload() } }) {
                                Text(text = stringResource(Res.string.discord_retry))
                            }
                        }
                    is PickerState.Loaded -> {
                        val assignableRoles: List<DiscordGuildRole> = state.value.filter { !it.managed }
                        if (assignableRoles.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.discord_picker_empty_roles),
                                style = LocalTypography.current.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            GuildPickerField(
                                label = stringResource(Res.string.discord_roles_role_picker),
                                options = assignableRoles.map { it.id to it.name },
                                selectedId = discordRoleId,
                                onSelect = { id ->
                                    discordRoleId = id
                                    // Seed the display name from the picked role unless the operator already typed one.
                                    if (roleName.isBlank()) {
                                        roleName = assignableRoles.firstOrNull { it.id == id }?.name.orEmpty()
                                    }
                                },
                            )
                        }
                    }
                }
                AppTextField(
                    value = roleName,
                    onValueChange = { roleName = it },
                    label = stringResource(Res.string.discord_roles_role_name),
                    isError = false,
                    errorText = null,
                    modifier = Modifier.fillMaxWidth(),
                )
                RoleToggleRow(
                    label = stringResource(Res.string.discord_roles_self_assign),
                    checked = selfAssign,
                    onCheckedChange = { selfAssign = it },
                )
                RoleToggleRow(
                    label = stringResource(Res.string.discord_roles_dm_label),
                    hint = stringResource(Res.string.discord_roles_dm_hint),
                    checked = dmEnabled,
                    onCheckedChange = { dmEnabled = it },
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onCreate(discordRoleId, roleName.ifBlank { null }, selfAssign, dmEnabled) },
                enabled = discordRoleId.isNotBlank(),
            ) {
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

// A labelled switch row used inside the role dialogs, with an optional hint line under the label.
@Composable
private fun RoleToggleRow(
    label: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    hint: String? = null,
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
            Text(label, color = tokens.cardForeground)
            hint?.let { Text(it, style = typography.xs, color = tokens.mutedForeground) }
        }
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}

// Edit an existing notification role — the guild role id is immutable, so only the display name, self-assign
// and DM-on-live flags are editable (mirrors the whole-row backend PUT). Seeded from the current row.
@Composable
private fun EditRoleDialog(
    role: DiscordNotificationRole,
    onDismiss: () -> Unit,
    onSave: (roleName: String?, selfAssign: Boolean, dmEnabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var roleName: String by remember(role.id) { mutableStateOf(role.roleName.orEmpty()) }
    var selfAssign: Boolean by remember(role.id) { mutableStateOf(role.selfAssignEnabled) }
    var dmEnabled: Boolean by remember(role.id) { mutableStateOf(role.dmEnabled) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.discord_roles_edit_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = roleName,
                    onValueChange = { roleName = it },
                    label = stringResource(Res.string.discord_roles_role_name),
                    isError = false,
                    errorText = null,
                    modifier = Modifier.fillMaxWidth(),
                )
                RoleToggleRow(
                    label = stringResource(Res.string.discord_roles_self_assign),
                    checked = selfAssign,
                    onCheckedChange = { selfAssign = it },
                )
                RoleToggleRow(
                    label = stringResource(Res.string.discord_roles_dm_label),
                    hint = stringResource(Res.string.discord_roles_dm_hint),
                    checked = dmEnabled,
                    onCheckedChange = { dmEnabled = it },
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onSave(roleName.ifBlank { null }, selfAssign, dmEnabled) }) {
                Text(text = stringResource(Res.string.discord_roles_save), color = tokens.primary)
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

    // The guild's TEXT channels (type 0) — the button can only be posted to a text channel. Loading/Error/Loaded
    // are kept distinct so an upstream failure never collapses to the same "no channels" look as a guild that
    // genuinely has none — there is no manual channel-id fallback, the operator can only pick or retry.
    var channelsState: PickerState<List<DiscordGuildChannel>> by
        remember(connectionId) { mutableStateOf(PickerState.Loading) }
    suspend fun reload() {
        channelsState = PickerState.Loading
        channelsState = loadChannels(connectionId).toPickerState()
    }
    LaunchedEffect(connectionId) { reload() }
    val scope = rememberCoroutineScope()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.discord_roles_post_button_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                when (val state = channelsState) {
                    is PickerState.Loading -> CenteredMessage(stringResource(Res.string.discord_picker_loading))
                    is PickerState.Error ->
                        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            Text(
                                text = stringResource(Res.string.discord_error, state.detail),
                                style = LocalTypography.current.sm,
                                color = tokens.mutedForeground,
                            )
                            TextButton(onClick = { scope.launch { reload() } }) {
                                Text(text = stringResource(Res.string.discord_retry))
                            }
                        }
                    is PickerState.Loaded -> {
                        val textChannels: List<DiscordGuildChannel> = state.value.filter { it.type == 0 }
                        if (textChannels.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.discord_picker_empty_channels),
                                style = LocalTypography.current.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            GuildPickerField(
                                label = stringResource(Res.string.discord_roles_channel_picker),
                                options = textChannels.map { it.id to ("# " + (it.name ?: it.id)) },
                                selectedId = channelId,
                                onSelect = { channelId = it },
                            )
                        }
                    }
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
    val pingRoleId: String?,
    val embedTitle: String,
    val embedDescription: String,
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
                pingRoleId = null,
                embedTitle = "",
                embedDescription = "",
                enabled = true,
            )

        fun edit(
            connectionId: String,
            configId: String,
            triggerType: String,
            targetChannelId: String,
            message: String,
            pingRoleId: String?,
            embedTitle: String,
            embedDescription: String,
            enabled: Boolean,
        ): RuleEditor =
            RuleEditor(
                isEdit = true,
                connectionId = connectionId,
                configId = configId,
                triggerType = triggerType,
                targetChannelId = targetChannelId,
                message = message,
                pingRoleId = pingRoleId,
                embedTitle = embedTitle,
                embedDescription = embedDescription,
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
