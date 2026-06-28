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
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
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
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
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
import bot.nomnomz.dashboard.core.network.ChatMessage
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
import nomnomzbot.composeapp.generated.resources.chat_delete_action
import nomnomzbot.composeapp.generated.resources.chat_delete_action_short
import nomnomzbot.composeapp.generated.resources.chat_delete_confirm
import nomnomzbot.composeapp.generated.resources.chat_delete_dismiss
import nomnomzbot.composeapp.generated.resources.chat_delete_message
import nomnomzbot.composeapp.generated.resources.chat_delete_title
import nomnomzbot.composeapp.generated.resources.chat_empty
import nomnomzbot.composeapp.generated.resources.chat_subtitle
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

    // One decision for the whole page: Chat gates every write control (send, per-message moderation) at its
    // single Moderator manage floor (frontend-ia.md §3). A caller below it still reads the live feed, but the
    // send and moderation affordances render disabled with "Requires Moderator" (§7); the backend re-checks
    // every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Chat)

    // Load once, then poll the backend on an interval so the feed stays fresh without a hub connection.
    LaunchedEffect(Unit) {
        controller.load()
        while (true) {
            delay(PollIntervalMillis)
            controller.load()
        }
    }

    // When the hub is connected, stream live ChatMessage invocations into the controller so new lines
    // appear instantly — no poll tick needed. Runs concurrently with the poll loop; the poll remains the
    // fallback/initial source of truth and also handles deletes/timeouts that the hub doesn't re-emit.
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Column(modifier = Modifier.fillMaxSize().padding(spacing.s6), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_chat),
            subtitle = stringResource(Res.string.chat_subtitle),
        )
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
            Text(
                text = stringResource(Res.string.chat_action_error, detail),
                style = typography.sm,
                color = tokens.destructive,
                modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
            )
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

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.Top,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        // The chatter's name + message read as one node; the controls below carry their own action labels.
        Row(
            modifier = Modifier.weight(1f).clearAndSetSemantics { contentDescription = rowDescription },
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.Top,
        ) {
            Text(
                text = name,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.widthIn(max = spacing.s24 * 1.5f),
            )
            Text(
                text = message.message,
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f),
            )
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
            TextButton(
                onClick = { expanded = true },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = menuLabel },
            ) {
                Text(text = "···", style = typography.sm, color = tokens.mutedForeground, maxLines = 1)
            }
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
    val typography = LocalTypography.current

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
        OutlinedTextField(
            value = draft,
            onValueChange = { draft = it },
            modifier = Modifier.weight(1f),
            singleLine = true,
            placeholder = {
                Text(text = stringResource(Res.string.chat_send_placeholder), style = typography.sm)
            },
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

/** How often the feed re-polls the backend for fresh chat (a window concern, not a design-system token). */
private const val PollIntervalMillis: Long = 4_000L
