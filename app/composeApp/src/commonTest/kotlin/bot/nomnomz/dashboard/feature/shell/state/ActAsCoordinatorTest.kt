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
import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.InMemoryActAsSessionStore
import bot.nomnomz.dashboard.core.connection.PersistedActAs
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionPhase
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.navigation.RecordingAppReloader
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
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
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_ended_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_identity_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_revoke_failed
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves act-as is a full session swap through an app reload, over the real SessionStore and channel switcher:
//   - begin proves the target, persists the act-as marker (never a refresh token) and reloads to the target's
//     default page; the operator's remembered channel is never written;
//   - the boot after the reload (and every F5 while acting) opens the TARGET's session first — identity, token,
//     channel list, selected channel — without reading the operator's remembered channel or vaulting the token;
//   - an expired or rejected marker falls back to the operator with a one-shot notice;
//   - Exit re-mints the operator's token from the refresh cookie (the act-as token cannot revoke its own grant),
//     revokes, clears the marker and reloads to where the operator started.
@OptIn(ExperimentalCoroutinesApi::class)
class ActAsCoordinatorTest {

    private val profile = ConnectionProfile(
        id = "p1",
        displayName = "Self-host",
        baseUrl = "http://localhost:5080",
        source = ProfileSource.ServedOrigin,
    )

    private val operatorMe = CurrentUser(
        id = "operator-id",
        username = "stoney_eagle",
        displayName = "Stoney_Eagle",
        profileImageUrl = "https://cdn/operator.png",
        color = "#FF0000",
        isAdmin = true,
    )
    private val targetMe = CurrentUser(
        id = "target-id",
        username = "anda_six",
        displayName = "anda_six",
        profileImageUrl = "https://cdn/target.png",
        color = "#00FF00",
        isAdmin = false,
    )

    private val token = ImpersonationTokenDto(
        accessToken = TARGET_JWT,
        expiresAt = "2030-01-01T00:00:00Z",
        sessionId = "grant-1",
        user = UserSearchResult(id = "target-id", displayName = "anda_six"),
    )

    private val liveMarker = PersistedActAs(
        accessToken = TARGET_JWT,
        expiresAt = "2030-01-01T00:00:00Z",
        accessGrantId = "grant-1",
        displayName = "anda_six",
        returnLocation = ADMIN_ROUTE,
    )

    /** What survives a page reload: the vault, the remembered profile + channel, and the tab's act-as marker. */
    private class Persistent(
        val vault: FakeTokenVault = FakeTokenVault(),
        val profiles: FakeProfileStore = FakeProfileStore(),
        val remembered: FakeChannelStore = FakeChannelStore(),
        val actAs: InMemoryActAsSessionStore = InMemoryActAsSessionStore(),
    )

    /** One boot of the app: everything in memory is new, only [Persistent] carries over. */
    private class Boot(
        val store: SessionStore,
        val coordinator: ActAsCoordinator,
        val switcher: ChannelSwitcherController,
        val feedback: RecordingFeedback,
        val reloader: RecordingAppReloader,
        val steps: MutableList<String>,
        val revokeTokens: MutableList<String?>,
    )

    private fun boot(
        scope: CoroutineScope,
        persistent: Persistent,
        location: String = "",
        revokeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
        targetMeResult: ApiResult<CurrentUser> = ApiResult.Ok(targetMe),
        now: Instant = Instant.parse("2026-09-29T12:00:00Z"),
    ): Boot {
        val store = SessionStore(persistent.vault, persistent.profiles, persistent.remembered, scope)
        val steps: MutableList<String> = mutableListOf()
        val revokeTokens: MutableList<String?> = mutableListOf()
        val feedback = RecordingFeedback()
        val reloader = RecordingAppReloader(location)
        val switcher = ChannelSwitcherController(IdentityChannelsApi(store, steps), NoProvisioning, store) {}
        val coordinator = ActAsCoordinator(
            sessionStore = store,
            authApi = IdentityAuthApi(store, operatorMe, targetMeResult, steps),
            revokeSession = { grant ->
                steps += "revoke:$grant"
                revokeTokens += store.accessToken()
                revokeResult
            },
            actAsStore = persistent.actAs,
            reloader = reloader,
            feedback = feedback,
            scope = scope,
            clock = FixedClock(now),
        )
        return Boot(store, coordinator, switcher, feedback, reloader, steps, revokeTokens)
    }

    // The operator signed in on their own channel, chosen explicitly (so it is remembered), sitting on the admin page.
    private suspend fun signedInOperator(scope: CoroutineScope, persistent: Persistent, revoke: ApiResult<Unit> = ApiResult.Ok(Unit), targetMe: ApiResult<CurrentUser> = ApiResult.Ok(this.targetMe)): Boot {
        val b = boot(scope, persistent, location = ADMIN_ROUTE, revokeResult = revoke, targetMeResult = targetMe)
        b.store.connect(profile, SessionTokens(accessToken = OPERATOR_JWT))
        b.store.setUser(SessionUser("operator-id", "stoney_eagle", "Stoney_Eagle", null, isAdmin = true))
        b.switcher.load()
        b.switcher.select(OPERATOR_CHANNEL)
        b.steps.clear()
        return b
    }

    @Test
    fun begin_persists_the_act_as_marker_and_reloads_the_app_onto_the_targets_default_page() = runTest {
        val persistent = Persistent()
        val b = signedInOperator(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)

        val began: Boolean = b.coordinator.begin(token, "anda_six")

        assertTrue(began)
        // The target token was proven before anything was committed.
        assertEquals(listOf("me:$TARGET_JWT"), b.steps)
        // The marker carries the access-only act-as token and where the operator started — never a refresh token.
        assertEquals(liveMarker, persistent.actAs.session)
        // One full reload, at the target's default landing page (no operator route).
        assertEquals(listOf(""), b.reloader.reloads)
        // Nothing of the operator's custody changed: the vault holds no act-as token, the remembered channel stands.
        assertEquals(OPERATOR_JWT, persistent.vault.stored[profile.id]?.accessToken)
        assertEquals(OPERATOR_CHANNEL, persistent.remembered.stored)
    }

    @Test
    fun an_unreadable_target_identity_never_reloads_and_leaves_the_operator_in_place() = runTest {
        val persistent = Persistent()
        val b = signedInOperator(
            CoroutineScope(UnconfinedTestDispatcher(testScheduler)),
            persistent,
            targetMe = ApiResult.Failure(ApiError(status = 500, code = "500", message = "down")),
        )

        val began: Boolean = b.coordinator.begin(token, "anda_six")

        assertFalse(began)
        assertTrue(b.reloader.reloads.isEmpty(), "never reload into a session that cannot load")
        assertNull(persistent.actAs.session)
        assertNull(b.store.impersonating.value)
        assertEquals(OPERATOR_JWT, b.store.accessToken())
        assertEquals(OPERATOR_CHANNEL, b.store.activeChannelId.value)
        // The grant was revoked on the OPERATOR's token.
        assertEquals(listOf<String?>(OPERATOR_JWT), b.revokeTokens)
        assertEquals(Res.string.shell_impersonation_identity_failed, b.feedback.only.label)
    }

    @Test
    fun the_boot_after_the_reload_is_the_target_in_full_and_never_touches_the_operators_custody() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker))
        persistent.remembered.stored = OPERATOR_CHANNEL
        val b = boot(scope, persistent)

        val resumed: Boolean = b.coordinator.resume(profile, operatorTokens = null)
        b.switcher.load()

        assertTrue(resumed)
        assertEquals(SessionPhase.Connected, b.store.phase.value)
        // Every identity-bearing value is the target's: token, /me identity (name, avatar, no admin plane).
        assertEquals(TARGET_JWT, b.store.accessToken())
        assertEquals("anda_six", b.store.user.value?.displayName)
        assertEquals("https://cdn/target.png", b.store.user.value?.profileImageUrl)
        assertFalse(b.store.user.value!!.isAdmin)
        assertEquals("anda_six", b.store.impersonating.value?.displayName)
        // The roster and the selected channel come from the target's token; the operator's remembered channel is
        // neither read (it would have been selected) nor overwritten.
        assertEquals(TARGET_CHANNEL, b.store.activeChannelId.value)
        assertEquals(listOf(TARGET_CHANNEL), (b.switcher.state.value as SwitcherState.Ready).channels.map { it.id })
        assertEquals(OPERATOR_CHANNEL, persistent.remembered.stored)
        // The act-as token never entered the operator's vault.
        assertNull(persistent.vault.stored[profile.id])
        // The first request of the boot was the target's /me — nothing ran under the operator.
        assertEquals("me:$TARGET_JWT", b.steps.first())
        assertTrue(b.reloader.reloads.isEmpty())
    }

    @Test
    fun a_marker_past_its_expiry_is_dropped_and_the_boot_falls_back_to_the_operator_with_a_notice() = runTest {
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker.copy(expiresAt = "2026-09-29T11:00:00Z")))
        val b = boot(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)

        val resumed: Boolean = b.coordinator.resume(profile, operatorTokens = null)

        assertFalse(resumed)
        assertTrue(b.steps.isEmpty(), "an expired token is never sent")
        assertNull(persistent.actAs.session)
        assertNull(b.store.impersonating.value)
        assertEquals(ActAsEndNotice(ActAsEndReason.Expired), persistent.actAs.notice)
    }

    @Test
    fun a_marker_the_server_rejects_is_dropped_and_no_target_token_is_left_held() = runTest {
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker))
        val b = boot(
            CoroutineScope(UnconfinedTestDispatcher(testScheduler)),
            persistent,
            targetMeResult = ApiResult.Failure(ApiError(status = 401, code = "401", message = "revoked")),
        )

        val resumed: Boolean = b.coordinator.resume(profile, operatorTokens = null)

        assertFalse(resumed)
        assertNull(b.store.accessToken())
        assertNull(b.store.impersonating.value)
        assertEquals(SessionPhase.NotConnected, b.store.phase.value)
        assertNull(persistent.actAs.session)
        assertEquals(ActAsEndNotice(ActAsEndReason.Expired), persistent.actAs.notice)
    }

    @Test
    fun exit_after_a_reload_re_mints_the_operator_token_revokes_on_it_and_reloads_to_where_they_started() = runTest {
        val scope = CoroutineScope(UnconfinedTestDispatcher(testScheduler))
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker))
        val b = boot(scope, persistent)
        b.coordinator.resume(profile, operatorTokens = null)
        b.steps.clear()

        b.coordinator.exit()

        // The operator's token came from the refresh cookie (web: no refresh token in JS), BEFORE the revoke.
        assertEquals(listOf("refresh:null", "revoke:grant-1"), b.steps)
        assertEquals(listOf<String?>(OPERATOR_FRESH_JWT), b.revokeTokens)
        assertNull(persistent.actAs.session, "the marker is gone, so the reload boots as the operator")
        assertNull(persistent.actAs.notice, "a clean exit leaves nothing to report")
        assertEquals(listOf(ADMIN_ROUTE), b.reloader.reloads)
    }

    @Test
    fun a_failed_revoke_still_exits_and_the_reason_survives_the_reload() = runTest {
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker))
        val b = boot(
            CoroutineScope(UnconfinedTestDispatcher(testScheduler)),
            persistent,
            revokeResult = ApiResult.Failure(ApiError(status = 500, code = "500", message = "boom")),
        )
        b.coordinator.resume(profile, operatorTokens = null)

        b.coordinator.exit()

        assertNull(persistent.actAs.session)
        assertEquals(ActAsEndNotice(ActAsEndReason.RevokeFailed, "boom"), persistent.actAs.notice)
        assertEquals(listOf(ADMIN_ROUTE), b.reloader.reloads)

        // The next boot tells the operator, exactly once.
        val next = boot(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)
        next.coordinator.surfacePendingNotice()
        next.coordinator.surfacePendingNotice()
        val message = next.feedback.only
        assertEquals(FeedbackKind.Error, message.kind)
        assertEquals(Res.string.shell_impersonation_revoke_failed, message.label)
        assertEquals(listOf<Any>("boom"), message.formatArgs)
    }

    @Test
    fun a_rejected_act_as_token_runs_the_full_exit_and_the_next_boot_says_it_ended() = runTest {
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker))
        val b = boot(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)
        b.coordinator.resume(profile, operatorTokens = null)

        b.coordinator.onActAsTokenRejected().join()

        assertNull(b.store.impersonating.value)
        assertEquals(listOf<String?>(OPERATOR_FRESH_JWT), b.revokeTokens)
        assertNull(persistent.actAs.session)
        assertEquals(listOf(ADMIN_ROUTE), b.reloader.reloads)
        val next = boot(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)
        next.coordinator.surfacePendingNotice()
        assertEquals(Res.string.shell_impersonation_ended_expired, next.feedback.only.label)
    }

    @Test
    fun exit_for_logout_revokes_on_the_operator_token_without_reloading() = runTest {
        val persistent = Persistent(actAs = InMemoryActAsSessionStore(liveMarker))
        val b = boot(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)
        b.coordinator.resume(profile, operatorTokens = null)

        b.coordinator.exitWithoutReload()

        assertEquals(listOf<String?>(OPERATOR_FRESH_JWT), b.revokeTokens)
        assertEquals(OPERATOR_FRESH_JWT, b.store.accessToken(), "the logout that follows carries the operator token")
        assertNull(persistent.actAs.session)
        assertTrue(b.reloader.reloads.isEmpty())
    }

    @Test
    fun exit_when_not_acting_changes_nothing() = runTest {
        val persistent = Persistent()
        val b = signedInOperator(CoroutineScope(UnconfinedTestDispatcher(testScheduler)), persistent)

        b.coordinator.exit()

        assertTrue(b.steps.isEmpty())
        assertTrue(b.reloader.reloads.isEmpty())
        assertEquals(OPERATOR_JWT, b.store.accessToken())
    }

    private companion object {
        const val OPERATOR_JWT: String = "operator-jwt"
        const val OPERATOR_FRESH_JWT: String = "operator-fresh-jwt"
        const val TARGET_JWT: String = "target-jwt"
        const val OPERATOR_CHANNEL: String = "operator-channel"
        const val TARGET_CHANNEL: String = "target-channel"
        const val ADMIN_ROUTE: String = "#/admin"
    }

    private class FixedClock(private val instant: Instant) : Clock {
        override fun now(): Instant = instant
    }

    // The backend answers per token, exactly like the real one: the roster is the caller's own.
    private class IdentityChannelsApi(private val store: SessionStore, private val steps: MutableList<String>) : ChannelsApi {
        override suspend fun list(): ApiResult<List<ChannelSummary>> {
            steps += "list:${store.accessToken()}"
            return ApiResult.Ok(
                if (store.accessToken() == TARGET_JWT) {
                    listOf(ChannelSummary(id = TARGET_CHANNEL, displayName = "Target", chatColor = "#TARGET"))
                } else {
                    listOf(ChannelSummary(id = OPERATOR_CHANNEL, displayName = "Operator", chatColor = "#OPERATOR"))
                },
            )
        }

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

    // /me answers for whoever holds the token; refresh re-mints the OPERATOR's token from the cookie.
    private class IdentityAuthApi(
        private val store: SessionStore,
        private val operator: CurrentUser,
        private val target: ApiResult<CurrentUser>,
        private val steps: MutableList<String>,
    ) : AuthApi {
        override suspend fun me(): ApiResult<CurrentUser> {
            steps += "me:${store.accessToken()}"
            return if (store.accessToken() == TARGET_JWT) target else ApiResult.Ok(operator)
        }

        override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> {
            steps += "refresh:$refreshToken"
            return ApiResult.Ok(AuthPayload(accessToken = OPERATOR_FRESH_JWT))
        }

        override suspend fun providers(): ApiResult<List<LoginProvider>> = ApiResult.Ok(emptyList())
        override suspend fun startDeviceLogin(provider: String): ApiResult<DeviceCodeStart> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun pollDeviceLogin(provider: String, deviceCode: String): ApiResult<DeviceLoginPoll> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun logout(): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    private object NoProvisioning : ChannelProvisioningApi {
        override suspend fun enterModeratedChannel(twitchBroadcasterId: String): ApiResult<ChannelSummary> =
            ApiResult.Failure(ApiError(501, null, "unused"))
    }
}

private class FakeTokenVault : SessionTokenStore {
    val stored: MutableMap<String, SessionTokens> = mutableMapOf()
    override suspend fun read(profileId: String): SessionTokens? = stored[profileId]
    override suspend fun write(profileId: String, tokens: SessionTokens) {
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
