// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.connection.ActAsTokenCustody
import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.InMemoryActAsCustody
import bot.nomnomz.dashboard.core.connection.InMemoryActAsSessionStore
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.navigation.RecordingAppReloader
import bot.nomnomz.dashboard.core.network.ActAsSessionInfo
import bot.nomnomz.dashboard.core.network.ApiClient
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.AuthUser
import bot.nomnomz.dashboard.core.network.ChannelProvisioningApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.RefreshBody
import bot.nomnomz.dashboard.core.network.RestAuthApi
import bot.nomnomz.dashboard.core.network.RestChannelsApi
import bot.nomnomz.dashboard.core.network.StatusResponse
import bot.nomnomz.dashboard.core.network.UserSearchResult
import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import java.net.InetSocketAddress
import java.util.Collections
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// The X-Channel-Id header is what the backend resolves the tenant from on EVERY request. This drives act-as begin
// and Exit through the real ActAsCoordinator, SessionStore, channel switcher, RestAuthApi/RestChannelsApi and the
// real ApiClient (Ktor CIO) against a real local HTTP server, and reads the header off the wire:
//   - as the admin, requests carry the admin's channel;
//   - the begin probe (the target's token) carries NO channel — never the admin's;
//   - the boot after begin carries the target's own channel, and the admin's remembered channel is not read;
//   - the boot after Exit carries the admin's channel again.
@OptIn(ExperimentalCoroutinesApi::class)
class ActAsChannelHeaderTest {

    private val backend: FakeBackend = FakeBackend()

    private val profile: ConnectionProfile = ConnectionProfile(
        id = "p1",
        displayName = "Self-host",
        baseUrl = backend.baseUrl,
        source = ProfileSource.ServedOrigin,
    )

    private val token: ImpersonationTokenDto = ImpersonationTokenDto(
        accessToken = TARGET_JWT,
        expiresAt = "2030-01-01T00:00:00Z",
        sessionId = "grant-1",
        user = UserSearchResult(id = "target-id", displayName = "anda_six"),
    )

    // What survives a restart: the vault, the remembered profile and channel, the act-as custody and return route.
    private val vault: MemoryTokenVault = MemoryTokenVault()
    private val profiles: MemoryProfileStore = MemoryProfileStore()
    private val remembered: MemoryChannelStore = MemoryChannelStore()
    private val custody: ActAsTokenCustody = InMemoryActAsCustody()
    private val actAs: InMemoryActAsSessionStore = InMemoryActAsSessionStore()

    @AfterTest
    fun stopBackend() = backend.close()

    /** One app boot, wired as AppGraph wires it: the client reads token and channel from the session on each call. */
    private class Boot(
        val store: SessionStore,
        val authApi: AuthApi,
        val switcher: ChannelSwitcherController,
        val coordinator: ActAsCoordinator,
        val reloader: RecordingAppReloader,
    )

    private fun boot(scope: CoroutineScope, location: String = ""): Boot {
        val store = SessionStore(vault, profiles, remembered, scope)
        val client = ApiClient(
            baseUrlProvider = store::baseUrl,
            tokenProvider = store::accessToken,
            channelProvider = { store.activeChannelId.value },
        )
        val authApi = RestAuthApi(client)
        val switcher = ChannelSwitcherController(RestChannelsApi(client, store), NoProvisioning, store) {}
        val reloader = RecordingAppReloader(location)
        val coordinator = ActAsCoordinator(
            sessionStore = store,
            authApi = authApi,
            revokeSession = { ApiResult.Ok(Unit) },
            actAsStore = actAs,
            tokenCustody = custody,
            reloader = reloader,
            feedback = RecordingFeedback(),
            scope = scope,
        )
        return Boot(store, authApi, switcher, coordinator, reloader)
    }

    // The boot after a restart, as ConnectController.restoreSession runs it: the refresh answer decides who it is.
    private suspend fun restartBoot(scope: CoroutineScope): Boot {
        val b = boot(scope)
        b.store.pin(profile)
        val operatorTokens: SessionTokens? = vault.stored[profile.id]
        val answer: ApiResult<AuthPayload> = b.authApi.refresh(operatorTokens?.refreshToken, b.coordinator.heldActAsToken())
        val payload: AuthPayload = (answer as ApiResult.Ok).value
        if (payload.impersonation != null) {
            assertTrue(b.coordinator.resume(profile, operatorTokens, payload))
        } else {
            b.coordinator.onOperatorSession()
            b.store.connect(profile, SessionTokens(payload.accessToken, payload.refreshToken ?: operatorTokens?.refreshToken))
        }
        b.switcher.load()
        return b
    }

    // The channel the backend saw on the next ordinary request (a /me probe) made by [b].
    private suspend fun channelSentBy(b: Boot): String? {
        backend.requests.clear()
        b.authApi.me()
        return backend.requests.single().channel
    }

    @Test
    fun the_channel_header_follows_begin_to_the_target_and_exit_back_to_the_admin() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))

        // The admin, signed in on his own channel (chosen explicitly, so it is remembered), on the admin page.
        val admin = boot(scope, location = ADMIN_ROUTE)
        admin.store.connect(profile, SessionTokens(OPERATOR_JWT, OPERATOR_REFRESH))
        admin.store.setUser(SessionUser("operator-id", "stoney_eagle", "Stoney_Eagle", null, isAdmin = true))
        admin.switcher.load()
        admin.switcher.select(OPERATOR_CHANNEL)
        assertEquals(OPERATOR_CHANNEL, channelSentBy(admin))

        // Begin: the target's token is proven with no channel at all — the admin's channel never rides along.
        backend.sessionOpen = true
        backend.requests.clear()
        assertTrue(admin.coordinator.begin(token, "anda_six"))
        val probe: Seen = backend.requests.single()
        assertEquals("GET /api/v1/auth/me" to TARGET_JWT, probe.route to probe.bearer)
        assertNull(probe.channel, "the begin probe must not carry the admin's channel")
        assertEquals(listOf(""), admin.reloader.reloads)

        // The boot after begin is the target, on the target's own channel.
        val acting = restartBoot(scope)
        assertEquals(TARGET_CHANNEL, channelSentBy(acting))
        assertEquals(TARGET_FRESH_JWT, backend.requests.single().bearer)
        assertEquals(OPERATOR_CHANNEL, remembered.stored, "the admin's remembered channel is untouched")

        // Exit: the server ends the session; the app restarts where the admin started.
        acting.coordinator.exit()
        assertEquals(listOf(ADMIN_ROUTE), acting.reloader.reloads)
        assertFalse(backend.sessionOpen)

        // The boot after Exit is the admin, back on the admin's channel.
        val back = restartBoot(scope)
        assertNull(back.store.impersonating.value)
        assertEquals(OPERATOR_CHANNEL, channelSentBy(back))
        assertEquals(OPERATOR_FRESH_JWT, backend.requests.single().bearer)
    }

    private companion object {
        const val OPERATOR_JWT: String = "operator-jwt"
        const val OPERATOR_FRESH_JWT: String = "operator-fresh-jwt"
        const val OPERATOR_REFRESH: String = "operator-refresh"
        const val TARGET_JWT: String = "target-jwt"
        const val TARGET_FRESH_JWT: String = "target-fresh-jwt"
        const val OPERATOR_CHANNEL: String = "operator-channel"
        const val TARGET_CHANNEL: String = "target-channel"
        const val ADMIN_ROUTE: String = "#/admin"
    }

    /** One request as the backend received it: verb + path, the bearer, and the X-Channel-Id header. */
    private data class Seen(val route: String, val bearer: String?, val channel: String?)

    /**
     * The backend on a real socket, answering per bearer like the real one: /me and the channel list are the
     * caller's own; refresh answers with the target while the act-as session is open and an act-as token is
     * sent, else with the operator; Exit ends the session and hands back the operator.
     */
    private class FakeBackend : AutoCloseable {
        private val json: Json = Json { encodeDefaults = true; ignoreUnknownKeys = true }
        private val server: HttpServer = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        val requests: MutableList<Seen> = Collections.synchronizedList(mutableListOf())

        @Volatile
        var sessionOpen: Boolean = false

        val baseUrl: String

        init {
            server.createContext("/") { exchange -> handle(exchange) }
            server.start()
            baseUrl = "http://127.0.0.1:${server.address.port}"
        }

        private fun handle(exchange: HttpExchange) {
            val bearer: String? = exchange.requestHeaders.getFirst("Authorization")?.removePrefix("Bearer ")
            val route = "${exchange.requestMethod} ${exchange.requestURI.path}"
            requests += Seen(route, bearer, exchange.requestHeaders.getFirst("X-Channel-Id"))
            val acting: Boolean = bearer?.startsWith("target") == true
            val body: String = when (route) {
                "GET /api/v1/auth/me" -> json.encodeToString(StatusResponse(if (acting) targetUser() else operatorUser()))
                "GET /api/v1/channels" ->
                    json.encodeToString(
                        PaginatedEnvelope(listOf(ChannelSummary(id = if (acting) TARGET_CHANNEL else OPERATOR_CHANNEL))),
                    )
                "GET /api/v1/channels/moderated" -> json.encodeToString(StatusResponse(emptyList<ModeratedChannel>()))
                "POST /api/v1/auth/refresh" -> {
                    val sent: RefreshBody = json.decodeFromString(exchange.requestBody.readBytes().decodeToString())
                    json.encodeToString(StatusResponse(if (sent.actAsToken != null && sessionOpen) target() else operator()))
                }
                "POST /api/v1/auth/impersonation/exit" -> {
                    sessionOpen = false
                    json.encodeToString(StatusResponse(operator()))
                }
                else -> return respond(exchange, 404, "{}")
            }
            respond(exchange, 200, body)
        }

        private fun respond(exchange: HttpExchange, status: Int, body: String) {
            val bytes: ByteArray = body.encodeToByteArray()
            exchange.responseHeaders.add("Content-Type", "application/json")
            exchange.sendResponseHeaders(status, bytes.size.toLong())
            exchange.responseBody.use { it.write(bytes) }
        }

        private fun targetUser(): CurrentUser = CurrentUser(id = "target-id", username = "anda_six", displayName = "anda_six")

        private fun operatorUser(): CurrentUser =
            CurrentUser(id = "operator-id", username = "stoney_eagle", displayName = "Stoney_Eagle", isAdmin = true)

        private fun target(): AuthPayload =
            AuthPayload(
                accessToken = TARGET_FRESH_JWT,
                user = AuthUser(id = "target-id", username = "anda_six", displayName = "anda_six"),
                impersonation = ActAsSessionInfo(sessionId = "grant-1", expiresAt = "2030-01-01T00:00:00Z"),
            )

        private fun operator(): AuthPayload =
            AuthPayload(
                accessToken = OPERATOR_FRESH_JWT,
                refreshToken = "operator-refresh-2",
                user = AuthUser(id = "operator-id", username = "stoney_eagle", displayName = "Stoney_Eagle"),
            )

        override fun close() = server.stop(0)
    }

    private object NoProvisioning : ChannelProvisioningApi {
        override suspend fun enterModeratedChannel(twitchBroadcasterId: String): ApiResult<ChannelSummary> =
            ApiResult.Failure(ApiError(501, null, "unused"))
    }
}

private class MemoryTokenVault : SessionTokenStore {
    val stored: MutableMap<String, SessionTokens> = mutableMapOf()
    override suspend fun read(profileId: String): SessionTokens? = stored[profileId]
    override suspend fun write(profileId: String, tokens: SessionTokens) {
        stored[profileId] = tokens
    }
    override suspend fun clear(profileId: String) {
        stored.remove(profileId)
    }
}

private class MemoryProfileStore : ActiveProfileStore {
    private var stored: ConnectionProfile? = null
    override suspend fun read(): ConnectionProfile? = stored
    override suspend fun write(profile: ConnectionProfile) {
        stored = profile
    }
    override suspend fun clear() {
        stored = null
    }
}

private class MemoryChannelStore : ActiveChannelStore {
    @Volatile
    var stored: String? = null
    override suspend fun read(): String? = stored
    override suspend fun write(channelId: String) {
        stored = channelId
    }
    override suspend fun clear() {
        stored = null
    }
}
