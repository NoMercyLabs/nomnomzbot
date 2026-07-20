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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.chat.state.MultiChatController
import bot.nomnomz.dashboard.feature.chat.state.MultiChatState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import androidx.compose.runtime.rememberCoroutineScope
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.multichat_empty_feed
import nomnomzbot.composeapp.generated.resources.multichat_error
import nomnomzbot.composeapp.generated.resources.multichat_loading
import nomnomzbot.composeapp.generated.resources.multichat_picker_hint
import nomnomzbot.composeapp.generated.resources.multichat_picker_none
import nomnomzbot.composeapp.generated.resources.multichat_pick_a_channel
import nomnomzbot.composeapp.generated.resources.shell_nav_multichat
import org.jetbrains.compose.resources.stringResource

// The multi-channel chat-watch page (owner requirement 2026-07-10): a moderator picks several channels they
// own/moderate and watches all their live chats at once in ONE merged, time-ordered feed, each line tagged with
// its channel. The picker toggles a channel on/off (join/leave on a dedicated hub connection); the feed routes
// each live line by its channelId. Read-only monitoring — no composer here; a mod acts on a channel from its own
// Chat page. [hubEvents] is the dedicated multi-watch hub's event stream (kept separate from the main dashboard
// hub so watching extra channels never leaks their chat into the single-channel Chat page).
@Composable
fun MultiChatScreen(
    controller: MultiChatController,
    @Suppress("UNUSED_PARAMETER") role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>,
) {
    val spacing = LocalSpacing.current
    val state: MultiChatState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()

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
                    onToggle = { channel ->
                        if (current.watched.any { it.id == channel.id }) controller.removeChannel(channel.id)
                        else scope.launch { controller.addChannel(channel.id) }
                    },
                )
        }
    }
}

@Composable
private fun ReadyContent(ready: MultiChatState.Ready, onToggle: (ChannelSummary) -> Unit) {
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
            else -> MergedFeed(messages = ready.messages, nameByChannel = nameByChannel)
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
private fun MergedFeed(messages: List<ChatMessage>, nameByChannel: Map<String, String>) {
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
                MultiChatRow(message = msg, channelName = nameByChannel[msg.channelId])
            }
        }
    }
}

// One compact monitoring line: time · channel tag · provider tag · colored name · message text. Deliberately
// lighter than the single-channel MessageRow (no per-line moderation actions) — this surface is for WATCHING
// many channels at once; a mod acts on a specific channel from its own Chat page.
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MultiChatRow(message: ChatMessage, channelName: String?) {
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
