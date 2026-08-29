// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.realtime

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long

// ASP.NET Core SignalR JSON Hub Protocol — record separator byte between frames.
private const val RECORD_SEPARATOR: Char = ''

// SignalR message types (hub protocol spec, incl. the stateful-reconnect Ack/Sequence pair —
// https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md).
private const val TYPE_INVOCATION: Int = 1
private const val TYPE_PING: Int = 6
private const val TYPE_CLOSE: Int = 7
private const val TYPE_ACK: Int = 8
private const val TYPE_SEQUENCE: Int = 9

// Client keep-alive cadence. The SignalR server evicts a connection it hasn't heard from within its
// ClientTimeoutInterval (default 30 s) — and server→client frames do NOT reset that timer, only client→server
// traffic does — so the client must send its own protocol ping well under the timeout to hold the socket open.
private const val PingIntervalMillis: Long = 15_000

// A dead connection often never surfaces as a close/error on the client — a container replaced during a
// deploy, or a proxy that silently drops an idle socket, can leave hubSocket.receive() suspended forever
// with no close frame ever arriving. Without a read-timeout the reconnect loop never runs again: the socket
// looks "open" locally while the server side is long gone (the exact "no reconnect after a server reboot"
// symptom). Generous multiple of the ping cadence so ordinary network jitter never trips it.
private const val ReceiveTimeoutMillis: Long = PingIntervalMillis * 4

// How long a resumed connection has to prove itself alive (one confirming frame from the server) before we
// give up on the resume attempt and fall back to a fresh connect. Short — a resume that's going to work
// responds almost immediately; a longer wait would just delay the clean-fallback path on a truly refused resume.
private const val ResumeConfirmTimeoutMillis: Long = 5_000

/**
 * Thin SignalR hub client targeting the backend `DashboardHub` at `/hubs/dashboard`.
 *
 * The raw text transport is a [HubSocket] (expect/actual): the browser's native WebSocket on wasmJs — where
 * Ktor's WebSockets plugin never opens a socket on the Fetch engine, which is why the web dashboard's live
 * push previously never connected — and Ktor's CIO WebSocket on jvm/desktop. Everything here (handshake,
 * JoinChannel, keep-alive ping, `\x1e`-framing, dispatch, reconnect) is shared over that transport.
 *
 * Lifecycle:
 * - Call [connect] to open the WebSocket, complete the handshake, and join a channel group.
 * - Collect [events] to receive hub invocations dispatched by the server.
 * - Call [disconnect] to close gracefully (or let the scope cancel).
 *
 * Reconnection & stateful resume: the server runs `WithStatefulReconnect()` (861caccf). This client opts in
 * on the handshake (`useStatefulReconnect: true`) and implements the hub protocol's Ack/Sequence pair
 * (types 8/9): outbound invocations (JoinChannel/LeaveChannel) are numbered and buffered until acked; a
 * reconnect after a prior successful connection sends a `Sequence` message FIRST and resends only the
 * still-unacked buffered invocations, instead of rejoining from scratch — the server's persisted hub
 * context means groups already joined don't need to be rejoined. Inbound invocations are numbered the same
 * way so a message the server resends after a resume (it doesn't know exactly what got through) is
 * recognised as a duplicate and dropped without being redelivered to [events]. If the resumed connection
 * never proves itself alive within [ResumeConfirmTimeoutMillis], the resume state is reset and the NEXT
 * attempt falls back to a plain fresh connect (full JoinChannel replay) — never a stuck or falsely-resumed
 * connection. Back-off is exponential, capped at 30 s; only a deliberate [disconnect] stops the loop.
 *
 * Thread safety: all mutations are confined to the internal [scope] launched on [Dispatchers.Default]; the
 * outbound sequence/buffer state is additionally guarded by [outboundMutex] since [join]/[leave] send from
 * their own launched coroutines concurrently with the connect loop.
 */
class DashboardHubClient {

    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var connectJob: Job? = null
    private var currentChannelId: String? = null
    private var socket: HubSocket? = null

    // The set of channel groups this ONE connection is subscribed to. A single-channel consumer leaves it at
    // {primary}; the multi-watch surface adds more via [join] / drops them via [leave]. Guarded by [joinMutex]
    // because it is read on the connect coroutine (to (re)join every channel after a handshake) and mutated from
    // [join]/[leave]/[connect]. On any FRESH (non-resumed) connect the FULL set is re-joined, so a dropped socket
    // restores every watched channel, not just the last one.
    private val joinMutex: Mutex = Mutex()
    private val joinedChannels: MutableSet<String> = mutableSetOf()

    // ── Stateful-reconnect bookkeeping (hub protocol Ack/Sequence, types 8/9) ─────────────────────────────
    // Outbound: every tracked invocation (Join/Leave — NOT pings, which carry no state worth resending) gets
    // the next sequence id and is buffered until an inbound Ack covers it. On a resumed connect the buffer is
    // resent verbatim, in order, after our own Sequence(nextOutboundSeq) message.
    private val outboundMutex: Mutex = Mutex()
    private var nextOutboundSeq: Long = 1
    private val outboundBuffer: ArrayDeque<Pair<Long, String>> = ArrayDeque()

    // True once ANY connection attempt has fully established (handshake + join/resume + confirmed alive).
    // The NEXT attempt is treated as a resume candidate only while this stays true; a failed resume attempt
    // resets it (see [resetResumeState]) so the following attempt falls back to a plain fresh connect.
    private var hasEstablishedBefore: Boolean = false

    // Inbound: the id we expect the NEXT invocation frame to carry, seeded from the server's own Sequence
    // message on a resumed connection (absent on a fresh connection — inbound numbering then starts at 1
    // implicitly). [inboundHighestProcessed] is the highest id ever actually dispatched to [events]; an
    // incoming id at or below it is a resend of something we already delivered and must be dropped, not
    // redelivered — this state survives across reconnects (never reset by [resetResumeState]) since it
    // records what the APPLICATION has seen, not the connection's health.
    private var inboundNextExpected: Long = 1
    private var inboundHighestProcessed: Long = 0

    private val _events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 64)

    /** Hub invocations received from the server after a successful [connect] + `JoinChannel`. */
    val events: SharedFlow<HubEvent> = _events.asSharedFlow()

    /** True while the WebSocket is connected and the handshake is complete. */
    var isConnected: Boolean = false
        private set

    private val _connectionState: MutableStateFlow<HubConnectionState> =
        MutableStateFlow(HubConnectionState.Disconnected)

    /**
     * The shell's truthful hub-health signal (S050 — "shell truth"): [HubConnectionState.Connected] only while
     * the handshake is actually complete, [HubConnectionState.Reconnecting] the instant a session drops or a
     * connect/resume attempt is in flight, and [HubConnectionState.Disconnected] only after an explicit
     * [disconnect] (or before the first [connect]). A UI observing this — not the bare [isConnected] flag, which
     * has no "currently retrying" state — can show a dead-vs-live indicator that updates within one
     * [ReceiveTimeoutMillis] window of a real drop, never a static "always green" dot.
     */
    val connectionState: StateFlow<HubConnectionState> = _connectionState.asStateFlow()

    /**
     * Open the WebSocket to `{baseUrl}/hubs/dashboard`, complete the SignalR handshake, invoke
     * `JoinChannel({channelId})`, then stream incoming hub invocations into [events].
     *
     * Re-entrant per channel: a repeat call for the SAME channel while connected is a no-op; a call for a
     * DIFFERENT channel tears down the current connection and rejoins the new channel's group (so the feed
     * follows the operator's active channel instead of staying stuck on the first one it ever joined) — this
     * is a deliberate new session, so it also resets all stateful-reconnect bookkeeping.
     *
     * [tokenProvider] is read on EVERY (re)connect, never captured once: the REST layer rotates the JWT on a
     * 401, so a reconnect must send the CURRENT token or the socket strands on a stale one and every retry 401s.
     *
     * [refreshToken] refreshes the JWT (POST /auth/refresh) and returns true when a fresh one was stored. A raw
     * WebSocket has no HTTP interceptor, so when the handshake fails on an expired token — the common case for
     * an idle chat page where no REST call has fired to refresh — the client would otherwise retry with the same
     * expired token forever. We call it before each retry that failed to establish, so an expired token self-heals.
     */
    fun connect(
        baseUrl: String,
        tokenProvider: () -> String?,
        channelId: String,
        refreshToken: (suspend () -> Boolean)? = null,
    ) {
        if (connectJob?.isActive == true && currentChannelId == channelId) return
        connectJob?.cancel()
        currentChannelId = channelId
        connectJob =
            scope.launch {
                // Reset the joined set to just this primary channel — a fresh connect targets one channel; the
                // multi-watch surface layers extra channels on top afterwards via [join]. A deliberate new
                // session also resets stateful-reconnect bookkeeping — there is nothing to resume across a
                // channel switch.
                joinMutex.withLock {
                    joinedChannels.clear()
                    joinedChannels.add(channelId)
                }
                outboundMutex.withLock { resetResumeStateLocked() }
                inboundNextExpected = 1
                inboundHighestProcessed = 0
                var backoffMs: Long = 1_000
                // The very first attempt of a brand-new session is still "trying to connect", not "dead" — there
                // was never a live connection to have dropped. Reconnecting is the correct truthful state for
                // both cases (an indicator has no need to distinguish "never connected yet" from "lost it").
                _connectionState.value = HubConnectionState.Reconnecting
                while (true) {
                    var established = false
                    // Reset the back-off once a session actually establishes, so a long-lived socket that later
                    // drops reconnects promptly instead of inheriting a grown delay from an earlier failure run.
                    runCatching {
                            openSession(baseUrl, tokenProvider) {
                                established = true
                                backoffMs = 1_000
                                _connectionState.value = HubConnectionState.Connected
                            }
                        }
                        .onFailure { /* swallowed — the reconnect loop below handles it */ }
                    isConnected = false
                    // The attempt ended (dropped, refused, or never established) and the loop is about to retry —
                    // truthfully reflect that as Reconnecting rather than leaving the last Connected value stale.
                    _connectionState.value = HubConnectionState.Reconnecting
                    // The session never established this attempt — overwhelmingly an expired/absent JWT (the
                    // handshake upgrade 401s), or a refused resume. Reset resume bookkeeping so the NEXT attempt
                    // falls back to a plain fresh connect instead of retrying a resume the server won't honour.
                    if (!established) {
                        outboundMutex.withLock { resetResumeStateLocked() }
                        refreshToken?.invoke()
                    }
                    // Reconnect loop — honour back-off so we don't spam the server on flaky networks.
                    delay(backoffMs)
                    backoffMs = (backoffMs * 2).coerceAtMost(30_000)
                }
            }
    }

    /**
     * Add [channelId] to the set of channels this connection watches, joining its group on the live socket now
     * (and on every future reconnect). Used by the multi-watch chat surface to monitor several channels over ONE
     * connection — each [HubEvent.ChatMessage] carries its `channelId`, so the caller routes/ tags each line. A
     * no-op for a channel already joined. Requires an active [connect]; joins arrive once the handshake completes.
     */
    fun join(channelId: String) {
        scope.launch {
            val added: Boolean = joinMutex.withLock { joinedChannels.add(channelId) }
            if (added && isConnected) {
                val hubSocket: HubSocket? = joinMutex.withLock { socket }
                hubSocket?.let { sendTracked(it, joinInvocation(channelId)) }
            }
        }
    }

    /**
     * Drop [channelId] from the watched set, leaving its group on the live socket so its pushes stop — without
     * disturbing the other watched channels. A no-op for a channel not currently joined.
     */
    fun leave(channelId: String) {
        scope.launch {
            val removed: Boolean = joinMutex.withLock { joinedChannels.remove(channelId) }
            if (removed && isConnected) {
                val hubSocket: HubSocket? = joinMutex.withLock { socket }
                hubSocket?.let { sendTracked(it, leaveInvocation(channelId)) }
            }
        }
    }

    /** Close the WebSocket and stop the reconnect loop. */
    fun disconnect() {
        connectJob?.cancel()
        connectJob = null
        currentChannelId = null
        scope.launch {
            joinMutex.withLock { joinedChannels.clear() }
            outboundMutex.withLock { resetResumeStateLocked() }
        }
        socket?.close()
        socket = null
        isConnected = false
        _connectionState.value = HubConnectionState.Disconnected
    }

    /** Release all resources. After this the client cannot be reused. */
    fun dispose() {
        scope.cancel()
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private suspend fun openSession(
        baseUrl: String,
        tokenProvider: () -> String?,
        onConnected: () -> Unit,
    ) {
        // Read the CURRENT token for this attempt (see [connect]); bail and let the caller's back-off retry
        // when none is available yet, instead of opening the socket with an empty token (a guaranteed 401).
        val accessToken: String = tokenProvider() ?: return

        // Strip trailing slash and derive ws:// from http:// (or wss:// from https://).
        val base: String = baseUrl.trimEnd('/')
        val wsBase: String =
            when {
                base.startsWith("https://") -> "wss://" + base.removePrefix("https://")
                base.startsWith("http://") -> "ws://" + base.removePrefix("http://")
                else -> base
            }

        val hubSocket: HubSocket = HubSocket()
        socket = hubSocket
        try {
            // Opens and suspends until the socket is OPEN; throws (caught by the reconnect loop) if it fails.
            hubSocket.open("$wsBase/hubs/dashboard?access_token=$accessToken")

            // ── Handshake ──────────────────────────────────────────────
            // Send the JSON hub protocol handshake request, terminated with the record separator. The
            // "useStatefulReconnect" field is the raw-websocket opt-in equivalent of the JS client's
            // `.withStatefulReconnect(...)` negotiation — this client skips negotiate entirely, so the
            // handshake is the only place left to signal it.
            hubSocket.send("""{"protocol":"json","version":1,"useStatefulReconnect":true}$RECORD_SEPARATOR""")

            // The first frame back is the handshake response. In the SignalR JSON hub protocol a SUCCESS
            // response is the EMPTY OBJECT `{}`; a rejection carries `{"error":"…"}`. Bail ONLY when an "error"
            // field is actually present — treating the empty `{}` success as a rejection would close the socket
            // the instant every handshake succeeded.
            val handshakeFrame: String = hubSocket.receive() ?: return
            val handshakeMsg: String = handshakeFrame.trimEnd(RECORD_SEPARATOR)
            val handshake: JsonObject? =
                runCatching { Json.parseToJsonElement(handshakeMsg).jsonObject }.getOrNull()
            if (handshake?.containsKey("error") == true) return

            val isResume: Boolean = hasEstablishedBefore
            if (isResume) {
                // ── Resume ──────────────────────────────────────────────
                // Per the hub protocol spec, Sequence is the FIRST message either party sends on a reconnect.
                // Resend only what the server never acked — the group memberships already acked (or never
                // buffered because they predate this feature) persist server-side in the resumed hub context,
                // so they do NOT need to be rejoined.
                val (startSeq: Long, pending: List<String>) =
                    outboundMutex.withLock { nextOutboundSeq to outboundBuffer.map { it.second } }
                hubSocket.send("""{"type":$TYPE_SEQUENCE,"sequenceId":$startSeq}$RECORD_SEPARATOR""")
                for (framed: String in pending) hubSocket.send(framed)

                // The resume must prove itself alive before we trust it — the server may simply refuse a
                // resume it doesn't recognise by dropping the socket. A confirming frame can be anything
                // (its own Sequence, an Ack, an invocation, a ping) — we just need SOMETHING back.
                val confirmFrame: String =
                    withTimeoutOrNull(ResumeConfirmTimeoutMillis) { hubSocket.receive() }
                        ?: return // refused/dead — openSession returns, established stays false, caller
                // resets resume state so the NEXT attempt falls back to a plain fresh connect.

                isConnected = true
                onConnected()
                for (segment: String in confirmFrame.split(RECORD_SEPARATOR)) {
                    if (segment.isBlank()) continue
                    dispatchSegment(hubSocket, segment)
                }
            } else {
                // ── Fresh connect: JoinChannel invocation(s) ───────────────
                // Tell the hub which channel group(s) we want to subscribe to. Join EVERY channel in the
                // watched set (the primary plus any multi-watch additions), so a reconnect restores all of
                // them — not just the last one. A single-channel consumer joins exactly its one channel. Each
                // join is tracked/buffered so a LATER resume can resend it if it's still unacked.
                val channelsToJoin: List<String> = joinMutex.withLock { joinedChannels.toList() }
                for (id: String in channelsToJoin) {
                    sendTracked(hubSocket, joinInvocation(id))
                }

                isConnected = true
                onConnected()
            }
            hasEstablishedBefore = true

            coroutineScope {
                // ── Keep-alive ping ────────────────────────────────────────
                // Send our own SignalR ping under the server's ClientTimeoutInterval; without it the hub evicts
                // us every ~30 s (server→client chat frames don't reset that timer) and the feed goes silent
                // until the next reconnect. Cancelled when the receive loop ends (the finally below). Pings are
                // NOT sequence-tracked — they carry no state worth resending after a drop.
                val pingJob: Job =
                    launch {
                        while (true) {
                            delay(PingIntervalMillis)
                            hubSocket.send("""{"type":$TYPE_PING}$RECORD_SEPARATOR""")
                        }
                    }

                // ── Event loop ────────────────────────────────────────────
                try {
                    while (true) {
                        // A single frame may carry multiple SignalR messages, each separated by \x1e. A null
                        // timeout result means no frame arrived within ReceiveTimeoutMillis — the connection is
                        // presumed dead (see ReceiveTimeoutMillis) — so break out and let the outer loop reconnect.
                        val raw: String =
                            withTimeoutOrNull(ReceiveTimeoutMillis) { hubSocket.receive() } ?: break
                        for (segment: String in raw.split(RECORD_SEPARATOR)) {
                            if (segment.isBlank()) continue
                            dispatchSegment(hubSocket, segment)
                        }
                    }
                } finally {
                    pingJob.cancel()
                }
            }
        } finally {
            isConnected = false
            hubSocket.close()
            if (socket === hubSocket) socket = null
        }
    }

    /** Resets ONLY the outbound resume bookkeeping — must be called under [outboundMutex]. */
    private fun resetResumeStateLocked() {
        hasEstablishedBefore = false
        nextOutboundSeq = 1
        outboundBuffer.clear()
    }

    /** Sends a tracked (Join/Leave) invocation: assigns it the next outbound sequence id and buffers it. */
    private suspend fun sendTracked(hubSocket: HubSocket, invocation: String) {
        outboundMutex.withLock {
            outboundBuffer.addLast(nextOutboundSeq to invocation)
            nextOutboundSeq += 1
        }
        hubSocket.send(invocation)
    }

    private fun joinInvocation(channelId: String): String =
        """{"type":1,"target":"JoinChannel","arguments":["$channelId"]}$RECORD_SEPARATOR"""

    private fun leaveInvocation(channelId: String): String =
        """{"type":1,"target":"LeaveChannel","arguments":["$channelId"]}$RECORD_SEPARATOR"""

    private suspend fun dispatchSegment(hubSocket: HubSocket, segment: String) {
        val json: JsonElement =
            runCatching { Json.parseToJsonElement(segment) }.getOrNull() ?: return
        val obj: JsonObject = json.jsonObject
        val type: Int = obj["type"]?.jsonPrimitive?.int ?: return

        when (type) {
            TYPE_INVOCATION -> {
                // Assign this invocation the next expected inbound id (seeded by the server's own Sequence
                // message on a resume; implicitly 1, 2, 3, … on a fresh connection). An id at or below the
                // highest we've ever actually dispatched is a resend of something already delivered — the
                // server doesn't know precisely what got through before the drop — so it's dropped here,
                // never re-emitted to [events], while still advancing the counter and sending its Ack.
                val id: Long = inboundNextExpected
                inboundNextExpected += 1
                val isDuplicate: Boolean = id <= inboundHighestProcessed
                if (!isDuplicate) {
                    inboundHighestProcessed = id
                    val target: String = obj["target"]?.jsonPrimitive?.content ?: return
                    val args: JsonArray = obj["arguments"]?.jsonArray ?: return
                    val event: HubEvent? = HubEvent.from(target, args)
                    if (event != null) _events.tryEmit(event)
                }
                hubSocket.send("""{"type":$TYPE_ACK,"sequenceId":$id}$RECORD_SEPARATOR""")
            }
            TYPE_ACK -> {
                val acked: Long = obj["sequenceId"]?.jsonPrimitive?.long ?: return
                outboundMutex.withLock { outboundBuffer.removeAll { it.first <= acked } }
            }
            TYPE_SEQUENCE -> {
                val startId: Long = obj["sequenceId"]?.jsonPrimitive?.long ?: return
                inboundNextExpected = startId
            }
            TYPE_PING -> Unit // pong is automatic — nothing to do on an inbound ping
            TYPE_CLOSE -> {
                isConnected = false
                _connectionState.value = HubConnectionState.Reconnecting
            }
        }
    }
}

/**
 * The shell's truthful hub-health states (S050). [Disconnected] only holds before the first [DashboardHubClient.connect]
 * call or after an explicit [DashboardHubClient.disconnect] — never while the reconnect loop is still trying, which is
 * always [Reconnecting] instead (covers both "never established yet" and "just dropped").
 */
enum class HubConnectionState {
    Connected,
    Reconnecting,
    Disconnected,
}
