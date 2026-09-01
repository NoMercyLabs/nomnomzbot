// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.icon.DotsHorizontalGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.chat.state.MultiChatController
import bot.nomnomz.dashboard.feature.chat.state.MultiChatState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import androidx.compose.runtime.rememberCoroutineScope
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.multichat_ban_action
import nomnomzbot.composeapp.generated.resources.multichat_ban_confirm
import nomnomzbot.composeapp.generated.resources.multichat_ban_dismiss
import nomnomzbot.composeapp.generated.resources.multichat_ban_message
import nomnomzbot.composeapp.generated.resources.multichat_ban_title
import nomnomzbot.composeapp.generated.resources.multichat_composer_channel_label
import nomnomzbot.composeapp.generated.resources.multichat_composer_placeholder
import nomnomzbot.composeapp.generated.resources.multichat_composer_send
import nomnomzbot.composeapp.generated.resources.multichat_delete_action
import nomnomzbot.composeapp.generated.resources.multichat_delete_confirm
import nomnomzbot.composeapp.generated.resources.multichat_delete_dismiss
import nomnomzbot.composeapp.generated.resources.multichat_delete_message
import nomnomzbot.composeapp.generated.resources.multichat_delete_title
import nomnomzbot.composeapp.generated.resources.multichat_empty_feed
import nomnomzbot.composeapp.generated.resources.multichat_error
import nomnomzbot.composeapp.generated.resources.multichat_loading
import nomnomzbot.composeapp.generated.resources.multichat_picker_hint
import nomnomzbot.composeapp.generated.resources.multichat_picker_none
import nomnomzbot.composeapp.generated.resources.multichat_pick_a_channel
import nomnomzbot.composeapp.generated.resources.multichat_row_actions
import nomnomzbot.composeapp.generated.resources.multichat_timeout_action
import nomnomzbot.composeapp.generated.resources.multichat_timeout_confirm
import nomnomzbot.composeapp.generated.resources.multichat_timeout_dismiss
import nomnomzbot.composeapp.generated.resources.multichat_timeout_message
import nomnomzbot.composeapp.generated.resources.multichat_timeout_title
import nomnomzbot.composeapp.generated.resources.shell_nav_multichat
import org.jetbrains.compose.resources.stringResource

// The multi-channel chat-watch page (owner requirement 2026-07-10): a moderator picks several channels they
// own/moderate and watches all their live chats at once in ONE merged, time-ordered feed, each line tagged with
// its channel. The picker toggles a channel on/off (join/leave on a dedicated hub connection); the feed routes
// each live line by its channelId. A composer sends a message to any ONE watched channel (the same
// `ChatApi.send` the single-channel Chat page uses), and each line carries inline moderation quick-actions
// (delete / timeout / ban) via the same `ChatApi` moderation calls — a mod no longer has to leave the merged
// feed to act on what they're watching (S076). [hubEvents] is the dedicated multi-watch hub's event stream
// (kept separate from the main dashboard hub so watching extra channels never leaks their chat into the
// single-channel Chat page).
@Composable
fun MultiChatScreen(
    controller: MultiChatController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>,
) {
    val spacing = LocalSpacing.current
    val state: MultiChatState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    // The write gate for the composer and the inline moderation actions. The route's own read floor (Moderator)
    // already governs whether this page is reachable at all, so there is no separate manage floor to name here —
    // a caller who can see the page can act from it.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.MultiChat)

    LaunchedEffect(Unit) { controller.load() }
    // Forward live hub pushes into the merged feed for the WHOLE time the page is open, so new messages appear
    // without a reload (the subscription cancels when this effect leaves composition).
    LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: MultiChatState = state) {
            is MultiChatState.Loading -> CenteredText(stringResource(Res.string.multichat_loading), loading = true)
            is MultiChatState.Error -> CenteredText(stringResource(Res.string.multichat_error, current.detail))
            is MultiChatState.Ready ->
                ReadyContent(
                    ready = current,
                    manage = manage,
                    onToggle = { channel ->
                        if (current.watched.any { it.id == channel.id }) controller.removeChannel(channel.id)
                        else scope.launch { controller.addChannel(channel.id) }
                    },
                    onSend = { channelId, message -> scope.launch { controller.sendMessage(channelId, message) } },
                    onDelete = { channelId, messageId -> scope.launch { controller.deleteMessage(channelId, messageId) } },
                    onTimeout = { channelId, userId -> scope.launch { controller.timeoutUser(channelId, userId) } },
                    onBan = { channelId, userId -> scope.launch { controller.banUser(channelId, userId) } },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    ready: MultiChatState.Ready,
    manage: ManageDecision,
    onToggle: (ChannelSummary) -> Unit,
    onSend: (channelId: String, message: String) -> Unit,
    onDelete: (channelId: String, messageId: String) -> Unit,
    onTimeout: (channelId: String, userId: String) -> Unit,
    onBan: (channelId: String, userId: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    // channelId -> display name, so each feed line can be tagged with its source channel.
    val nameByChannel: Map<String, String> =
        ready.watched.associate { it.id to (it.displayName.ifBlank { it.login }) }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_multichat))
        ready.actionError?.let { ActionErrorBanner(message = it) }

        ChannelPicker(available = ready.available, watched = ready.watched, onToggle = onToggle)

        when {
            ready.watched.isEmpty() ->
                CenteredText(stringResource(Res.string.multichat_pick_a_channel))
            ready.messages.isEmpty() ->
                CenteredText(stringResource(Res.string.multichat_empty_feed))
            else ->
                MergedFeed(
                    messages = ready.messages,
                    nameByChannel = nameByChannel,
                    manage = manage,
                    onDelete = onDelete,
                    onTimeout = onTimeout,
                    onBan = onBan,
                )
        }

        if (ready.watched.isNotEmpty()) {
            Composer(watched = ready.watched, manage = manage, onSend = onSend)
        }
    }
}

// The multi-target composer: pick WHICH watched channel to send into (a plain select, defaulting to the first
// watched channel and following it as the watched set changes), then type + send. Gated by [manage] like the
// inline moderation actions — a caller below the page's floor never reaches this surface at all (the route's
// own read floor already keeps them off the page), so this only guards a genuinely denied state.
@Composable
private fun Composer(watched: List<ChannelSummary>, manage: ManageDecision, onSend: (channelId: String, message: String) -> Unit) {
    val spacing = LocalSpacing.current
    var targetChannelId: String by remember(watched.firstOrNull()?.id) { mutableStateOf(watched.first().id) }
    var draft: String by remember { mutableStateOf("") }
    var channelPickerExpanded: Boolean by remember { mutableStateOf(false) }
    val target: ChannelSummary = watched.firstOrNull { it.id == targetChannelId } ?: watched.first()

    fun submit() {
        val text: String = draft.trim()
        if (text.isEmpty()) return
        onSend(target.id, text)
        draft = ""
    }

    ManageGate(decision = manage) { enabled ->
        Card(modifier = Modifier.fillMaxWidth()) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(spacing.s4),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.Bottom,
            ) {
                if (watched.size > 1) {
                    AppSelectField(
                        value = target.displayName.ifBlank { target.login },
                        label = stringResource(Res.string.multichat_composer_channel_label),
                        expanded = channelPickerExpanded,
                        onExpandedChange = { channelPickerExpanded = it },
                        enabled = enabled,
                        modifier = Modifier.weight(0.3f),
                    ) {
                        watched.forEach { channel ->
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        text = resolveRowLabel(
                                            primary = channel.displayName,
                                            secondary = channel.login,
                                            typeLabel = "Channel",
                                            discriminatorSource = channel.id,
                                        )
                                    )
                                },
                                onClick = {
                                    targetChannelId = channel.id
                                    channelPickerExpanded = false
                                },
                            )
                        }
                    }
                }
                AppTextField(
                    value = draft,
                    onValueChange = { draft = it },
                    label = stringResource(Res.string.multichat_composer_placeholder),
                    placeholder = stringResource(Res.string.multichat_composer_placeholder),
                    enabled = enabled,
                    modifier = Modifier.weight(1f),
                )
                Button(onClick = ::submit, enabled = enabled && draft.isNotBlank()) {
                    Text(text = stringResource(Res.string.multichat_composer_send))
                }
            }
        }
    }
}

// The channel picker: a wrap of toggle badges, one per watchable channel. A selected badge is a watched channel;
// tapping toggles it. Live channels get a subtle live dot via the badge label (the backend flags isLive).
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ChannelPicker(
    available: List<ChannelSummary>,
    watched: List<ChannelSummary>,
    onToggle: (ChannelSummary) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(
                text = stringResource(Res.string.multichat_picker_hint),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            if (available.isEmpty()) {
                Text(
                    text = stringResource(Res.string.multichat_picker_none),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            } else {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    available.forEach { channel ->
                        val isWatched: Boolean = watched.any { it.id == channel.id }
                        val label: String = channel.displayName.ifBlank { channel.login }
                        Badge(
                            variant = if (isWatched) BadgeVariant.Default else BadgeVariant.Outline,
                            selected = isWatched,
                            onClick = { onToggle(channel) },
                        ) {
                            Text(text = if (channel.isLive) "● $label" else label, maxLines = 1)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MergedFeed(
    messages: List<ChatMessage>,
    nameByChannel: Map<String, String>,
    manage: ManageDecision,
    onDelete: (channelId: String, messageId: String) -> Unit,
    onTimeout: (channelId: String, userId: String) -> Unit,
    onBan: (channelId: String, userId: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    // Auto-follow the tail as new lines arrive, like a live chat feed. Key on the tail id as well as the size:
    // the merged feed is capped (300), so a size-only key would freeze auto-follow once it fills — exactly on the
    // busy multi-channel case this page exists for.
    val listState = rememberLazyListState()
    LaunchedEffect(messages.size, messages.lastOrNull()?.id) {
        if (messages.isNotEmpty()) listState.scrollToItem(messages.lastIndex)
    }

    Card(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            state = listState,
            modifier = Modifier.fillMaxSize().padding(vertical = spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            itemsIndexed(items = messages, key = { index, msg -> if (msg.id.isNotEmpty()) msg.id else "idx-$index" }) { _, msg ->
                MultiChatRow(
                    message = msg,
                    channelName = nameByChannel[msg.channelId],
                    manage = manage,
                    onDelete = onDelete,
                    onTimeout = onTimeout,
                    onBan = onBan,
                )
            }
        }
    }
}

// One compact monitoring line: time · channel tag · provider tag · colored name · message text · inline
// moderation menu. The moderation menu (delete / timeout / ban) targets the line's OWN channelId — since the
// feed merges several channels, an action must always be routed to the channel that message actually belongs
// to, never to whichever channel the composer currently targets.
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MultiChatRow(
    message: ChatMessage,
    channelName: String?,
    manage: ManageDecision,
    onDelete: (channelId: String, messageId: String) -> Unit,
    onTimeout: (channelId: String, userId: String) -> Unit,
    onBan: (channelId: String, userId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = chatterName(message)
    val nameColor: Color = message.color?.toComposeColor() ?: tokens.mutedForeground

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s2),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        formatClockTime(message.timestamp)?.let { time ->
            Text(text = time, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
        }
        channelName?.let { cn ->
            Badge(variant = BadgeVariant.Secondary) {
                Text(text = cn, style = typography.xs, maxLines = 1)
            }
        }
        // A source-platform tag for non-Twitch lines (kick/youtube), so a merged cross-platform feed shows origin.
        message.provider.takeIf { it.isNotBlank() && !it.equals("twitch", ignoreCase = true) }
            ?.let { provider -> ProviderTag(provider) }
        Text(
            text = name,
            style = typography.sm,
            color = nameColor,
            maxLines = 1,
        )
        // Decorated body — the SAME renderer as the primary chat feed, so Twitch emotes/cheermotes show as
        // inline images and plain runs (Unicode emoji) as Twemoji, not tofu. Hosted in a FlowRow so the mixed
        // image/text fragments wrap across the row's remaining width.
        FlowRow(
            modifier = Modifier.weight(1f),
            horizontalArrangement = Arrangement.spacedBy(spacing.s1),
            verticalArrangement = Arrangement.Center,
        ) {
            ChatMessageFragments(fragments = message.fragments, fallbackText = message.message)
        }
        // A system/announcement line carries no chatter id — nothing to timeout/ban/delete there.
        if (message.userId.isNotBlank()) {
            ModerationMenu(message = message, name = name, manage = manage, onDelete = onDelete, onTimeout = onTimeout, onBan = onBan)
        }
    }
}

// The per-line moderation menu: delete this message, timeout the author, or ban the author — all routed to
// the LINE's OWN channelId (not the composer's current target). Each destructive action confirms first via the
// shared [ConfirmDialog], mirroring the single-channel Chat page's moderation menu.
@Composable
private fun ModerationMenu(
    message: ChatMessage,
    name: String,
    manage: ManageDecision,
    onDelete: (channelId: String, messageId: String) -> Unit,
    onTimeout: (channelId: String, userId: String) -> Unit,
    onBan: (channelId: String, userId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    var confirmDelete: Boolean by remember { mutableStateOf(false) }
    var confirmTimeout: Boolean by remember { mutableStateOf(false) }
    var confirmBan: Boolean by remember { mutableStateOf(false) }

    val menuLabel: String = stringResource(Res.string.multichat_row_actions, name)
    val deleteItemLabel: String = stringResource(Res.string.multichat_delete_action)
    val timeoutItemLabel: String = stringResource(Res.string.multichat_timeout_action, name)
    val banItemLabel: String = stringResource(Res.string.multichat_ban_action, name)

    Box {
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = DotsHorizontalGlyph,
                label = menuLabel,
                onClick = { expanded = true },
                enabled = enabled,
            )
        }

        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(
                text = { Text(text = deleteItemLabel, style = typography.sm, color = tokens.destructive) },
                modifier = Modifier.semantics { role = Role.Button; contentDescription = deleteItemLabel },
                onClick = { expanded = false; confirmDelete = true },
            )
            DropdownMenuItem(
                text = { Text(text = timeoutItemLabel, style = typography.sm, color = tokens.destructive) },
                modifier = Modifier.semantics { role = Role.Button; contentDescription = timeoutItemLabel },
                onClick = { expanded = false; confirmTimeout = true },
            )
            DropdownMenuItem(
                text = { Text(text = banItemLabel, style = typography.sm, color = tokens.destructive) },
                modifier = Modifier.semantics { role = Role.Button; contentDescription = banItemLabel },
                onClick = { expanded = false; confirmBan = true },
            )
        }
    }

    if (confirmDelete) {
        ConfirmDialog(
            title = stringResource(Res.string.multichat_delete_title),
            message = stringResource(Res.string.multichat_delete_message, name),
            confirmLabel = stringResource(Res.string.multichat_delete_confirm),
            dismissLabel = stringResource(Res.string.multichat_delete_dismiss),
            destructive = true,
            onConfirm = { onDelete(message.channelId, message.id); confirmDelete = false },
            onDismiss = { confirmDelete = false },
        )
    }

    if (confirmTimeout) {
        ConfirmDialog(
            title = stringResource(Res.string.multichat_timeout_title),
            message = stringResource(Res.string.multichat_timeout_message, name),
            confirmLabel = stringResource(Res.string.multichat_timeout_confirm),
            dismissLabel = stringResource(Res.string.multichat_timeout_dismiss),
            destructive = true,
            onConfirm = { onTimeout(message.channelId, message.userId); confirmTimeout = false },
            onDismiss = { confirmTimeout = false },
        )
    }

    if (confirmBan) {
        ConfirmDialog(
            title = stringResource(Res.string.multichat_ban_title),
            message = stringResource(Res.string.multichat_ban_message, name),
            confirmLabel = stringResource(Res.string.multichat_ban_confirm),
            dismissLabel = stringResource(Res.string.multichat_ban_dismiss),
            destructive = true,
            onConfirm = { onBan(message.channelId, message.userId); confirmBan = false },
            onDismiss = { confirmBan = false },
        )
    }
}

@Composable
private fun CenteredText(text: String, loading: Boolean = false) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    Box(modifier = Modifier.fillMaxWidth().padding(spacing.s8), contentAlignment = Alignment.Center) {
        if (loading) {
            Spinner(modifier = Modifier)
        } else {
            Text(text = text, color = tokens.mutedForeground)
        }
    }
}
