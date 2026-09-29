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

import bot.nomnomz.dashboard.core.connection.ActAsEndNotice
import bot.nomnomz.dashboard.core.connection.ActAsEndReason
import bot.nomnomz.dashboard.core.connection.ActAsTokenCustody
import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.CookieActAsCustody
import bot.nomnomz.dashboard.core.connection.InMemoryActAsCustody
import bot.nomnomz.dashboard.core.connection.InMemoryActAsSessionStore
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionPhase
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.navigation.RecordingAppReloader
import bot.nomnomz.dashboard.core.network.ActAsSessionInfo
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.AuthUser
import bot.nomnomz.dashboard.core.network.ChannelBotStatusDetail
import bot.nomnomz.dashboard.core.network.ChannelProvisioningApi
import bot.nomnomz.dashboard.core.network.ChannelScopesResponse
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.LoginProvider
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OAuthStart
import bot.nomnomz.dashboard.core.network.UserSearchResult
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_ended_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_exit_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_identity_failed
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves act-as is a full session swap through an app reload, driven by the SERVER, over the real SessionStore and
// channel switcher against a fake backend that answers per credential exactly like the real one:
//   - begin proves the target and reloads; the act-as token never reaches JS storage (web: the HttpOnly cookie
//     holds it; desktop: process memory), only the operator's return route does;
//   - a reload (F5) boots as the target because /auth/refresh answers with `impersonation` — not because of any
//     stored marker; the operator's remembered channel is neither read nor written;
//   - Exit calls /auth/impersonation/exit with the act-as bearer, then reloads where the operator started;
//   - a 401 re-mints the act-as token while the session is open, and hands the operator back once it is over.
@OptIn(ExperimentalCoroutinesApi::class)
class ActAsCoordinatorTest {

    private val profile = ConnectionProfile(
        id = "p1",
        displayName = "Self-host",
        baseUrl = "http://localhost:5080",
        source = ProfileSource.ServedOrigin,
    )

    private val token = ImpersonationTokenDto(
        accessToken = TARGET_JWT,
        expiresAt = "2030-01-01T00:00:00Z",
        sessionId = "grant-1",
        user = UserSearchResult(id = "target-id", displayName = "anda_six"),
    )

    /** What survives a reload: the vault, the remembered profile + channel, the tab's act-as state, the server. */
    private class Persistent(
        val server: FakeServer,
        val custody: ActAsTokenCustody,
        val vault: FakeTokenVault = FakeTokenVault(),
        val profiles: FakeProfileStore = FakeProfileStore(),
        val remembered: FakeChannelStore = FakeChannelStore(),
        val actAs: InMemoryActAsSessionStore = InMemoryActAsSessionStore(),
    )

    private fun web(): Persistent = Persistent(FakeServer(web = true), CookieActAsCustody)

    private fun desktop(): Persistent =
        Persistent(FakeServer(web = false), InMemoryActAsCustody()).also {
            it.vault.stored[profile.id] = SessionTokens(accessToken = OPERATOR_JWT, refreshToken = OPERATOR_REFRESH)
        }

    /** One boot of the app: everything in memory is new, only [Persistent] carries over. */
    private class Boot(
        val store: SessionStore,
        val coordinator: ActAsCoordinator,
        val switcher: ChannelSwitcherController,
        val feedback: RecordingFeedback,
        val reloader: RecordingAppReloader,
    )

    private fun boot(scope: CoroutineScope, persistent: Persistent, location: String = ""): Boot {
        val store = SessionStore(persistent.vault, persistent.profiles, persistent.remembered, scope)
        persistent.server.store = store
        val feedback = RecordingFeedback()
        val reloader = RecordingAppReloader(location)
        val switcher = ChannelSwitcherController(IdentityChannelsApi(store), NoProvisioning, store) {}
        val coordinator = ActAsCoordinator(
            sessionStore = store,
            authApi = persistent.server,
            revokeSession = { grant ->
                persistent.server.steps += "revoke:$grant:${store.accessToken()}"
                ApiResult.Ok(Unit)
            },
            actAsStore = persistent.actAs,
            tokenCustody = persistent.custody,
            reloader = reloader,
            feedback = feedback,
            scope = scope,
        )
        return Boot(store, coordinator, switcher, feedback, reloader)
    }

    // The operator signed in on their own channel, chosen explicitly (so it is remembered), on the admin page.
    private suspend fun signedInOperator(scope: CoroutineScope, persistent: Persistent): Boot {
        val b = boot(scope, persistent, location = ADMIN_ROUTE)
        b.store.connect(profile, persistent.vault.stored[profile.id] ?: SessionTokens(accessToken = OPERATOR_JWT))
        b.store.setUser(SessionUser("operator-id", "stoney_eagle", "Stoney_Eagle", null, isAdmin = true))
        b.switcher.load()
        b.switcher.select(OPERATOR_CHANNEL)
        persistent.server.steps.clear()
        return b
    }

    // The admin console's impersonate call: the server opens the session and (web) sets the HttpOnly cookie.
    private suspend fun startActingAs(scope: CoroutineScope, persistent: Persistent): Boot {
        val b = signedInOperator(scope, persistent)
        persistent.server.impersonate()
        assertTrue(b.coordinator.begin(token, "anda_six"))
        persistent.server.steps.clear()
        return b
    }

    // The boot after a reload, exactly as ConnectController.restoreSession runs it: refresh decides who this is.
    private suspend fun reloadBoot(scope: CoroutineScope, persistent: Persistent): Boot {
        val b = boot(scope, persistent)
        val operatorTokens: SessionTokens? = persistent.vault.stored[profile.id]
        val answer = persistent.server.refresh(operatorTokens?.refreshToken, b.coordinator.heldActAsToken())
        val payload: AuthPayload = (answer as ApiResult.Ok).value
        if (payload.impersonation != null) {
            assertTrue(b.coordinator.resume(profile, operatorTokens, payload))
        } else {
            b.coordinator.onOperatorSession()
            b.store.connect(profile, SessionTokens(accessToken = payload.accessToken))
        }
        return b
    }

    @Test
    fun begin_proves_the_target_keeps_no_token_in_js_storage_and_reloads_onto_the_targets_default_page() = runTest {
        val persistent = web()
        val b = signedInOperator(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)
        persistent.server.impersonate()

        val began: Boolean = b.coordinator.begin(token, "anda_six")

        assertTrue(began)
        assertEquals(listOf("me:$TARGET_JWT"), persistent.server.steps)
        // Only the operator's return route went to sessionStorage — no token of any kind.
        assertEquals(listOf(ADMIN_ROUTE), persistent.actAs.written)
        assertNull(persistent.custody.read(), "web custody is the HttpOnly cookie; JS holds no act-as token")
        assertEquals(listOf(""), b.reloader.reloads)
        // Nothing of the operator's custody changed.
        assertEquals(OPERATOR_JWT, persistent.vault.stored[profile.id]?.accessToken)
        assertEquals(OPERATOR_CHANNEL, persistent.remembered.stored)
    }

    @Test
    fun an_unreadable_target_identity_never_reloads_and_leaves_the_operator_in_place() = runTest {
        val persistent = web()
        persistent.server.targetMe = ApiResult.Failure(ApiError(status = 500, code = "500", message = "down"))
        val b = signedInOperator(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)

        val began: Boolean = b.coordinator.begin(token, "anda_six")

        assertFalse(began)
        assertTrue(b.reloader.reloads.isEmpty(), "never reload into a session that cannot load")
        assertTrue(persistent.actAs.written.isEmpty())
        assertNull(b.store.impersonating.value)
        assertEquals(OPERATOR_JWT, b.store.accessToken())
        assertEquals(OPERATOR_CHANNEL, b.store.activeChannelId.value)
        // The grant was revoked on the OPERATOR's token.
        assertEquals(listOf("me:$TARGET_JWT", "revoke:grant-1:$OPERATOR_JWT"), persistent.server.steps)
        assertEquals(Res.string.shell_impersonation_identity_failed, b.feedback.only.label)
    }

    @Test
    fun f5_while_acting_boots_as_the_target_because_the_refresh_answer_says_so() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)

        val b = reloadBoot(scope, persistent)
        b.switcher.load()

        // Web sends no token in the body: the browser's HttpOnly cookie carries the act-as session.
        assertEquals("refresh:null:null", persistent.server.steps.first())
        assertEquals(SessionPhase.Connected, b.store.phase.value)
        assertEquals(TARGET_FRESH_JWT, b.store.accessToken())
        assertEquals("anda_six", b.store.user.value?.displayName)
        assertFalse(b.store.user.value!!.isAdmin)
        assertEquals("grant-1", b.store.impersonating.value?.accessGrantId)
        assertEquals(TARGET_CHANNEL, b.store.activeChannelId.value)
        assertEquals(listOf(TARGET_CHANNEL), (b.switcher.state.value as SwitcherState.Ready).channels.map { it.id })
        assertEquals(OPERATOR_CHANNEL, persistent.remembered.stored, "the operator's channel is neither read nor written")
        assertEquals(OPERATOR_JWT, persistent.vault.stored[profile.id]?.accessToken)
    }

    @Test
    fun desktop_holds_the_act_as_token_in_memory_and_sends_it_with_the_restart_refresh() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = desktop()
        startActingAs(scope, persistent)
        assertEquals(TARGET_JWT, persistent.custody.read())

        val b = reloadBoot(scope, persistent)

        assertEquals("refresh:$OPERATOR_REFRESH:$TARGET_JWT", persistent.server.steps.first())
        assertEquals(TARGET_FRESH_JWT, b.store.accessToken())
        assertEquals(TARGET_FRESH_JWT, persistent.custody.read(), "the re-minted token replaces the held one")
        assertEquals(OPERATOR_REFRESH, persistent.vault.stored[profile.id]?.refreshToken, "not consumed while acting")
    }

    @Test
    fun no_token_ever_reaches_js_storage_or_the_vault_across_the_whole_web_act_as_lifecycle() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)
        persistent.server.expireAccessToken()
        b.coordinator.renew()
        b.coordinator.exit()

        val actAsTokens: List<String> = listOf(TARGET_JWT, TARGET_FRESH_JWT, TARGET_RENEWED_JWT)
        for (written: String in persistent.actAs.written) {
            assertTrue(
                (actAsTokens + OPERATOR_FRESH_JWT).none { written.contains(it) },
                "a token reached sessionStorage: $written",
            )
        }
        assertTrue(
            actAsTokens.none { t -> persistent.vault.writes.any { it.accessToken == t } },
            "an act-as token reached the operator's vault",
        )
        // The lifecycle really ran: the renewal re-minted, and Exit handed the operator back.
        assertEquals(listOf(ADMIN_ROUTE), persistent.actAs.written)
        assertFalse(persistent.server.sessionOpen)
    }

    @Test
    fun exit_calls_the_exit_endpoint_with_the_act_as_bearer_then_reloads_where_the_operator_started() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)
        persistent.server.steps.clear()

        b.coordinator.exit()

        assertEquals(listOf("exit:$TARGET_FRESH_JWT:null"), persistent.server.steps)
        assertFalse(persistent.server.sessionOpen)
        assertEquals(listOf(ADMIN_ROUTE), b.reloader.reloads)
        assertNull(persistent.actAs.returnLocation)
        assertNull(persistent.actAs.notice, "a clean exit leaves nothing to report")

        // The boot after the reload is the operator — the server answers so, and no notice shows.
        val next = reloadBoot(scope, persistent)
        assertNull(next.store.impersonating.value)
        assertEquals(OPERATOR_FRESH_JWT, next.store.accessToken())
        next.coordinator.surfacePendingNotice()
        assertTrue(next.feedback.messages.isEmpty())
    }

    @Test
    fun a_desktop_exit_vaults_the_operators_rotated_refresh_token_before_the_restart() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = desktop()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)
        persistent.server.steps.clear()

        b.coordinator.exit()

        assertEquals(listOf("exit:$TARGET_FRESH_JWT:$OPERATOR_REFRESH"), persistent.server.steps)
        assertEquals(SessionTokens(OPERATOR_FRESH_JWT, OPERATOR_ROTATED_REFRESH), persistent.vault.stored[profile.id])
        assertNull(persistent.custody.read())
        assertEquals(listOf(ADMIN_ROUTE), b.reloader.reloads)
    }

    @Test
    fun a_failed_exit_keeps_the_operator_acting_and_says_so() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)
        persistent.server.exitResult = ApiResult.Failure(ApiError(status = 500, code = "500", message = "boom"))

        b.coordinator.exit()

        assertTrue(b.reloader.reloads.isEmpty(), "a reload would only come back as the target")
        assertEquals(TARGET_FRESH_JWT, b.store.accessToken())
        assertEquals(ADMIN_ROUTE, persistent.actAs.returnLocation)
        assertEquals(FeedbackKind.Error, b.feedback.only.kind)
        assertEquals(Res.string.shell_impersonation_exit_failed, b.feedback.only.label)
        assertEquals(listOf<Any>("boom"), b.feedback.only.formatArgs)
    }

    @Test
    fun a_rejected_act_as_token_with_the_session_still_open_is_re_minted_as_the_same_target() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)
        persistent.server.expireAccessToken()

        val renewed: Boolean = b.coordinator.renew()

        assertTrue(renewed, "the 401'd request is retried")
        assertEquals(TARGET_RENEWED_JWT, b.store.accessToken())
        assertEquals("grant-1", b.store.impersonating.value?.accessGrantId)
        assertTrue(b.reloader.reloads.isEmpty())
    }

    @Test
    fun a_session_ended_elsewhere_hands_back_the_admin_and_the_next_boot_says_so_once() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)
        persistent.server.endSessionElsewhere()

        val renewed: Boolean = b.coordinator.renew()

        assertFalse(renewed)
        assertEquals(listOf(ADMIN_ROUTE), b.reloader.reloads)
        assertEquals(ActAsEndNotice(ActAsEndReason.Expired), persistent.actAs.notice)
        // Nothing more happens on the dying instance: a late 401 or an Exit press is ignored.
        assertFalse(b.coordinator.renew())
        b.coordinator.exit()
        assertEquals(1, b.reloader.reloads.size)

        val next = reloadBoot(scope, persistent)
        assertNull(next.store.impersonating.value)
        next.coordinator.surfacePendingNotice()
        next.coordinator.surfacePendingNotice()
        assertEquals(Res.string.shell_impersonation_ended_expired, next.feedback.only.label)
    }

    @Test
    fun a_reload_after_the_session_ended_while_away_boots_as_the_admin_with_a_notice() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = web()
        startActingAs(scope, persistent)
        persistent.server.endSessionElsewhere()

        val b = reloadBoot(scope, persistent)

        assertNull(b.store.impersonating.value)
        assertEquals(OPERATOR_FRESH_JWT, b.store.accessToken())
        assertNull(persistent.actAs.returnLocation)
        b.coordinator.surfacePendingNotice()
        assertEquals(Res.string.shell_impersonation_ended_expired, b.feedback.only.label)
    }

    @Test
    fun a_logout_while_acting_drops_the_act_as_state_and_reloads() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = desktop()
        startActingAs(scope, persistent)
        val b = reloadBoot(scope, persistent)

        b.coordinator.onLoggedOutWhileActing()

        assertNull(persistent.custody.read())
        assertNull(persistent.actAs.returnLocation)
        assertEquals(listOf(""), b.reloader.reloads)
    }

    @Test
    fun exit_when_not_acting_changes_nothing() = runTest {
        val persistent = web()
        val b = signedInOperator(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)

        b.coordinator.exit()

        assertTrue(persistent.server.steps.isEmpty())
        assertTrue(b.reloader.reloads.isEmpty())
        assertEquals(OPERATOR_JWT, b.store.accessToken())
    }

    private companion object {
        const val OPERATOR_JWT: String = "operator-jwt"
        const val OPERATOR_FRESH_JWT: String = "operator-fresh-jwt"
        const val OPERATOR_REFRESH: String = "operator-refresh"
        const val OPERATOR_ROTATED_REFRESH: String = "operator-refresh-2"
        const val TARGET_JWT: String = "target-jwt"
        const val TARGET_FRESH_JWT: String = "target-fresh-jwt"
        const val TARGET_RENEWED_JWT: String = "target-renewed-jwt"
        const val OPERATOR_CHANNEL: String = "operator-channel"
        const val TARGET_CHANNEL: String = "target-channel"
        const val ADMIN_ROUTE: String = "#/admin"
    }

    /**
     * The backend, per credential: the act-as session is open or not; the act-as credential is the HttpOnly cookie
     * on web or the body token on native; /me and the channel list answer for whoever holds the bearer.
     */
    private class FakeServer(private val web: Boolean) : AuthApi {
        lateinit var store: SessionStore
        val steps: MutableList<String> = mutableListOf()
        var sessionOpen: Boolean = false
        var targetMe: ApiResult<CurrentUser> = ApiResult.Ok(
            CurrentUser(id = "target-id", username = "anda_six", displayName = "anda_six", isAdmin = false),
        )
        var exitResult: ApiResult<AuthPayload?>? = null
        private var actAsCookie: String? = null
        private var nextTargetToken: String = TARGET_FRESH_JWT

        fun impersonate() {
            sessionOpen = true
            if (web) actAsCookie = TARGET_JWT
        }

        fun expireAccessToken() {
            nextTargetToken = TARGET_RENEWED_JWT
        }

        fun endSessionElsewhere() {
            sessionOpen = false
        }

        override suspend fun me(): ApiResult<CurrentUser> {
            val bearer: String? = store.accessToken()
            steps += "me:$bearer"
            return if (bearer?.startsWith("target") == true) {
                targetMe
            } else {
                ApiResult.Ok(CurrentUser(id = "operator-id", username = "stoney_eagle", displayName = "Stoney_Eagle", isAdmin = true))
            }
        }

        override suspend fun refresh(refreshToken: String?, actAsToken: String?): ApiResult<AuthPayload> {
            steps += "refresh:$refreshToken:$actAsToken"
            val actAs: String? = if (web) actAsCookie else actAsToken
            if (actAs != null && sessionOpen) {
                if (web) actAsCookie = nextTargetToken
                return ApiResult.Ok(
                    AuthPayload(
                        accessToken = nextTargetToken,
                        user = AuthUser(id = "target-id", username = "anda_six", displayName = "anda_six"),
                        impersonation = ActAsSessionInfo(sessionId = "grant-1", expiresAt = "2030-01-01T00:00:00Z"),
                    ),
                )
            }
            actAsCookie = null
            return ApiResult.Ok(operator())
        }

        override suspend fun exitImpersonation(refreshToken: String?): ApiResult<AuthPayload?> {
            steps += "exit:${store.accessToken()}:$refreshToken"
            exitResult?.let { return it }
            if (!sessionOpen) return ApiResult.Failure(ApiError(status = 401, code = "401", message = "ended"))
            sessionOpen = false
            actAsCookie = null
            return ApiResult.Ok(operator())
        }

        private fun operator(): AuthPayload =
            AuthPayload(
                accessToken = OPERATOR_FRESH_JWT,
                refreshToken = if (web) null else OPERATOR_ROTATED_REFRESH,
                user = AuthUser(id = "operator-id", username = "stoney_eagle", displayName = "Stoney_Eagle"),
            )

        override suspend fun providers(): ApiResult<List<LoginProvider>> = ApiResult.Ok(emptyList())
        override suspend fun startDeviceLogin(provider: String): ApiResult<DeviceCodeStart> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun pollDeviceLogin(provider: String, deviceCode: String): ApiResult<DeviceLoginPoll> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun logout(): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    // The roster is the caller's own: the target's token lists the target's channel.
    private class IdentityChannelsApi(private val store: SessionStore) : ChannelsApi {
        override suspend fun list(): ApiResult<List<ChannelSummary>> =
            ApiResult.Ok(
                if (store.accessToken()?.startsWith("target") == true) {
                    listOf(ChannelSummary(id = TARGET_CHANNEL, displayName = "Target", chatColor = "#TARGET"))
                } else {
                    listOf(ChannelSummary(id = OPERATOR_CHANNEL, displayName = "Operator", chatColor = "#OPERATOR"))
                },
            )

        override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
        override suspend fun primaryChannel(): ApiResult<ChannelSummary> = unused()
        override suspend fun join(channelId: String): ApiResult<Unit> = unused()
        override suspend fun leave(channelId: String): ApiResult<Unit> = unused()
        override suspend fun reset(channelId: String): ApiResult<Unit> = unused()
        override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = unused()
        override suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse> = unused()
        override suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart> = unused()
        override suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail> = unused()
        override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = unused()

        private fun <T> unused(): ApiResult<T> = ApiResult.Failure(ApiError(501, "UNUSED", "unused"))
    }

    private object NoProvisioning : ChannelProvisioningApi {
        override suspend fun enterModeratedChannel(twitchBroadcasterId: String): ApiResult<ChannelSummary> =
            ApiResult.Failure(ApiError(501, null, "unused"))
    }
}

private class FakeTokenVault : SessionTokenStore {
    val stored: MutableMap<String, SessionTokens> = mutableMapOf()
    val writes: MutableList<SessionTokens> = mutableListOf()
    override suspend fun read(profileId: String): SessionTokens? = stored[profileId]
    override suspend fun write(profileId: String, tokens: SessionTokens) {
        writes += tokens
        stored[profileId] = tokens
    }
    override suspend fun clear(profileId: String) {
        stored.remove(profileId)
    }
}

private class FakeProfileStore : ActiveProfileStore {
    private var stored: ConnectionProfile? = null
    override suspend fun read(): ConnectionProfile? = stored
    override suspend fun write(profile: ConnectionProfile) {
        stored = profile
    }
    override suspend fun clear() {
        stored = null
    }
}

private class FakeChannelStore : ActiveChannelStore {
    var stored: String? = null
    override suspend fun read(): String? = stored
    override suspend fun write(channelId: String) {
        stored = channelId
    }
    override suspend fun clear() {
        stored = null
    }
}
