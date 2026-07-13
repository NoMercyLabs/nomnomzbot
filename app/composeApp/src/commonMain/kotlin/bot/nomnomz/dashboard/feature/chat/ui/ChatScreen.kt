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

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.CloseGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.DotsHorizontalGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ReplyGlyph
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEvent
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.isShiftPressed
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import coil3.compose.AsyncImage
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.component.Tooltip
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.media.AnimatedNetworkImage
import bot.nomnomz.dashboard.core.media.EmojiCatalog
import bot.nomnomz.dashboard.core.media.EmojiEntry
import bot.nomnomz.dashboard.core.media.EmojiText
import bot.nomnomz.dashboard.core.media.searchEmoji
import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.network.ChatSettings
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.chat.state.ChatController
import bot.nomnomz.dashboard.feature.chat.state.ChatState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import kotlinx.datetime.Instant
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.chat_action_error
import nomnomzbot.composeapp.generated.resources.chat_announce_action
import nomnomzbot.composeapp.generated.resources.chat_ban_action
import nomnomzbot.composeapp.generated.resources.chat_ban_action_short
import nomnomzbot.composeapp.generated.resources.chat_ban_confirm
import nomnomzbot.composeapp.generated.resources.chat_ban_dismiss
import nomnomzbot.composeapp.generated.resources.chat_ban_reason_label
import nomnomzbot.composeapp.generated.resources.chat_ban_scope_all_moderated
import nomnomzbot.composeapp.generated.resources.chat_ban_scope_label
import nomnomzbot.composeapp.generated.resources.chat_ban_scope_this_channel
import nomnomzbot.composeapp.generated.resources.chat_ban_title
import nomnomzbot.composeapp.generated.resources.chat_delete_action
import nomnomzbot.composeapp.generated.resources.chat_delete_action_short
import nomnomzbot.composeapp.generated.resources.chat_delete_confirm
import nomnomzbot.composeapp.generated.resources.chat_delete_dismiss
import nomnomzbot.composeapp.generated.resources.chat_delete_message
import nomnomzbot.composeapp.generated.resources.chat_delete_title
import nomnomzbot.composeapp.generated.resources.chat_empty
import nomnomzbot.composeapp.generated.resources.chat_settings_emote_only
import nomnomzbot.composeapp.generated.resources.chat_settings_followers_duration
import nomnomzbot.composeapp.generated.resources.chat_settings_followers_only
import nomnomzbot.composeapp.generated.resources.chat_settings_panel_title
import nomnomzbot.composeapp.generated.resources.chat_settings_slow_delay
import nomnomzbot.composeapp.generated.resources.chat_settings_slow_mode
import nomnomzbot.composeapp.generated.resources.chat_settings_sub_only
import nomnomzbot.composeapp.generated.resources.chat_subtitle
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_blue
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_green
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_label
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_orange
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_primary
import nomnomzbot.composeapp.generated.resources.moderation_announce_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_announce_message_label
import nomnomzbot.composeapp.generated.resources.moderation_announce_message_required
import nomnomzbot.composeapp.generated.resources.moderation_announce_send
import nomnomzbot.composeapp.generated.resources.moderation_announce_title
import nomnomzbot.composeapp.generated.resources.shell_nav_chat
import nomnomzbot.composeapp.generated.resources.chat_error
import nomnomzbot.composeapp.generated.resources.chat_loading
import nomnomzbot.composeapp.generated.resources.chat_message_description
import nomnomzbot.composeapp.generated.resources.chat_report_action
import nomnomzbot.composeapp.generated.resources.chat_report_action_short
import nomnomzbot.composeapp.generated.resources.chat_report_confirm
import nomnomzbot.composeapp.generated.resources.chat_report_description
import nomnomzbot.composeapp.generated.resources.chat_report_dismiss
import nomnomzbot.composeapp.generated.resources.chat_report_reason_label
import nomnomzbot.composeapp.generated.resources.chat_report_title
import nomnomzbot.composeapp.generated.resources.chat_reply_action
import nomnomzbot.composeapp.generated.resources.chat_reply_cancel
import nomnomzbot.composeapp.generated.resources.chat_reply_expand
import nomnomzbot.composeapp.generated.resources.chat_reply_indicator
import nomnomzbot.composeapp.generated.resources.chat_reply_indicator_unknown
import nomnomzbot.composeapp.generated.resources.chat_retry
import nomnomzbot.composeapp.generated.resources.chat_row_actions
import nomnomzbot.composeapp.generated.resources.chat_send_action
import nomnomzbot.composeapp.generated.resources.chat_send_as_bot
import nomnomzbot.composeapp.generated.resources.chat_send_as_you
import nomnomzbot.composeapp.generated.resources.chat_send_identity_label
import nomnomzbot.composeapp.generated.resources.chat_send_placeholder
import nomnomzbot.composeapp.generated.resources.chat_timeout_action
import nomnomzbot.composeapp.generated.resources.chat_timeout_action_short
import nomnomzbot.composeapp.generated.resources.chat_timeout_confirm
import nomnomzbot.composeapp.generated.resources.chat_timeout_dismiss
import nomnomzbot.composeapp.generated.resources.chat_timeout_message
import nomnomzbot.composeapp.generated.resources.chat_timeout_title
import org.jetbrains.compose.resources.stringResource

// The Chat page (frontend-ia.md §3 — the Chat group): the channel's recent chat is real data from
// [ChatController] (the backend persists it from EventSub `channel.chat.message`). The screen is a pure
// projection of the controller's state; it loads on first composition, polls for fresh lines, and offers a
// retry on a first-load failure. The streamer can send a line as the bot from the box at the bottom, and each
// message is moderatable: delete that one message or timeout its author — both only once confirmed in the
// shared ConfirmDialog (design-system rule: destructive actions MUST confirm).
@Composable
fun ChatScreen(
    controller: ChatController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: ChatState by controller.state.collectAsStateWithLifecycle()
    val replyTarget: ChatMessage? by controller.replyTarget.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    var showAnnounce: Boolean by remember { mutableStateOf(false) }

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Chat)

    // Load the initial history once. New lines then arrive over the hub ([subscribeToHub]) in real time — the
    // client keep-alive ping (DashboardHubClient) holds the socket open so the server never times it out, so the
    // push keeps flowing. Polling is only a fallback for a no-hub environment (a test/local build with no live
    // SignalR connection), never the primary path.
    LaunchedEffect(Unit) {
        controller.load()
        if (hubEvents == null) {
            while (true) {
                delay(PollIntervalMillis)
                controller.load()
            }
        }
    }

    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Column(modifier = Modifier.fillMaxSize().padding(spacing.s6), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_chat),
            subtitle = stringResource(Res.string.chat_subtitle),
            trailing = {
                ManageGate(decision = manage) { enabled ->
                    TextButton(onClick = { showAnnounce = true }, enabled = enabled) {
                        Text(stringResource(Res.string.chat_announce_action))
                    }
                }
            },
        )

        // Chat mode toggles — only rendered once settings are loaded.
        val currentState: ChatState = state
        if (currentState is ChatState.Ready && currentState.settings != null) {
            ManageGate(decision = manage) { enabled ->
                ChatModesBar(
                    settings = currentState.settings,
                    enabled = enabled,
                    onToggle = { updated -> scope.launch { controller.updateSettings(updated) } },
                )
            }
        }

        Box(modifier = Modifier.weight(1f).fillMaxWidth()) {
            when (val current: ChatState = state) {
                is ChatState.Loading -> CenteredMessage(stringResource(Res.string.chat_loading))
                is ChatState.Empty -> CenteredMessage(stringResource(Res.string.chat_empty))
                is ChatState.Error ->
                    ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
                is ChatState.Ready ->
                    MessageFeed(
                        messages = current.messages,
                        actionError = current.actionError,
                        manage = manage,
                        onDelete = { id -> scope.launch { controller.deleteMessage(id) } },
                        onTimeout = { userId -> scope.launch { controller.timeout(userId) } },
                        onBan = { userId, banScope, reason ->
                            scope.launch { controller.ban(userId, banScope, reason) }
                        },
                        onReport = { userId, userName, displayName, reason ->
                            scope.launch { controller.report(userId, userName, displayName, reason) }
                        },
                        onReply = { message -> controller.startReply(message) },
                    )
            }
        }
        val composerEmotes: List<ChatEmoteCatalogue> =
            (state as? ChatState.Ready)?.emotes ?: emptyList()
        SendBox(
            manage = manage,
            emotes = composerEmotes,
            replyTarget = replyTarget,
            onCancelReply = { controller.cancelReply() },
            onSend = { text, identity -> scope.launch { controller.send(text, identity) } },
        )
    }

    if (showAnnounce) {
        AnnounceDialog(
            onDismiss = { showAnnounce = false },
            onSend = { message, color ->
                showAnnounce = false
                scope.launch { controller.announce(message, color) }
            },
        )
    }
}

@Composable
private fun MessageFeed(
    messages: List<ChatMessage>,
    actionError: String?,
    manage: ManageDecision,
    onDelete: (messageId: String) -> Unit,
    onTimeout: (userId: String) -> Unit,
    onBan: (userId: String, scope: String, reason: String?) -> Unit,
    onReport: (userId: String, userName: String, displayName: String, reason: String) -> Unit,
    onReply: (message: ChatMessage) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val listState = rememberLazyListState()
    // Keep the newest line in view as chat arrives (the feed is oldest-first, so the bottom is newest).
    LaunchedEffect(messages.size) {
        if (messages.isNotEmpty()) listState.scrollToItem(messages.lastIndex)
    }

    Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        actionError?.let { detail ->
            ActionErrorBanner(message = stringResource(Res.string.chat_action_error, detail))
        }
        LazyColumn(
            state = listState,
            modifier = Modifier.weight(1f).fillMaxWidth(),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            items(items = messages, key = { message -> message.id }) { message ->
                MessageRow(
                    message = message,
                    manage = manage,
                    onDelete = onDelete,
                    onTimeout = onTimeout,
                    onBan = onBan,
                    onReport = onReport,
                    onReply = onReply,
                )
            }
        }
    }
}

@Composable
private fun MessageRow(
    message: ChatMessage,
    manage: ManageDecision,
    onDelete: (messageId: String) -> Unit,
    onTimeout: (userId: String) -> Unit,
    onBan: (userId: String, scope: String, reason: String?) -> Unit,
    onReport: (userId: String, userName: String, displayName: String, reason: String) -> Unit,
    onReply: (message: ChatMessage) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = chatterName(message)
    val rowDescription: String =
        stringResource(Res.string.chat_message_description, name, message.message)
    val nameColor: Color = message.color?.toComposeColor() ?: tokens.mutedForeground

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.Top,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        // Per-line timestamp in the viewer's LOCAL time (muted), like the Twitch client's message times.
        formatClockTime(message.timestamp)?.let { time ->
            Text(text = time, style = typography.sm, color = tokens.mutedForeground, maxLines = 1)
        }
        // The message-body column takes the row's free width: an optional FFZ-style reply indicator sits above
        // the inline badges + name + fragments.
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            // Render the reply indicator whenever this line is a reply — the parent id alone is enough (REST
            // scrollback persists only the id); the author/body, when present (live hub), drive hover + expand.
            if (message.replyToMessageId != null || !message.replyParentUserName.isNullOrBlank()) {
                ReplyIndicator(message = message)
            }
            MessageInlineBody(
                message = message,
                rowDescription = rowDescription,
                name = name,
                nameColor = nameColor,
            )
        }
        MessageActions(
            message = message,
            name = name,
            manage = manage,
            onDelete = onDelete,
            onTimeout = onTimeout,
            onBan = onBan,
            onReport = onReport,
            onReply = { onReply(message) },
        )
    }
}

// The inline body of one chat line: badges + name + decorated fragments, all flowing inline as a single a11y
// read ([rowDescription]). Extracted from [MessageRow] so the row can stack a reply indicator above it.
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MessageInlineBody(
    message: ChatMessage,
    rowDescription: String,
    name: String,
    nameColor: Color,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    FlowRow(
        modifier = Modifier.fillMaxWidth().clearAndSetSemantics { contentDescription = rowDescription },
        horizontalArrangement = Arrangement.spacedBy(spacing.s1),
        verticalArrangement = Arrangement.Center,
    ) {
        // Chatter avatar (resolved by the backend enricher) — a small circle before the badges.
        message.avatarUrl?.takeIf { it.isNotBlank() }?.let { avatar ->
            AsyncImage(
                model = avatar,
                contentDescription = null,
                modifier = Modifier.size(20.dp).clip(CircleShape).align(Alignment.CenterVertically),
            )
        }
        // Badge strip — 18 dp per badge.
        message.badges.forEach { badge ->
            val url: String? = badge.urls["2"] ?: badge.urls["1"] ?: badge.urls.values.firstOrNull()
            if (url != null) {
                AnimatedNetworkImage(
                    url = url,
                    contentDescription = badge.setId,
                    modifier = Modifier.size(18.dp).align(Alignment.CenterVertically),
                )
            }
        }
        // Pronoun chip (resolved by the backend enricher) — a small muted badge next to the name.
        message.pronouns?.takeIf { it.isNotBlank() }?.let { pronouns -> PronounChip(pronouns) }
        // Colored, semi-bold name.
        Text(
            text = "$name:",
            style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
            color = nameColor,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        // Fragment body — falls back to the flat [message] string when fragments are empty (REST history).
        if (message.fragments.isNotEmpty()) {
            message.fragments.forEach { fragment ->
                when (fragment.type) {
                    "emote" -> {
                        val url: String? = fragment.emote?.urls?.let {
                            it["2"] ?: it["1"] ?: it.values.firstOrNull()
                        }
                        if (url != null) {
                            AnimatedNetworkImage(
                                url = url,
                                contentDescription = fragment.text,
                                modifier = Modifier.size(24.dp).align(Alignment.CenterVertically),
                            )
                        } else {
                            Text(text = fragment.text, style = typography.sm, color = tokens.cardForeground)
                        }
                    }
                    "cheermote" -> {
                        val url: String? = fragment.cheermote?.urls?.let {
                            it["2"] ?: it["1"] ?: it.values.firstOrNull()
                        }
                        if (url != null) {
                            AnimatedNetworkImage(
                                url = url,
                                contentDescription = fragment.text,
                                modifier = Modifier.size(24.dp).align(Alignment.CenterVertically),
                            )
                        } else {
                            val tierColor: Color =
                                fragment.cheermote?.colorHex?.toComposeColor() ?: tokens.cardForeground
                            Text(text = fragment.text, style = typography.sm, color = tierColor)
                        }
                    }
                    "mention" -> {
                        val mentionColor: Color =
                            fragment.mention?.color?.toComposeColor() ?: tokens.primary
                        Text(
                            text = "@${fragment.mention?.displayName?.takeIf { it.isNotBlank() } ?: fragment.text.removePrefix("@")}",
                            style = typography.sm,
                            color = mentionColor,
                        )
                    }
                    "link" -> {
                        Text(
                            text = fragment.text,
                            style = typography.sm.copy(textDecoration = TextDecoration.Underline),
                            color = tokens.primary,
                        )
                    }
                    else -> {
                        // Plain text run — may carry Unicode emoji, so render through [EmojiText] (inline
                        // Twemoji images) rather than raw `Text`, which draws □ tofu on the web build.
                        EmojiText(text = fragment.text, style = typography.sm, color = tokens.cardForeground)
                    }
                }
            }
        } else {
            // REST history has no fragments — render the flat message string, which is where typed Unicode
            // emoji land, through [EmojiText] so they show as images instead of □ tofu on the web build.
            EmojiText(text = message.message, style = typography.sm, color = tokens.cardForeground)
        }
    }
}

// The per-message reply indicator (FFZ-style): a compact "↩ Replying to @author" line above a reply's body.
// Hovering it reveals the parent inline (@author: body, truncated) via the DS Tooltip; clicking it expands the
// full parent message below, clicking again collapses. Both the hover preview and the click-expand need the
// parent body — the live hub carries author + body from EventSub, but REST scrollback persists only the parent
// id, so there the indicator renders as a static, non-expandable line off the id alone.
@Composable
private fun ReplyIndicator(message: ChatMessage) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val parentName: String? = message.replyParentUserName?.takeIf { it.isNotBlank() }
    val parentBody: String? = message.replyParentMessageBody?.takeIf { it.isNotBlank() }

    val indicatorText: String =
        parentName?.let { stringResource(Res.string.chat_reply_indicator, it) }
            ?: stringResource(Res.string.chat_reply_indicator_unknown)
    val expandLabel: String = stringResource(Res.string.chat_reply_expand, parentName ?: indicatorText)

    // The full parent line "@author: body"; the hover bubble shows a truncated form, the expansion the whole body.
    val parentLine: String? =
        parentBody?.let { body -> if (parentName != null) "@$parentName: $body" else body }
    val tooltipText: String =
        parentBody?.let { body ->
            val short: String = truncateReplyBody(body)
            if (parentName != null) "@$parentName: $short" else short
        } ?: indicatorText

    var expanded: Boolean by remember(message.id) { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Tooltip(text = tooltipText) {
            Row(
                modifier = Modifier
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .then(
                        if (parentLine != null) Modifier.clickable { expanded = !expanded } else Modifier,
                    )
                    .semantics {
                        contentDescription = if (parentLine != null) expandLabel else indicatorText
                    },
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Icon(
                    imageVector = ReplyGlyph,
                    contentDescription = null,
                    tint = tokens.mutedForeground,
                    modifier = Modifier.size(spacing.s3),
                )
                Text(
                    text = indicatorText,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        // Click-to-expand: the full parent message inline (only when the body is available).
        if (expanded && parentLine != null) {
            Text(
                text = parentLine,
                style = typography.xs,
                color = tokens.mutedForeground,
                modifier = Modifier
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .background(tokens.muted)
                    .padding(horizontal = spacing.s2, vertical = spacing.s1),
            )
        }
    }
}

// The composer's reply-mode banner: names who the next send will reply to, with a ✕ to leave reply mode. The
// send routes through ChatController, which threads the parent id and clears reply mode on a successful send.
@Composable
private fun ReplyComposerBanner(target: ChatMessage, onCancel: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = chatterName(target)
    val bannerText: String = stringResource(Res.string.chat_reply_indicator, name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .padding(start = spacing.s3, top = spacing.s1, bottom = spacing.s1, end = spacing.s1),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Icon(
            imageVector = ReplyGlyph,
            contentDescription = null,
            tint = tokens.mutedForeground,
            modifier = Modifier.size(spacing.s4),
        )
        Text(
            text = bannerText,
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        GlyphButton(
            imageVector = CloseGlyph,
            label = stringResource(Res.string.chat_reply_cancel),
            onClick = onCancel,
        )
    }
}

// Truncate a replied-to message body for the hover bubble so a long parent line stays a compact tooltip.
private const val ReplyBodyMaxChars: Int = 140

private fun truncateReplyBody(body: String): String =
    if (body.length <= ReplyBodyMaxChars) body else body.take(ReplyBodyMaxChars).trimEnd() + "…"

// The per-message action row: a Reply affordance (FFZ-style) beside the destructive moderation menu. Reply is a
// send action, so it sits on the same Chat write floor as the mod menu and the composer's Send.
@Composable
private fun MessageActions(
    message: ChatMessage,
    name: String,
    manage: ManageDecision,
    onDelete: (messageId: String) -> Unit,
    onTimeout: (userId: String) -> Unit,
    onBan: (userId: String, scope: String, reason: String?) -> Unit,
    onReport: (userId: String, userName: String, displayName: String, reason: String) -> Unit,
    onReply: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val replyLabel: String = stringResource(Res.string.chat_reply_action, name)

    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = ReplyGlyph,
                label = replyLabel,
                onClick = onReply,
                enabled = enabled,
            )
        }
        ModerationMenu(
            message = message,
            name = name,
            manage = manage,
            onDelete = onDelete,
            onTimeout = onTimeout,
            onBan = onBan,
            onReport = onReport,
        )
    }
}

// The per-message moderation menu: a labelled trigger that opens the closed menu of quick-actions (delete this
// message, timeout the author). Both are destructive, so each routes through the shared ConfirmDialog before
// it fires; the screen owns the pending-confirmation state here so the menu closes the instant one is chosen.
@Composable
private fun ModerationMenu(
    message: ChatMessage,
    name: String,
    manage: ManageDecision,
    onDelete: (messageId: String) -> Unit,
    onTimeout: (userId: String) -> Unit,
    onBan: (userId: String, scope: String, reason: String?) -> Unit,
    onReport: (userId: String, userName: String, displayName: String, reason: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    var confirmDelete: Boolean by remember { mutableStateOf(false) }
    var confirmTimeout: Boolean by remember { mutableStateOf(false) }
    var showBan: Boolean by remember { mutableStateOf(false) }
    var showReport: Boolean by remember { mutableStateOf(false) }

    val menuLabel: String = stringResource(Res.string.chat_row_actions, name)
    val deleteItemLabel: String = stringResource(Res.string.chat_delete_action)
    val timeoutItemLabel: String = stringResource(Res.string.chat_timeout_action, name)
    val banItemLabel: String = stringResource(Res.string.chat_ban_action, name)
    val reportItemLabel: String = stringResource(Res.string.chat_report_action, name)

    Box {
        // The moderation menu trigger is the write affordance: gate it so a caller below the floor sees the
        // per-message delete/timeout menu disabled with its reason, rather than gating each dialog button.
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = DotsHorizontalGlyph,
                label = menuLabel,
                onClick = { expanded = true },
                enabled = enabled,
            )
        }

        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.chat_delete_action_short),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = deleteItemLabel
                },
                onClick = {
                    expanded = false
                    confirmDelete = true
                },
            )
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.chat_timeout_action_short),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = timeoutItemLabel
                },
                onClick = {
                    expanded = false
                    confirmTimeout = true
                },
            )
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.chat_ban_action_short),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = banItemLabel
                },
                onClick = {
                    expanded = false
                    showBan = true
                },
            )
            // Report is NOT destructive — it flags the chatter for a moderator to triage (no punishment), so it
            // renders in the default foreground rather than the destructive red of delete/timeout/ban.
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.chat_report_action_short),
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )
                },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = reportItemLabel
                },
                onClick = {
                    expanded = false
                    showReport = true
                },
            )
        }
    }

    if (confirmDelete) {
        ConfirmDialog(
            title = stringResource(Res.string.chat_delete_title),
            message = stringResource(Res.string.chat_delete_message, name),
            confirmLabel = stringResource(Res.string.chat_delete_confirm),
            dismissLabel = stringResource(Res.string.chat_delete_dismiss),
            destructive = true,
            onConfirm = {
                onDelete(message.id)
                confirmDelete = false
            },
            onDismiss = { confirmDelete = false },
        )
    }

    if (confirmTimeout) {
        ConfirmDialog(
            title = stringResource(Res.string.chat_timeout_title),
            message = stringResource(Res.string.chat_timeout_message, name),
            confirmLabel = stringResource(Res.string.chat_timeout_confirm),
            dismissLabel = stringResource(Res.string.chat_timeout_dismiss),
            destructive = true,
            onConfirm = {
                onTimeout(message.userId)
                confirmTimeout = false
            },
            onDismiss = { confirmTimeout = false },
        )
    }

    if (showBan) {
        BanDialog(
            name = name,
            onDismiss = { showBan = false },
            onConfirm = { banScope, reason ->
                onBan(message.userId, banScope, reason)
                showBan = false
            },
        )
    }

    if (showReport) {
        ReportDialog(
            name = name,
            onDismiss = { showReport = false },
            onConfirm = { reason ->
                onReport(message.userId, message.username, message.displayName, reason)
                showReport = false
            },
        )
    }
}

// The report dialog: capture a required reason, then file the report (a moderator triages it later on the
// Moderation page). Confirm stays disabled until the reason is non-blank, matching the backend's required-reason rule.
@Composable
private fun ReportDialog(
    name: String,
    onDismiss: () -> Unit,
    onConfirm: (reason: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var reason: String by remember { mutableStateOf("") }
    val canSubmit: Boolean = reason.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.chat_report_title, name)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                Text(
                    text = stringResource(Res.string.chat_report_description),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Textarea(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.chat_report_reason_label),
                    minLines = 2,
                    maxLines = 4,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { if (canSubmit) onConfirm(reason.trim()) },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.chat_report_confirm),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.chat_report_dismiss)) }
        },
    )
}

// The ban dialog (chat-client.md §3.5): choose the scope — this channel only, or every channel the operator
// moderates — plus an optional reason, then confirm. The backend issues the ban(s) as the operator's own token.
@Composable
private fun BanDialog(
    name: String,
    onDismiss: () -> Unit,
    onConfirm: (scope: String, reason: String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var scope: String by remember { mutableStateOf("this_channel") }
    var reason: String by remember { mutableStateOf("") }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.chat_ban_title, name)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                Text(
                    text = stringResource(Res.string.chat_ban_scope_label),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Badge(
                        selected = scope == "this_channel",
                        onClick = { scope = "this_channel" },
                    ) {
                        Text(stringResource(Res.string.chat_ban_scope_this_channel), style = typography.sm)
                    }
                    Badge(
                        selected = scope == "all_moderated",
                        onClick = { scope = "all_moderated" },
                    ) {
                        Text(stringResource(Res.string.chat_ban_scope_all_moderated), style = typography.sm)
                    }
                }
                Textarea(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.chat_ban_reason_label),
                    minLines = 2,
                    maxLines = 4,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(scope, reason.trim().ifBlank { null }) }) {
                Text(stringResource(Res.string.chat_ban_confirm), color = tokens.destructive)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.chat_ban_dismiss)) }
        },
    )
}

// The send composer: an input that sends as the operator's own account by default or, via the identity selector,
// as the bot (chat-client.md §3.1) — clearing on send. Recognised emote codes and Unicode emoji render INLINE in
// the field itself ([EmoteComposerField]); typing a trailing ":prefix" opens autocomplete over BOTH channel emotes
// and standard emoji, Tab inserts the top hit, Enter sends. Empty / blank input is ignored, matching the backend's
// empty-message rejection.
@Composable
private fun SendBox(
    manage: ManageDecision,
    emotes: List<ChatEmoteCatalogue>,
    replyTarget: ChatMessage?,
    onCancelReply: () -> Unit,
    onSend: (message: String, senderIdentity: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    // A TextFieldValue (not a bare String) so the caret is explicit: a programmatic insert (Tab / pick) parks it at
    // the end, which the String field overload cannot do — the fix for the mid-string garble the live client showed.
    var draft: TextFieldValue by remember { mutableStateOf(TextFieldValue("")) }
    // Who the line is sent as: "you" = the operator's own account (default), "bot" = the channel bot identity.
    var identity: String by remember { mutableStateOf("you") }
    val canSend: Boolean = draft.text.isNotBlank()

    // The full Unicode-emoji catalogue, loaded once; the composer offers these alongside channel emotes.
    val emojiEntries: List<EmojiEntry> by produceState<List<EmojiEntry>>(initialValue = emptyList()) {
        value = EmojiCatalog.load()
    }

    // Lookup for the inline field: a whitespace token matching a catalogue code renders as the emote image.
    val emoteByCode: Map<String, ChatEmoteCatalogue> =
        remember(emotes) { emotes.filter { it.code.isNotBlank() }.associateBy { it.code.lowercase() } }

    // Autocomplete: a trailing ":token" (2+ word chars) searches channel emotes by SUBSTRING and emoji by
    // shortcode/keyword — emotes first — so ":wa" surfaces verosWaving / :wave: and ":smi" surfaces :smile:.
    val query: String? =
        remember(draft.text) { Regex(":([A-Za-z0-9_]{2,})$").find(draft.text)?.groupValues?.get(1) }
    val suggestions: List<ComposerSuggestion> =
        remember(query, emotes, emojiEntries) {
            if (query == null) {
                emptyList()
            } else {
                val emoteHits: List<ComposerSuggestion> =
                    emoteSuggestions(emotes, query).map { ComposerSuggestion.Emote(it) }
                val emojiHits: List<ComposerSuggestion> =
                    searchEmoji(emojiEntries, query).map { ComposerSuggestion.Emoji(it) }
                (emoteHits + emojiHits).take(SuggestionLimit)
            }
        }

    fun submit() {
        // The manage floor gates every send path — the visible button AND the Enter key — so a caller below the
        // Chat write floor can neither click nor type-send.
        if (canSend && manage.isAllowed) {
            onSend(draft.text, identity)
            draft = TextFieldValue("")
        }
    }

    // Replace the trailing ":token" with the picked suggestion — a channel emote's code, or an emoji's glyph — plus
    // a trailing space; the inline field then renders it as its image. The caret is parked at the end so the next
    // keystroke appends rather than landing mid-string.
    fun insert(suggestion: ComposerSuggestion) {
        val inserted: String = Regex(":[A-Za-z0-9_]{2,}$").replace(draft.text) { "${suggestion.insertText} " }
        draft = TextFieldValue(text = inserted, selection = TextRange(inserted.length))
    }

    // Tab inserts the top suggestion (like the Twitch composer); Enter sends, Shift+Enter adds a newline. Each is
    // consumed before the field would type it, but only when it would actually do something.
    val onPreviewKey: (KeyEvent) -> Boolean = key@{ event ->
        if (event.type != KeyEventType.KeyDown) return@key false
        when (event.key) {
            Key.Tab -> {
                val top: ComposerSuggestion? = suggestions.firstOrNull()
                if (top != null) {
                    insert(top)
                    true
                } else {
                    false
                }
            }
            Key.Enter, Key.NumPadEnter ->
                if (event.isShiftPressed) {
                    false
                } else {
                    submit()
                    true
                }
            else -> false
        }
    }

    val sendLabel: String = stringResource(Res.string.chat_send_action)

    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // Reply-mode banner: when a message has been chosen to reply to, name the parent with a ✕ to cancel. The
        // send threads the parent id through ChatController.send, which clears reply mode on a successful send.
        replyTarget?.let { target ->
            ReplyComposerBanner(target = target, onCancel = onCancelReply)
        }
        // Autocomplete suggestions float directly above the input, like the Twitch composer.
        if (suggestions.isNotEmpty()) {
            EmoteSuggestions(suggestions = suggestions, onPick = { insert(it) })
        }
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            // Identity selector: send as the operator's own account ("You") or the channel bot ("Bot").
            SendIdentitySelector(identity = identity, onSelect = { identity = it })
            EmoteComposerField(
                value = draft,
                onValueChange = { draft = it },
                emoteByCode = emoteByCode,
                placeholder = stringResource(Res.string.chat_send_placeholder),
                enabled = true,
                onPreviewKey = onPreviewKey,
                modifier = Modifier.weight(1f),
            )
            ManageGate(decision = manage) { gateEnabled ->
                // Send is offered only when the gate allows it AND the draft is non-blank; the Enter key goes
                // through the same `submit()` guard, and the visible affordance reflects the floor.
                val sendEnabled: Boolean = gateEnabled && canSend
                TextButton(
                    onClick = { submit() },
                    enabled = sendEnabled,
                    modifier = Modifier.semantics { contentDescription = sendLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.chat_send_action),
                        color = if (sendEnabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }
    }
}

// One composer autocomplete row — a channel emote or a standard emoji — with a common image + label + insert text,
// so the picker, Tab-insert, and inline field treat both uniformly.
private sealed interface ComposerSuggestion {
    val imageUrl: String?
    val label: String
    val insertText: String

    data class Emote(val emote: ChatEmoteCatalogue) : ComposerSuggestion {
        override val imageUrl: String?
            get() = emote.urls["2"] ?: emote.urls["1"] ?: emote.urls.values.firstOrNull()
        override val label: String get() = emote.code
        override val insertText: String get() = emote.code
    }

    data class Emoji(val entry: EmojiEntry) : ComposerSuggestion {
        override val imageUrl: String get() = entry.imageUrl
        override val label: String get() = ":${entry.primaryShortcode}:"
        override val insertText: String get() = entry.glyph
    }
}

// The combined emote + emoji autocomplete is capped so the floating list stays compact.
private const val SuggestionLimit: Int = 12

// The send-identity selector (chat-client.md §3.1): two compact chips choosing who the composed line is sent as
// — "You" (the operator's own account, default) or "Bot" (the channel bot). Selecting one only sets local state;
// the send itself is gated separately, so a caller below the send floor sees the disabled Send button, not this.
@Composable
private fun SendIdentitySelector(identity: String, onSelect: (String) -> Unit) {
    val spacing = LocalSpacing.current
    val selectorLabel: String = stringResource(Res.string.chat_send_identity_label)

    Row(
        horizontalArrangement = Arrangement.spacedBy(spacing.s1),
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier.semantics { contentDescription = selectorLabel },
    ) {
        TabsList {
            TabsTrigger(
                selected = identity == "you",
                onClick = { onSelect("you") },
            ) {
                Text(text = stringResource(Res.string.chat_send_as_you))
            }
            TabsTrigger(
                selected = identity == "bot",
                onClick = { onSelect("bot") },
            ) {
                Text(text = stringResource(Res.string.chat_send_as_bot))
            }
        }
    }
}

// The emote/emoji autocomplete list — each matching suggestion (image + label) shown above the composer input; a
// tap inserts it (a channel emote's code or an emoji's glyph) into the draft.
@Composable
private fun EmoteSuggestions(
    suggestions: List<ComposerSuggestion>,
    onPick: (ComposerSuggestion) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(8.dp))
            .background(tokens.card)
            .padding(spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        suggestions.forEach { suggestion ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(4.dp))
                    .clickable { onPick(suggestion) }
                    .padding(horizontal = spacing.s2, vertical = spacing.s1),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                suggestion.imageUrl?.let { url ->
                    AnimatedNetworkImage(
                        url = url,
                        contentDescription = null,
                        modifier = Modifier.size(24.dp),
                    )
                }
                Text(text = suggestion.label, style = typography.sm, color = tokens.cardForeground)
            }
        }
    }
}

// Filter the emote catalogue for the composer autocomplete: every emote whose code CONTAINS [query]
// (case-insensitive), like the Twitch/7TV composer — not merely a prefix — so ":wa" surfaces verosWaving,
// aaoaWat, basedcodeWave, … and not only codes that literally start with "wa". Prefix matches rank first, then
// shorter codes, then alphabetically; the list is capped at [limit] so the dropdown stays compact.
internal fun emoteSuggestions(
    emotes: List<ChatEmoteCatalogue>,
    query: String,
    limit: Int = 12,
): List<ChatEmoteCatalogue> =
    emotes
        .asSequence()
        .filter { it.code.contains(query, ignoreCase = true) }
        .sortedWith(
            compareByDescending<ChatEmoteCatalogue> { it.code.startsWith(query, ignoreCase = true) }
                .thenBy { it.code.length }
                .thenBy { it.code.lowercase() },
        )
        .take(limit)
        .toList()

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
                text = stringResource(Res.string.chat_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.chat_retry)) }
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

/** The chatter's best display name: display name, then login, then the raw id. */
private fun chatterName(message: ChatMessage): String =
    message.displayName.takeIf { it.isNotBlank() }
        ?: message.username.takeIf { it.isNotBlank() }
        ?: message.userId

/**
 * Parse a hex color string ("#RRGGBB" or "#RGB") to a Compose [Color]. Returns null on malformed input so
 * callers can fall back to a theme token.
 */
private fun String.toComposeColor(): Color? = runCatching {
    val hex: String = trimStart('#').let { if (it.length == 3) "${it[0]}${it[0]}${it[1]}${it[1]}${it[2]}${it[2]}" else it }.take(6)
    Color(
        red = hex.substring(0, 2).toInt(16) / 255f,
        green = hex.substring(2, 4).toInt(16) / 255f,
        blue = hex.substring(4, 6).toInt(16) / 255f,
    )
}.getOrNull()

// Format an ISO-8601 UTC timestamp to the viewer's local wall-clock time (HH:mm) — the per-line time the feed
// shows (chat-client.md §0 render contract). Returns null on a malformed timestamp so the row still renders.
private fun formatClockTime(isoUtc: String): String? = runCatching {
    val local = Instant.parse(isoUtc).toLocalDateTime(TimeZone.currentSystemDefault())
    "${local.hour.toString().padStart(2, '0')}:${local.minute.toString().padStart(2, '0')}"
}.getOrNull()

// A small muted chip showing the chatter's resolved pronouns beside their name (chat-client.md §0 render
// contract). The pronoun text is data (already localized by the source), so it carries no i18n key.
@Composable
private fun PronounChip(pronouns: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Text(
        text = pronouns,
        style = typography.sm,
        color = tokens.mutedForeground,
        maxLines = 1,
        modifier = Modifier
            .clip(RoundedCornerShape(4.dp))
            .background(tokens.muted)
            .padding(horizontal = spacing.s1),
    )
}

// ─── Chat mode toggles ───────────────────────────────────────────────────────

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ChatModesBar(
    settings: ChatSettings,
    enabled: Boolean,
    onToggle: (ChatSettings) -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.chat_settings_panel_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            ChatModeChip(
                label = if (settings.slowMode)
                    stringResource(Res.string.chat_settings_slow_delay, settings.slowModeDelay)
                else stringResource(Res.string.chat_settings_slow_mode),
                active = settings.slowMode,
                enabled = enabled,
                onToggle = { onToggle(settings.copy(slowMode = !settings.slowMode)) },
            )
            ChatModeChip(
                label = stringResource(Res.string.chat_settings_sub_only),
                active = settings.subscriberOnly,
                enabled = enabled,
                onToggle = { onToggle(settings.copy(subscriberOnly = !settings.subscriberOnly)) },
            )
            ChatModeChip(
                label = stringResource(Res.string.chat_settings_emote_only),
                active = settings.emotesOnly,
                enabled = enabled,
                onToggle = { onToggle(settings.copy(emotesOnly = !settings.emotesOnly)) },
            )
            ChatModeChip(
                label = if (settings.followersOnly && settings.followersOnlyDuration > 0)
                    stringResource(Res.string.chat_settings_followers_duration, settings.followersOnlyDuration)
                else stringResource(Res.string.chat_settings_followers_only),
                active = settings.followersOnly,
                enabled = enabled,
                onToggle = { onToggle(settings.copy(followersOnly = !settings.followersOnly)) },
            )
        }
    }
}

@Composable
private fun ChatModeChip(
    label: String,
    active: Boolean,
    enabled: Boolean,
    onToggle: () -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Badge(
        selected = active,
        onClick = { if (enabled) onToggle() },
        enabled = enabled,
    ) {
        Text(text = label, style = typography.sm)
    }
}

// ─── Announce dialog ──────────────────────────────────────────────────────────

private val AnnounceColors: List<Pair<String, Color>> = listOf(
    "primary" to Color(0xFF9146FF),
    "blue" to Color(0xFF1E88E5),
    "green" to Color(0xFF2E7D32),
    "orange" to Color(0xFFE65100),
)

@Composable
private fun AnnounceDialog(onDismiss: () -> Unit, onSend: (message: String, color: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var message: String by remember { mutableStateOf("") }
    var selectedColor: String by remember { mutableStateOf("primary") }
    val hasMessage: Boolean = message.isNotBlank()

    val colorLabels: Map<String, String> = mapOf(
        "primary" to stringResource(Res.string.moderation_announce_color_primary),
        "blue" to stringResource(Res.string.moderation_announce_color_blue),
        "green" to stringResource(Res.string.moderation_announce_color_green),
        "orange" to stringResource(Res.string.moderation_announce_color_orange),
    )

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.moderation_announce_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                Textarea(
                    value = message,
                    onValueChange = { message = it },
                    label = stringResource(Res.string.moderation_announce_message_label),
                    isError = !hasMessage && message.isNotEmpty(),
                    errorText = stringResource(Res.string.moderation_announce_message_required),
                    minLines = 2,
                    maxLines = 4,
                    modifier = Modifier.fillMaxWidth(),
                )
                Text(
                    text = stringResource(Res.string.moderation_announce_color_label),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    AnnounceColors.forEach { (key, swatch) ->
                        val isSelected: Boolean = selectedColor == key
                        Box(
                            modifier = Modifier
                                .size(28.dp)
                                .clip(RoundedCornerShape(4.dp))
                                .background(swatch)
                                .then(
                                    if (isSelected) Modifier.padding(2.dp)
                                        .clip(RoundedCornerShape(2.dp))
                                        .background(swatch)
                                    else Modifier
                                )
                                .clickable { selectedColor = key }
                                .semantics {
                                    contentDescription = colorLabels[key] ?: key
                                    role = Role.RadioButton
                                },
                        )
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { if (hasMessage) onSend(message.trim(), selectedColor) }, enabled = hasMessage) {
                Text(stringResource(Res.string.moderation_announce_send))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_announce_dismiss))
            }
        },
    )
}

/** How often the feed re-polls the backend for fresh chat (a window concern, not a design-system token). */
private const val PollIntervalMillis: Long = 4_000L
