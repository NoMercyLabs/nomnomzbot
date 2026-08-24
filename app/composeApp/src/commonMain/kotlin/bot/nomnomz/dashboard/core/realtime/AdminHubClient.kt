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

import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long

// ASP.NET Core SignalR JSON Hub Protocol — record separator byte (0x1E) between frames.
private const val RECORD_SEPARATOR: Char = ''

// SignalR message types, incl. the stateful-reconnect Ack/Sequence pair — see DashboardHubClient for the
// spec reference and the full rationale (mirrored here 1:1; this hub just has no outbound invocations to
// buffer, so its outbound Sequence is always sent with an empty resend list).
private const val TYPE_INVOCATION: Int = 1
private const val TYPE_PING: Int = 6
private const val TYPE_CLOSE: Int = 7
private const val TYPE_ACK: Int = 8
private const val TYPE_SEQUENCE: Int = 9

private const val PingIntervalMillis: Long = 15_000

// See DashboardHubClient.ReceiveTimeoutMillis — a dead connection often never surfaces as a close/error
// (e.g. the origin container replaced during a deploy); without a read-timeout the reconnect loop never
// runs again because hubSocket.receive() can suspend forever.
private const val ReceiveTimeoutMillis: Long = PingIntervalMillis * 4

// See DashboardHubClient.ResumeConfirmTimeoutMillis.
private const val ResumeConfirmTimeoutMillis: Long = 5_000

/**
 * SignalR hub client for the platform-operator hub `AdminHub` at `/hubs/admin`. Unlike [DashboardHubClient]
 * this hub has NO per-channel group — the handshake is gated on the `iam:manage` platform grant and every
 * push goes to all connected operators, so there is no `JoinChannel` step. The server pushes:
 *
 * - `ReceiveSystemStatus` every 15 s — the REAL system snapshot + stats, so the admin home's live panel
 *   moves without polling.
 * - `ReceiveChannelRegistryUpdate` on a channel going live/offline and on a tenant suspension.
 * - `ReceiveLog` on operator-notable events (tenant suspensions).
 *
 * The raw text transport is a [HubSocket] (browser-native WebSocket on wasmJs — Ktor WS never opens a socket
 * there; Ktor CIO on jvm/desktop).
 *
 * Reconnection & stateful resume: mirrors [DashboardHubClient] — the handshake opts in with
 * `useStatefulReconnect: true`, and a reconnect after a prior successful connection sends the hub protocol's
 * `Sequence` message (type 9) first (this hub has nothing outbound worth buffering, so nothing is resent) and
 * numbers inbound pushes so a duplicate resent after a resume is dropped, not redelivered to [events]. A
 * resume that never proves itself alive within [ResumeConfirmTimeoutMillis] falls back to a plain fresh
 * connect on the next attempt — never a stuck or falsely-resumed connection. Exponential back-off (cap 30 s);
 * only [disconnect] stops the loop.
 */
class AdminHubClient {

    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var connectJob: Job? = null
    private var socket: HubSocket? = null

    // This hub has no client→server invocations at all today (no Join/Leave equivalent), so the outbound
    // buffer is always empty — kept for protocol symmetry: the Sequence message is still required on every
    // resume regardless of whether there's anything to resend.
    private var nextOutboundSeq: Long = 1
    private var hasEstablishedBefore: Boolean = false
    private var inboundNextExpected: Long = 1
    private var inboundHighestProcessed: Long = 0

    private val _events: MutableSharedFlow<AdminHubEvent> = MutableSharedFlow(extraBufferCapacity = 64)

    /** Hub invocations received from the operator hub after a successful [connect]. */
    val events: SharedFlow<AdminHubEvent> = _events.asSharedFlow()

    var isConnected: Boolean = false
        private set

    /**
     * Open the WebSocket to `{baseUrl}/hubs/admin`, complete the SignalR handshake, then stream incoming hub
     * invocations into [events]. Re-entrant: a repeat call while already connecting/connected is a no-op.
     *
     * [tokenProvider] is read on EVERY (re)connect (never captured once) so a rotated JWT is used on reconnect;
     * [refreshToken] refreshes an expired token before a retry that failed to establish, so an idle operator
     * console self-heals instead of 401-storming the handshake forever.
     */
    fun connect(
        baseUrl: String,
        tokenProvider: () -> String?,
        refreshToken: (suspend () -> Boolean)? = null,
    ) {
        if (connectJob?.isActive == true) return
        resetResumeState()
        inboundNextExpected = 1
        inboundHighestProcessed = 0
        connectJob =
            scope.launch {
                var backoffMs: Long = 1_000
                while (true) {
                    var established = false
                    runCatching {
                            openSession(baseUrl, tokenProvider) {
                                established = true
                                backoffMs = 1_000
                            }
                        }
                        .onFailure { /* swallowed — the reconnect loop handles it */ }
                    isConnected = false
                    if (!established) {
                        resetResumeState()
                        refreshToken?.invoke()
                    }
                    delay(backoffMs)
                    backoffMs = (backoffMs * 2).coerceAtMost(30_000)
                }
            }
    }

    /** Close the WebSocket and stop the reconnect loop. */
    fun disconnect() {
        connectJob?.cancel()
        connectJob = null
        resetResumeState()
        socket?.close()
        socket = null
        isConnected = false
    }

    fun dispose() {
        scope.cancel()
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private fun resetResumeState() {
        hasEstablishedBefore = false
        nextOutboundSeq = 1
    }

    private suspend fun openSession(
        baseUrl: String,
        tokenProvider: () -> String?,
        onConnected: () -> Unit,
    ) {
        val accessToken: String = tokenProvider() ?: return

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
            hubSocket.open("$wsBase/hubs/admin?access_token=$accessToken")

            hubSocket.send("""{"protocol":"json","version":1,"useStatefulReconnect":true}$RECORD_SEPARATOR""")

            val handshakeFrame: String = hubSocket.receive() ?: return
            val handshakeMsg: String = handshakeFrame.trimEnd(RECORD_SEPARATOR)
            val handshake: JsonObject? =
                runCatching { Json.parseToJsonElement(handshakeMsg).jsonObject }.getOrNull()
            if (handshake?.containsKey("error") == true) return

            if (hasEstablishedBefore) {
                // ── Resume — Sequence first (hub protocol spec), nothing to resend on this hub. ──
                hubSocket.send("""{"type":$TYPE_SEQUENCE,"sequenceId":$nextOutboundSeq}$RECORD_SEPARATOR""")
                val confirmFrame: String =
                    withTimeoutOrNull(ResumeConfirmTimeoutMillis) { hubSocket.receive() }
                        ?: return // refused/dead — established stays false, caller resets resume state.

                isConnected = true
                onConnected()
                for (segment: String in confirmFrame.split(RECORD_SEPARATOR)) {
                    if (segment.isBlank()) continue
                    dispatchSegment(hubSocket, segment)
                }
            } else {
                isConnected = true
                onConnected()
            }
            hasEstablishedBefore = true

            coroutineScope {
                val pingJob: Job =
                    launch {
                        while (true) {
                            delay(PingIntervalMillis)
                            hubSocket.send("""{"type":$TYPE_PING}$RECORD_SEPARATOR""")
                        }
                    }

                try {
                    while (true) {
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

    private suspend fun dispatchSegment(hubSocket: HubSocket, segment: String) {
        val json: JsonElement =
            runCatching { Json.parseToJsonElement(segment) }.getOrNull() ?: return
        val obj: JsonObject = json.jsonObject
        val type: Int = obj["type"]?.jsonPrimitive?.int ?: return

        when (type) {
            TYPE_INVOCATION -> {
                // See DashboardHubClient.dispatchSegment — same inbound dedup: a resend at or below the
                // highest id we've ever actually dispatched is dropped, not redelivered.
                val id: Long = inboundNextExpected
                inboundNextExpected += 1
                val isDuplicate: Boolean = id <= inboundHighestProcessed
                if (!isDuplicate) {
                    inboundHighestProcessed = id
                    val target: String = obj["target"]?.jsonPrimitive?.content ?: return
                    val args: JsonArray = obj["arguments"]?.jsonArray ?: return
                    val event: AdminHubEvent? = AdminHubEvent.from(target, args)
                    if (event != null) _events.tryEmit(event)
                }
                hubSocket.send("""{"type":$TYPE_ACK,"sequenceId":$id}$RECORD_SEPARATOR""")
            }
            TYPE_ACK -> Unit // nothing outbound is ever buffered on this hub — no-op is correct
            TYPE_SEQUENCE -> {
                val startId: Long = obj["sequenceId"]?.jsonPrimitive?.long ?: return
                inboundNextExpected = startId
            }
            TYPE_PING -> Unit
            TYPE_CLOSE -> isConnected = false
        }
    }
}

/**
 * The operator-hub pushes, each mapping 1-to-1 to a method on the server `IAdminClient`. Unknown targets
 * decode to [Unknown] so an unmodelled push never crashes the receive loop.
 */
sealed interface AdminHubEvent {

    data class SystemStatus(val system: AdminSystem?, val stats: AdminStats?) : AdminHubEvent

    data class RegistryUpdate(val update: AdminRegistryUpdate) : AdminHubEvent

    data class Log(val entry: AdminLogEntry) : AdminHubEvent

    data class Unknown(val target: String, val rawArgs: String) : AdminHubEvent

    companion object {
        private val json: Json = Json { ignoreUnknownKeys = true; isLenient = true }

        internal fun from(target: String, args: JsonArray): AdminHubEvent? {
            if (args.isEmpty()) return Unknown(target, "[]")
            val first: String = args[0].toString()
            return runCatching {
                when (target) {
                    "ReceiveSystemStatus" -> {
                        val payload: AdminHubStatusPayload = json.decodeFromString(first)
                        SystemStatus(payload.system, payload.stats)
                    }
                    "ReceiveChannelRegistryUpdate" -> RegistryUpdate(json.decodeFromString(first))
                    "ReceiveLog" -> Log(json.decodeFromString(first))
                    else -> Unknown(target, first)
                }
            }.getOrNull()
        }
    }
}

/** `{ system, stats }` — the 15 s heartbeat payload (AdminHubStatusPublisher). */
@Serializable
private data class AdminHubStatusPayload(
    val system: AdminSystem? = null,
    val stats: AdminStats? = null,
)

/**
 * A live channel-registry change: go-live/offline carry [channelName] + [isLive] (+ title/game when live);
 * a tenant suspension carries [status] instead. All fields are optional because the shape varies by cause.
 */
@Serializable
data class AdminRegistryUpdate(
    val broadcasterId: String = "",
    val channelName: String? = null,
    val isLive: Boolean? = null,
    val streamTitle: String? = null,
    val gameName: String? = null,
    val status: String? = null,
)

/** An operator log line pushed on notable events (tenant suspensions). [type] = success|warning|error|info. */
@Serializable
data class AdminLogEntry(
    val message: String = "",
    val type: String = "info",
)
