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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.DotsHorizontalGlyph
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import coil3.compose.AsyncImage
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
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
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.chat_action_error
import nomnomzbot.composeapp.generated.resources.chat_announce_action
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
import nomnomzbot.composeapp.generated.resources.chat_retry
import nomnomzbot.composeapp.generated.resources.chat_row_actions
import nomnomzbot.composeapp.generated.resources.chat_send_action
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
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    var showAnnounce: Boolean by remember { mutableStateOf(false) }

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Chat)

    // Load the initial history once. When the hub is connected, new messages arrive via [subscribeToHub] in
    // real-time — no polling needed. Polling only runs as a fallback when the hub is not wired so the page
    // still works in environments without a live SignalR connection (e.g. local dev without a hub).
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
                    )
            }
        }
        SendBox(manage = manage, onSend = { text -> scope.launch { controller.send(text) } })
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
                MessageRow(message = message, manage = manage, onDelete = onDelete, onTimeout = onTimeout)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MessageRow(
    message: ChatMessage,
    manage: ManageDecision,
    onDelete: (messageId: String) -> Unit,
    onTimeout: (userId: String) -> Unit,
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
        // Badges + name + message fragments all flow inline — semantics collapses to a single read.
        FlowRow(
            modifier = Modifier.weight(1f).clearAndSetSemantics { contentDescription = rowDescription },
            horizontalArrangement = Arrangement.spacedBy(spacing.s1),
            verticalArrangement = Arrangement.Center,
        ) {
            // Badge strip — 18 dp per badge.
            message.badges.forEach { badge ->
                val url: String? = badge.urls["2"] ?: badge.urls["1"] ?: badge.urls.values.firstOrNull()
                if (url != null) {
                    AsyncImage(
                        model = url,
                        contentDescription = badge.setId,
                        modifier = Modifier.size(18.dp).align(Alignment.CenterVertically),
                    )
                }
            }
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
                                AsyncImage(
                                    model = url,
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
                                AsyncImage(
                                    model = url,
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
                            Text(text = fragment.text, style = typography.sm, color = tokens.cardForeground)
                        }
                    }
                }
            } else {
                Text(text = message.message, style = typography.sm, color = tokens.cardForeground)
            }
        }
        MessageActions(message = message, name = name, manage = manage, onDelete = onDelete, onTimeout = onTimeout)
    }
}

// The per-message moderation menu: a labelled trigger that opens the closed menu of quick-actions (delete this
// message, timeout the author). Both are destructive, so each routes through the shared ConfirmDialog before
// it fires; the screen owns the pending-confirmation state here so the menu closes the instant one is chosen.
@Composable
private fun MessageActions(
    message: ChatMessage,
    name: String,
    manage: ManageDecision,
    onDelete: (messageId: String) -> Unit,
    onTimeout: (userId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    var confirmDelete: Boolean by remember { mutableStateOf(false) }
    var confirmTimeout: Boolean by remember { mutableStateOf(false) }

    val menuLabel: String = stringResource(Res.string.chat_row_actions, name)
    val deleteItemLabel: String = stringResource(Res.string.chat_delete_action)
    val timeoutItemLabel: String = stringResource(Res.string.chat_timeout_action, name)

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
}

// The send composer: a single-line input that posts the typed line as the bot and clears on send. Empty / blank
// input is ignored (the send affordance does nothing), matching the backend's empty-message rejection.
@Composable
private fun SendBox(manage: ManageDecision, onSend: (message: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var draft: String by remember { mutableStateOf("") }
    val canSend: Boolean = draft.isNotBlank()

    fun submit() {
        if (canSend) {
            onSend(draft)
            draft = ""
        }
    }

    val sendLabel: String = stringResource(Res.string.chat_send_action)

    Row(
        modifier = Modifier.fillMaxWidth().padding(top = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        AppTextField(
            value = draft,
            onValueChange = { draft = it },
            label = "",
            modifier = Modifier.weight(1f),
            placeholder = stringResource(Res.string.chat_send_placeholder),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
            keyboardActions = KeyboardActions(onSend = { submit() }),
        )
        ManageGate(decision = manage) { gateEnabled ->
            // Send is offered only when the gate allows it AND the draft is non-blank; the keyboard Send action
            // above goes through the same `submit()` guard but the visible affordance reflects the floor.
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

// ─── Chat mode toggles ───────────────────────────────────────────────────────

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ChatModesBar(
    settings: ChatSettings,
    enabled: Boolean,
    onToggle: (ChatSettings) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(8.dp))
            .background(tokens.muted)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
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
