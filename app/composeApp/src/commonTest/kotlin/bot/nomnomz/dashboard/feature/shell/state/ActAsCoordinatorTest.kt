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

import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
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
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_ended_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_identity_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_revoke_failed
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves the act-as lifecycle end to end over the real SessionStore and the real channel switcher: while acting,
// every identity-bearing value the shell reads (the X-Channel-Id source, the channel list, the /me identity that
// drives the profile menu and the admin nav entry, the accent) is the TARGET's; nothing reads or writes the
// operator's remembered channel; Exit restores the operator token BEFORE the revoke call; a rejected act-as
// token runs the same exit instead of installing the operator's refreshed token.
@OptIn(ExperimentalCoroutinesApi::class)
class ActAsCoordinatorTest {

    private val profile = ConnectionProfile(
        id = "p1",
        displayName = "Self-host",
        baseUrl = "http://localhost:5080",
        source = ProfileSource.Manual,
    )

    private val operatorMe = CurrentUser(
        id = "operator-id",
        username = "operator",
        displayName = "The Operator",
        profileImageUrl = "https://cdn/operator.png",
        color = "#FF0000",
        isAdmin = true,
    )
    private val targetMe = CurrentUser(
        id = "target-id",
        username = "target",
        displayName = "Target Streamer",
        profileImageUrl = "https://cdn/target.png",
        color = "#00FF00",
        isAdmin = false,
    )

    private val token = ImpersonationTokenDto(
        accessToken = TARGET_JWT,
        expiresAt = "2030-01-01T00:00:00Z",
        sessionId = "grant-1",
        user = UserSearchResult(id = "target-id", displayName = "Target Streamer"),
    )

    private class Harness(
        val store: SessionStore,
        val remembered: FakeChannelStore,
        val coordinator: ActAsCoordinator,
        val switcher: ChannelSwitcherController,
        val feedback: RecordingFeedback,
        val steps: MutableList<String>,
        val revokeTokens: MutableList<String?>,
        val accents: MutableList<String?>,
    )

    private suspend fun harness(
        scope: CoroutineScope,
        revokeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
        targetMeResult: ApiResult<CurrentUser>? = null,
    ): Harness {
        val remembered = FakeChannelStore()
        val store = SessionStore(FakeTokenVault(), FakeProfileStore(), remembered, scope)
        store.connect(profile, SessionTokens(accessToken = OPERATOR_JWT, refreshToken = "operator-refresh", expiresAt = null))
        val steps: MutableList<String> = mutableListOf()
        val revokeTokens: MutableList<String?> = mutableListOf()
        val accents: MutableList<String?> = mutableListOf()
        val feedback = RecordingFeedback()
        val channels = IdentityChannelsApi(store, steps)
        val switcher = ChannelSwitcherController(channels, NoProvisioning, store) { accents += it }
        val auth = IdentityAuthApi(store, operatorMe, targetMeResult ?: ApiResult.Ok(targetMe), steps)
        val coordinator = ActAsCoordinator(
            sessionStore = store,
            authApi = auth,
            revokeSession = { grant ->
                steps += "revoke:$grant"
                revokeTokens += store.accessToken()
                revokeResult
            },
            reloadRoster = switcher::load,
            resolveAccess = { steps += "role:${store.activeChannelId.value}" },
            reconnectHubs = { steps += "hubs:${store.activeChannelId.value}" },
            applyAccent = { accents += it },
            clearReauthPrompt = { steps += "clear-reauth" },
            feedback = feedback,
            scope = scope,
        )
        // The operator is signed in on their own channel, chosen explicitly (so it is remembered).
        store.setUser(SessionUser("operator-id", "operator", "The Operator", null, isAdmin = true))
        switcher.load()
        switcher.select(OPERATOR_CHANNEL)
        steps.clear()
        accents.clear()
        return Harness(store, remembered, coordinator, switcher, feedback, steps, revokeTokens, accents)
    }

    @Test
    fun acting_as_a_user_who_does_not_moderate_the_operators_channel_retargets_everything_to_them() = runTest {
        val h = harness(CoroutineScope(UnconfinedTestDispatcher(testScheduler)))
        assertEquals(OPERATOR_CHANNEL, h.store.activeChannelId.value)

        val began: Boolean = h.coordinator.begin(token, "Target Streamer")

        assertTrue(began)
        // X-Channel-Id is read from activeChannelId on every request: it is now the TARGET's channel.
        assertEquals(TARGET_CHANNEL, h.store.activeChannelId.value)
        // The channel list is the one the target's token returns — the operator's channel is not in it.
        val roster = h.switcher.state.value as SwitcherState.Ready
        assertEquals(listOf(TARGET_CHANNEL), roster.channels.map { it.id })
        // Profile menu name + avatar and the admin nav gate all read the /me identity: the target's.
        assertEquals("Target Streamer", h.store.user.value?.displayName)
        assertEquals("https://cdn/target.png", h.store.user.value?.profileImageUrl)
        assertFalse(h.store.user.value!!.isAdmin, "no admin plane while acting as a non-admin")
        // Accent: the target's chat colour, then their channel's.
        assertEquals(listOf<String?>("#00FF00", "#TARGET"), h.accents)
        // Order: operator re-auth prompt cleared, identity, roster (on the target token), role, then hubs.
        assertEquals(
            listOf("clear-reauth", "me:$TARGET_JWT", "list:$TARGET_JWT", "role:$TARGET_CHANNEL", "hubs:$TARGET_CHANNEL"),
            h.steps.filterNot { it.startsWith("moderated") },
        )
        // The operator's remembered channel was neither read for the target nor overwritten.
        assertEquals(OPERATOR_CHANNEL, h.remembered.stored)
    }

    @Test
    fun exit_restores_the_operator_token_before_revoking_and_lands_back_on_the_operators_channel() = runTest {
        val h = harness(CoroutineScope(UnconfinedTestDispatcher(testScheduler)))
        h.coordinator.begin(token, "Target Streamer")
        h.switcher.select(TARGET_CHANNEL)
        h.steps.clear()
        h.accents.clear()

        h.coordinator.exit()

        // The revoke carried the OPERATOR token: an act-as token can never revoke its own grant (403).
        assertEquals(listOf<String?>(OPERATOR_JWT), h.revokeTokens)
        assertEquals("revoke:grant-1", h.steps.first())
        assertNull(h.store.impersonating.value)
        assertEquals(OPERATOR_JWT, h.store.accessToken())
        assertEquals(OPERATOR_CHANNEL, h.store.activeChannelId.value)
        assertEquals("The Operator", h.store.user.value?.displayName)
        assertTrue(h.store.user.value!!.isAdmin)
        assertEquals(OPERATOR_CHANNEL, h.remembered.stored, "a switch made while acting is never remembered")
        assertEquals(listOf<String?>("#FF0000", "#OPERATOR"), h.accents)
        assertTrue(h.feedback.messages.isEmpty())
    }

    @Test
    fun a_failed_revoke_is_surfaced_and_the_operator_is_still_restored() = runTest {
        val h = harness(
            CoroutineScope(UnconfinedTestDispatcher(testScheduler)),
            revokeResult = ApiResult.Failure(ApiError(status = 500, code = "500", message = "boom")),
        )
        h.coordinator.begin(token, "Target Streamer")

        h.coordinator.exit()

        assertEquals(OPERATOR_JWT, h.store.accessToken())
        val message = h.feedback.only
        assertEquals(FeedbackKind.Error, message.kind)
        assertEquals(Res.string.shell_impersonation_revoke_failed, message.label)
        assertEquals(listOf<Any>("boom"), message.formatArgs)
    }

    @Test
    fun a_rejected_act_as_token_runs_the_full_exit_and_says_why() = runTest {
        val h = harness(CoroutineScope(UnconfinedTestDispatcher(testScheduler)))
        h.coordinator.begin(token, "Target Streamer")

        h.coordinator.onActAsTokenRejected().join()

        assertNull(h.store.impersonating.value)
        assertEquals(OPERATOR_JWT, h.store.accessToken())
        assertEquals(listOf<String?>(OPERATOR_JWT), h.revokeTokens)
        assertEquals(Res.string.shell_impersonation_ended_expired, h.feedback.only.label)
    }

    @Test
    fun an_unreadable_target_identity_rolls_back_to_the_operator_and_revokes() = runTest {
        val h = harness(
            CoroutineScope(UnconfinedTestDispatcher(testScheduler)),
            targetMeResult = ApiResult.Failure(ApiError(status = 500, code = "500", message = "down")),
        )

        val began: Boolean = h.coordinator.begin(token, "Target Streamer")

        assertFalse(began)
        assertNull(h.store.impersonating.value)
        assertEquals(OPERATOR_JWT, h.store.accessToken())
        assertEquals("The Operator", h.store.user.value?.displayName)
        assertEquals(listOf<String?>(OPERATOR_JWT), h.revokeTokens)
        assertEquals(Res.string.shell_impersonation_identity_failed, h.feedback.only.label)
    }

    private companion object {
        const val OPERATOR_JWT: String = "operator-jwt"
        const val TARGET_JWT: String = "target-jwt"
        const val OPERATOR_CHANNEL: String = "operator-channel"
        const val TARGET_CHANNEL: String = "target-channel"
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

        override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> {
            steps += "moderated"
            return ApiResult.Ok(emptyList())
        }

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

        override suspend fun providers(): ApiResult<List<LoginProvider>> = ApiResult.Ok(emptyList())
        override suspend fun startDeviceLogin(provider: String): ApiResult<DeviceCodeStart> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun pollDeviceLogin(provider: String, deviceCode: String): ApiResult<DeviceLoginPoll> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> =
            ApiResult.Failure(ApiError(501, null, "unused"))
        override suspend fun logout(): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    private object NoProvisioning : ChannelProvisioningApi {
        override suspend fun enterModeratedChannel(twitchBroadcasterId: String): ApiResult<ChannelSummary> =
            ApiResult.Failure(ApiError(501, null, "unused"))
    }
}

private class FakeTokenVault : SessionTokenStore {
    private val stored: MutableMap<String, SessionTokens> = mutableMapOf()
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
