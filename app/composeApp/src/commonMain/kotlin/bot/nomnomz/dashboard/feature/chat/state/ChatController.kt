// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatApi
import bot.nomnomz.dashboard.core.network.ChatBadge
import bot.nomnomz.dashboard.core.network.ChatCheermote
import bot.nomnomz.dashboard.core.network.ChatEmote
import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue
import bot.nomnomz.dashboard.core.network.ChatFragment
import bot.nomnomz.dashboard.core.network.ChatGif
import bot.nomnomz.dashboard.core.network.ChatMention
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.network.ChatSettings
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.NetworkBanResult
import bot.nomnomz.dashboard.core.network.ShieldStatus
import bot.nomnomz.dashboard.core.realtime.HubChannelEvent
import bot.nomnomz.dashboard.core.realtime.HubChatMessage
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.decodeFromJsonElement

// The Chat page's state-holder (frontend-ia.md §3 — the Chat group). Resolves the active channel, then loads
// its real recent chat from the backend (persisted from EventSub `channel.chat.message`; no fabricated lines).
// It also drives the page's live actions — send a message as the bot, delete a single message, timeout a
// chatter — each of which re-loads on success so the feed always reflects the backend's truth. The screen
// renders [state]; a retry / reconnect (or a poll tick) calls [load] again.
//
// Real-time: [subscribeToHub] can be called once after [load] to forward live ChatMessage hub invocations
// from DashboardHubClient directly into the Ready state so new messages appear without a poll. The existing
// poll path remains the fallback when no hub client is wired.
class ChatController(
    private val channelsApi: ChannelsApi,
    private val chatApi: ChatApi,
    // The channel's emergency Shield Mode read/write (S076c) — the SAME ModerationApi.shieldMode /
    // setShieldMode the Moderation -> Desk toggle already calls, so there is exactly one way to flip Shield
    // Mode in the dashboard. Optional and nullable like [ModerationController]'s equally-optional
    // dependencies: a state-holder test that does not exercise Shield Mode omits it, and the toggle then
    // stays hidden rather than rendering a state nobody read.
    private val moderationApi: ModerationApi? = null,
) {
    private val _state: MutableStateFlow<ChatState> = MutableStateFlow(ChatState.Loading)

    /** The page render state: loading / ready (with the messages) / empty / error. */
    val state: StateFlow<ChatState> = _state.asStateFlow()

    // The channel the reads/writes target — [load] re-resolves it on every call and stores it here so every
    // action (send / delete / timeout / ban / announce) reuses the CURRENTLY-selected channel. Null until the
    // first successful resolve; updated whenever the operator switches channels via the switcher.
    private var channelId: String? = null

    // The message the composer is currently replying to (FFZ-style reply mode), or null for a normal send. This is
    // UI state, but it lives here so [send] threads the parent id to the backend and clears it on a successful
    // send — a single, testable seam rather than scattering the reply id across the screen and the send call.
    private val _replyTarget: MutableStateFlow<ChatMessage?> = MutableStateFlow(null)

    /** The message being replied to, or null when not in reply mode. The composer renders its banner off this. */
    val replyTarget: StateFlow<ChatMessage?> = _replyTarget.asStateFlow()

    /**
     * Re-resolve the active channel, then load its recent chat. The channel is resolved on EVERY call so a
     * switcher change re-targets the feed — and every subsequent moderation action, which all reuse [channelId].
     * When the resolved channel differs from the one currently loaded, the previous channel's feed is dropped to
     * Loading so its messages / settings / emotes never bleed into the newly-selected channel; a same-channel
     * reload (poll tick or post-action reload) keeps the feed on screen without flashing Loading. A resolve
     * failure surfaces as Error.
     */
    suspend fun load() {
        val resolved: String =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ChatState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value.id
            }
        // A switch to a different channel clears the prior feed so nothing bleeds across tenants.
        if (resolved != channelId) {
            channelId = resolved
            _state.value = ChatState.Loading
        }

        when (val result: ApiResult<List<ChatMessage>> = chatApi.messages(resolved)) {
            is ApiResult.Failure -> {
                val current: ChatState = _state.value
                // A refresh failure over an existing feed surfaces the error without dropping the messages;
                // a first-load failure (nothing rendered yet) becomes the full Error state.
                _state.value =
                    if (current is ChatState.Ready) current.copy(actionError = result.error.message)
                    else ChatState.Error(result.error.message)
            }
            is ApiResult.Ok -> {
                val current: ChatState = _state.value
                val existingSettings: ChatSettings? =
                    if (current is ChatState.Ready) current.settings else null
                _state.value =
                    if (result.value.isEmpty()) ChatState.Empty
                    else ChatState.Ready(result.value, settings = existingSettings)
            }
        }
        // Load settings + the composer emote catalogue + Shield Mode's current state on first load (once each);
        // Shield Mode also stays live afterward via [applyShieldModeEvent] over the hub push, so it is never
        // re-polled on every refresh like the messages themselves are.
        val fresh: ChatState = _state.value
        if (fresh is ChatState.Ready) {
            if (fresh.settings == null) loadSettings()
            if (fresh.emotes.isEmpty()) loadEmotes()
            // Retry whenever the state isn't a confirmed reading yet (never loaded, or the last read failed) —
            // unlike settings/emotes this can legitimately flip on the backend at any time (Twitch's own
            // automated trigger), so a transient failure (missing scope not yet re-granted) is worth re-checking
            // on the next poll tick rather than staying stuck unavailable for the rest of the page's life.
            if (fresh.shieldEnabled == null || !fresh.shieldAvailable) loadShieldMode(resolved)
        }
    }

    /**
     * Load emergency Shield Mode's current state (S076c) for [channel] into the Ready state. A failure means
     * unavailable here — NOT "off" (a phantom lie) — [ChatState.Ready.shieldAvailable] flips false so the screen
     * shows a needs-permission notice instead of an off toggle, mirroring [ModerationController]'s identical
     * read. No-ops when no [moderationApi] is wired (a state-holder test that never exercises Shield Mode).
     */
    private suspend fun loadShieldMode(channel: String) {
        val api: ModerationApi = moderationApi ?: return
        when (val result: ApiResult<ShieldStatus> = api.shieldMode(channel)) {
            is ApiResult.Ok -> {
                val current: ChatState = _state.value
                if (current is ChatState.Ready) {
                    _state.value = current.copy(shieldEnabled = result.value.enabled, shieldAvailable = true)
                }
            }
            is ApiResult.Failure -> {
                val current: ChatState = _state.value
                if (current is ChatState.Ready) {
                    _state.value = current.copy(shieldEnabled = false, shieldAvailable = false)
                }
            }
        }
    }

    /**
     * Turn emergency Shield Mode on or off ([enabled]) for the active channel — the SAME
     * `PATCH .../moderation/shield` route the Moderation -> Desk toggle calls
     * ([ModerationApi.setShieldMode]). Re-reads the fresh state on success so the toggle reflects the backend's
     * truth; surfaces the error over the intact feed on failure. No-ops when no channel is loaded or no
     * [moderationApi] is wired.
     */
    suspend fun setShieldMode(enabled: Boolean) {
        val channel: String = channelId ?: return
        val api: ModerationApi = moderationApi ?: return
        when (val result: ApiResult<Unit> = api.setShieldMode(channel, enabled)) {
            is ApiResult.Ok -> loadShieldMode(channel)
            is ApiResult.Failure -> failAction(result.error.message)
        }
    }

    /** Load the channel's usable emotes for the composer autocomplete (best-effort, once). */
    suspend fun loadEmotes() {
        val channel: String = channelId ?: return
        when (val result: ApiResult<List<ChatEmoteCatalogue>> = chatApi.emotes(channel)) {
            is ApiResult.Failure -> Unit
            is ApiResult.Ok -> {
                val current: ChatState = _state.value
                if (current is ChatState.Ready) _state.value = current.copy(emotes = result.value)
            }
        }
    }

    /**
     * Subscribe to [hubEvents]:
     * - [HubEvent.ChatMessage]: appends the new line to the feed so it appears at the bottom instantly, without
     *   waiting for a poll tick. If the feed is in Empty / Loading / Error state when the first live message
     *   arrives, we bootstrap a fresh Ready list — so chat works even when there's no history to load.
     * - [HubEvent.ChannelEvent]: a `shield_mode_begin` / `shield_mode_end` push (S076c — Twitch's own automated
     *   protection-mode toggle) for the active channel updates [ChatState.Ready.shieldEnabled] live, the same
     *   generic `ChannelEventDto` wire shape [MultiChatController] already handles for the multi-watch lane.
     *
     * Must be called from a coroutine scope that outlives the page (e.g. the screen's LaunchedEffect). The
     * subscription is cancelled when that scope cancels — no explicit teardown.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            when (evt) {
                is HubEvent.ChatMessage -> {
                    val newLine: ChatMessage = evt.message.toLocalMessage()
                    val current: ChatState = _state.value
                    val ready: ChatState.Ready =
                        when (current) {
                            is ChatState.Ready -> current
                            // No history yet — bootstrap a fresh feed; the first message makes the page live.
                            else -> ChatState.Ready(messages = emptyList())
                        }
                    // Skip a NON-BLANK id already in the feed. EventSub is at-least-once (redelivers
                    // channel.chat.message on a WS reconnect), the operator's own line echoes back, and the
                    // safety-net poll re-fetches the same persisted lines — and the feed's LazyColumn is keyed by
                    // id, so a duplicate key would crash the page. A blank id can't be de-duped, so it always
                    // appends: never suppress a live line just because its id failed to resolve.
                    if (newLine.id.isNotEmpty() && ready.messages.any { it.id == newLine.id }) return@collect
                    // Append (newest at bottom) and cap at 200 so the list stays bounded.
                    val capped: List<ChatMessage> = (ready.messages + newLine).takeLast(200)
                    _state.value = ready.copy(messages = capped)
                }
                is HubEvent.ChannelEvent -> {
                    applyShieldModeEvent(evt.event)
                    applyChatModerationEvent(evt.event)
                }
                else -> Unit
            }
        }
    }

    // Shield Mode begin/end arrives as a generic ChannelEvent (type "shield_mode_begin" / "shield_mode_end"),
    // never a dedicated HubEvent case — see [MultiChatController.applyShieldModeEvent], the same push routed
    // through the same backend ChannelEventDto wire shape. Ignored for any channel other than the one currently
    // on screen, and only applied once Shield Mode's initial state has actually been loaded (a push for a page
    // that never read Shield Mode yet has nothing meaningful to update).
    private fun applyShieldModeEvent(event: HubChannelEvent) {
        if (event.broadcasterId != channelId) return
        val current: ChatState = _state.value
        if (current !is ChatState.Ready || current.shieldEnabled == null) return
        when (event.type) {
            "shield_mode_begin" -> _state.value = current.copy(shieldEnabled = true, shieldAvailable = true)
            "shield_mode_end" -> _state.value = current.copy(shieldEnabled = false, shieldAvailable = true)
        }
    }

    // A message removed anywhere (a moderator/AutoMod delete, a whole-channel clear, or a targeted per-chatter
    // purge) must disappear from THIS feed the same instant it disappears from Twitch's own chat — owner report
    // 2026-09-09: it was staying on screen. Same generic ChannelEvent wire shape as Shield Mode, decoded per
    // [ChatModerationJson] since ChannelEventDto.Data is a different DTO shape per event type.
    private fun applyChatModerationEvent(event: HubChannelEvent) {
        if (event.broadcasterId != channelId) return
        val current: ChatState = _state.value
        if (current !is ChatState.Ready) return
        val data: JsonElement = event.data ?: return
        when (event.type) {
            "chat_cleared" -> _state.value = current.copy(messages = emptyList())
            "message_deleted" -> {
                val payload: MessageDeletedPayload =
                    runCatching { ChatModerationJson.decodeFromJsonElement<MessageDeletedPayload>(data) }
                        .getOrNull() ?: return
                _state.value =
                    current.copy(messages = current.messages.filterNot { it.id == payload.messageId })
            }
            "user_messages_cleared" -> {
                val payload: UserMessagesClearedPayload =
                    runCatching { ChatModerationJson.decodeFromJsonElement<UserMessagesClearedPayload>(data) }
                        .getOrNull() ?: return
                _state.value =
                    current.copy(messages = current.messages.filterNot { it.userId == payload.targetUserId })
            }
        }
    }

    /**
     * Send [message] to chat as [senderIdentity] — "you" (the logged-in operator's own account, the default) or
     * "bot" (the channel's bot identity), per chat-client.md §3.1. Does NOT reload the feed: the sent line comes
     * straight back over the live hub (EventSub echoes it), so reloading here would race persistence and clobber
     * the hub-appended line — the "one message late" bug. On failure, surface the error over the current feed
     * without disturbing it.
     */
    suspend fun send(message: String, senderIdentity: String = "you") {
        val trimmed: String = message.trim()
        if (trimmed.isEmpty()) return
        val channel: String = channelId ?: return failAction(NoChannelError)
        // In reply mode, thread the parent message id so the backend posts a Twitch reply; a normal send is null.
        val replyToMessageId: String? = _replyTarget.value?.id
        when (
            val result: ApiResult<Unit> = chatApi.send(channel, trimmed, senderIdentity, replyToMessageId)
        ) {
            // Leave reply mode only once the reply actually lands — a failed send keeps the target so the operator
            // can retry without re-selecting the message.
            is ApiResult.Ok -> _replyTarget.value = null
            is ApiResult.Failure -> failAction(result.error.message)
        }
    }

    /** Enter reply mode: the next [send] threads [message]'s id as the reply parent. The composer shows a banner. */
    fun startReply(message: ChatMessage) {
        _replyTarget.value = message
    }

    /** Leave reply mode without sending — the composer's ✕ and a successful [send] both clear the target. */
    fun cancelReply() {
        _replyTarget.value = null
    }

    /** Delete the single message [messageId], then reload so it drops from the feed. The screen confirms first. */
    suspend fun deleteMessage(messageId: String) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        afterAction(chatApi.deleteMessage(channel, messageId))
    }

    /** Timeout [userId] for [durationSeconds], then reload. The screen confirms this first (destructive). */
    suspend fun timeout(userId: String, durationSeconds: Int = ChatApi.DEFAULT_TIMEOUT_SECONDS) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        afterAction(chatApi.timeout(channel, userId, durationSeconds))
    }

    /**
     * Ban [userId] — in this channel only ([scope] = "this_channel") or across every channel the operator moderates
     * ([scope] = "all_moderated"). The screen confirms first (destructive). On success the ban lands via Twitch (no
     * feed reload — a banned chatter simply stops appearing); a network failure surfaces over the intact feed.
     */
    suspend fun ban(userId: String, scope: String, reason: String? = null) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        when (val result: ApiResult<NetworkBanResult> = chatApi.banUser(channel, userId, scope, reason)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> failAction(result.error.message)
        }
    }

    /**
     * File a viewer report against [userId] ([userName]/[displayName] captured from the chat row) with [reason] —
     * flags them for a moderator to triage on the Moderation page. Non-punishing (no feed reload); a failure surfaces
     * over the intact feed. The screen collects the reason and won't submit it blank.
     */
    suspend fun report(userId: String, userName: String, displayName: String?, reason: String) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        when (val result: ApiResult<Unit> = chatApi.fileReport(channel, userId, userName, displayName, reason)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> failAction(result.error.message)
        }
    }

    /** Load the channel's current chat settings and merge them into the Ready state. */
    suspend fun loadSettings() {
        val channel: String = channelId ?: return
        when (val result: ApiResult<ChatSettings> = chatApi.settings(channel)) {
            is ApiResult.Failure -> Unit
            is ApiResult.Ok -> {
                val current: ChatState = _state.value
                if (current is ChatState.Ready) {
                    _state.value = current.copy(settings = result.value)
                }
            }
        }
    }

    /** Persist [settings] to the backend, then update the in-memory state. */
    suspend fun updateSettings(settings: ChatSettings) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        when (val result: ApiResult<ChatSettings> = chatApi.updateSettings(channel, settings)) {
            is ApiResult.Failure -> failAction(result.error.message)
            is ApiResult.Ok -> {
                val current: ChatState = _state.value
                if (current is ChatState.Ready) {
                    _state.value = current.copy(settings = result.value)
                }
            }
        }
    }

    /** Post a Twitch announcement. [color]: "primary" | "blue" | "green" | "orange". */
    suspend fun announce(message: String, color: String = "primary") {
        val channel: String = channelId ?: return failAction(NoChannelError)
        afterAction(chatApi.announce(channel, message, color))
    }

    // An action either reloads the feed (success) or surfaces its error over the current Ready list without
    // losing it (failure) — so a failed send / delete / timeout leaves the page intact with a visible reason.
    private suspend fun afterAction(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failAction(result.error.message)
        }
    }

    private fun failAction(detail: String) {
        val current: ChatState = _state.value
        _state.value =
            if (current is ChatState.Ready) current.copy(actionError = detail)
            else ChatState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

// ─── Hub adapter ─────────────────────────────────────────────────────────────

internal fun HubChatMessage.toLocalMessage(): ChatMessage =
    ChatMessage(
        id = id,
        channelId = channelId,
        userId = userId,
        username = username,
        displayName = displayName,
        userType = userType,
        color = color,
        message = message,
        messageType = messageType,
        isCommand = isCommand,
        isCheer = isCheer,
        bitsAmount = if (bitsAmount > 0) bitsAmount else null,
        replyToMessageId = replyToMessageId,
        replyParentMessageBody = replyParentMessageBody,
        replyParentUserName = replyParentUserName,
        timestamp = timestamp,
        fragments = fragments.map { f ->
            ChatFragment(
                type = f.type,
                text = f.text,
                emote = f.emote?.let { e ->
                    ChatEmote(
                        id = e.id,
                        setId = e.setId,
                        format = e.format,
                        provider = e.provider,
                        urls = e.urls,
                        animated = e.animated,
                        zeroWidth = e.zeroWidth,
                    )
                },
                cheermote = f.cheermote?.let { c ->
                    ChatCheermote(
                        prefix = c.prefix,
                        bits = c.bits,
                        tier = c.tier,
                        urls = c.urls,
                        animated = c.animated,
                        colorHex = c.colorHex,
                    )
                },
                mention = f.mention?.let { m ->
                    ChatMention(
                        userId = m.userId,
                        username = m.username,
                        displayName = m.displayName,
                        color = m.color,
                    )
                },
                linkUrl = f.linkUrl,
                gif = f.gif?.let { g -> ChatGif(gifId = g.gifId, url = g.url) },
            )
        },
        badges = badges.map { b ->
            ChatBadge(
                setId = b.setId,
                id = b.id,
                info = b.info,
                urls = b.urls,
            )
        },
        avatarUrl = avatarUrl,
        pronouns = pronouns,
        provider = provider,
    )

/** The Chat page render state. */
sealed interface ChatState {
    data object Loading : ChatState

    /**
     * The channel's recent chat is listed (oldest first). [settings] is loaded once on first render.
     * [actionError] is non-null only when the last action (send/delete/timeout/announce/settings-change)
     * failed — the screen surfaces it as a transient banner while keeping the feed rendered.
     *
     * [shieldEnabled] is emergency Shield Mode's current state (S076c) — null until [ChatController] has loaded
     * it once (the toggle stays hidden until then, like [settings]); [shieldAvailable] is false when that read
     * (or the last write) failed here (missing scope / bot not installed on this channel) — NOT reported as
     * "off", so the screen can show a needs-permission notice instead of a phantom off toggle.
     */
    data class Ready(
        val messages: List<ChatMessage>,
        val settings: ChatSettings? = null,
        val actionError: String? = null,
        val emotes: List<ChatEmoteCatalogue> = emptyList(),
        val shieldEnabled: Boolean? = null,
        val shieldAvailable: Boolean = true,
    ) : ChatState

    data object Empty : ChatState

    data class Error(val detail: String) : ChatState
}
