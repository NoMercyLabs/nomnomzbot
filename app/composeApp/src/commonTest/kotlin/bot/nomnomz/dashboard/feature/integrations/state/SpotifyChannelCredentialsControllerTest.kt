// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.state

import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSpotifyCredentials
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OAuthStart
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary

// Proves the channel-scoped Spotify BYOC credential card's state machine (S-BYOC-spotify-b): saving PUTs the
// exact typed body to the channel-scoped route and the resulting state reports OWN credentials with the
// backend's `hasClientSecret`, with the typed secret retained NOWHERE in the exposed state; clearing DELETEs
// and the state falls back to reporting the app-level default. These assert the resulting STATE and the
// SIDE EFFECT (what was sent), never a surface "didn't throw".
class SpotifyChannelCredentialsControllerTest {

    private val channel = ChannelSummary(id = "chan-guid-1", login = "stoney_eagle", displayName = "Stoney_Eagle")

    private fun controller(
        integrations: FakeIntegrationsApiForSpotifyCreds,
        channels: ChannelsApi = FakeChannelsApiForSpotifyCreds(ApiResult.Ok(channel)),
        baseUrl: String? = "https://bot.example.test",
        feedback: RecordingFeedback = RecordingFeedback(),
    ): SpotifyChannelCredentialsController {
        val session = SessionStore(FakeVaultForSpotifyCreds())
        if (baseUrl != null) {
            session.pin(
                ConnectionProfile(
                    id = "test-profile",
                    displayName = "test",
                    baseUrl = baseUrl,
                    source = ProfileSource.Manual,
                )
            )
        }
        return SpotifyChannelCredentialsController(channels, integrations, session, feedback)
    }

    @Test
    fun load_reports_the_app_level_default_when_the_channel_has_no_own_client_id() = runTest {
        val api = FakeIntegrationsApiForSpotifyCreds(initial = ChannelSpotifyCredentials(clientId = null, hasClientSecret = false))
        val controller = controller(api)

        controller.load()

        val ready: SpotifyChannelCredentialsState.Ready =
            controller.state.value as SpotifyChannelCredentialsState.Ready
        assertFalse(ready.usingOwnCredentials)
        assertFalse(ready.hasClientSecret)
        assertEquals(
            "https://bot.example.test/api/v1/integrations/spotify/callback",
            ready.redirectUrl,
        )
    }

    @Test
    fun load_reports_own_credentials_when_the_channel_has_a_client_id() = runTest {
        val api =
            FakeIntegrationsApiForSpotifyCreds(
                initial = ChannelSpotifyCredentials(clientId = "existing-client", hasClientSecret = true)
            )
        val controller = controller(api)

        controller.load()

        val ready: SpotifyChannelCredentialsState.Ready =
            controller.state.value as SpotifyChannelCredentialsState.Ready
        assertTrue(ready.usingOwnCredentials)
        assertTrue(ready.hasClientSecret)
    }

    @Test
    fun save_puts_the_exact_credentials_and_the_state_reflects_own_credentials_from_the_reread() = runTest {
        val api = FakeIntegrationsApiForSpotifyCreds(initial = ChannelSpotifyCredentials(clientId = null, hasClientSecret = false))
        val feedback = RecordingFeedback()
        val controller = controller(api, feedback = feedback)
        controller.load()
        assertFalse((controller.state.value as SpotifyChannelCredentialsState.Ready).usingOwnCredentials)

        // The backend flips to "own, with a secret" once the PUT lands.
        api.afterSave = ChannelSpotifyCredentials(clientId = "my-client", hasClientSecret = true)
        controller.save(clientId = "  my-client  ", clientSecret = "  my-secret  ")

        // The exact (trimmed) credentials reached the right channel-scoped route.
        assertEquals("chan-guid-1", api.savedChannelId)
        assertEquals("my-client", api.savedClientId)
        assertEquals("my-secret", api.savedClientSecret)

        val ready: SpotifyChannelCredentialsState.Ready =
            controller.state.value as SpotifyChannelCredentialsState.Ready
        assertTrue(ready.usingOwnCredentials)
        assertTrue(ready.hasClientSecret)
        assertFalse(ready.saving)
        assertNull(ready.saveError)
        assertEquals(FeedbackKind.Success, feedback.only.kind)

        // The typed secret is retained NOWHERE in the exposed state — Ready carries no secret-value field at
        // all, and its string representation never contains what was typed.
        assertFalse(ready.toString().contains("my-secret"))
    }

    @Test
    fun save_with_a_blank_client_id_surfaces_the_missing_id_error_and_never_calls_the_backend() = runTest {
        val api = FakeIntegrationsApiForSpotifyCreds(initial = ChannelSpotifyCredentials(clientId = null, hasClientSecret = false))
        val controller = controller(api)
        controller.load()

        controller.save(clientId = "   ", clientSecret = "asecret")

        val ready: SpotifyChannelCredentialsState.Ready =
            controller.state.value as SpotifyChannelCredentialsState.Ready
        assertEquals(SpotifySaveError.MissingClientId, ready.saveError)
        assertNull(api.savedClientId)
    }

    @Test
    fun a_failed_save_surfaces_the_backend_error_and_keeps_the_prior_state() = runTest {
        val api =
            FakeIntegrationsApiForSpotifyCreds(
                initial = ChannelSpotifyCredentials(clientId = "existing", hasClientSecret = true),
                saveError = "forbidden",
            )
        val feedback = RecordingFeedback()
        val controller = controller(api, feedback = feedback)
        controller.load()

        controller.save(clientId = "newid", clientSecret = "newsecret")

        val ready: SpotifyChannelCredentialsState.Ready =
            controller.state.value as SpotifyChannelCredentialsState.Ready
        val saveError: SpotifySaveError? = ready.saveError
        assertTrue(saveError is SpotifySaveError.Backend && saveError.detail == "forbidden")
        assertFalse(ready.saving)
        // No optimistic flip on failure — still reporting the prior own-credentials state.
        assertTrue(ready.usingOwnCredentials)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
    }

    @Test
    fun clear_deletes_and_the_state_falls_back_to_the_app_level_default() = runTest {
        val api =
            FakeIntegrationsApiForSpotifyCreds(initial = ChannelSpotifyCredentials(clientId = "existing", hasClientSecret = true))
        val feedback = RecordingFeedback()
        val controller = controller(api, feedback = feedback)
        controller.load()
        assertTrue((controller.state.value as SpotifyChannelCredentialsState.Ready).usingOwnCredentials)

        // The backend reverts to "no own client" once the DELETE lands.
        api.afterClear = ChannelSpotifyCredentials(clientId = null, hasClientSecret = false)
        controller.clear()

        assertEquals("chan-guid-1", api.clearedChannelId)
        val ready: SpotifyChannelCredentialsState.Ready =
            controller.state.value as SpotifyChannelCredentialsState.Ready
        assertFalse(ready.usingOwnCredentials)
        assertFalse(ready.hasClientSecret)
        assertEquals(FeedbackKind.Success, feedback.only.kind)
    }
}

// ── Fakes ─────────────────────────────────────────────────────────────────────

private class FakeChannelsApiForSpotifyCreds(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeIntegrationsApiForSpotifyCreds(
    private val initial: ChannelSpotifyCredentials,
    private val saveError: String? = null,
) : IntegrationsApi {
    // Not exercised here: the counted delete preview has its own tests. The seam is implemented so the double
    // stays a real implementation of the interface rather than a partial one.
    override suspend fun disconnectBlastRadius(channelId: String, provider: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

    var afterSave: ChannelSpotifyCredentials? = null
    var afterClear: ChannelSpotifyCredentials? = null
    private var current: ChannelSpotifyCredentials = initial

    var savedChannelId: String? = null
    var savedClientId: String? = null
    var savedClientSecret: String? = null
    var clearedChannelId: String? = null

    override suspend fun status(channelId: String): ApiResult<List<IntegrationStatus>> = ApiResult.Ok(emptyList())

    override suspend fun startGenericConnect(
        channelId: String,
        provider: String,
        scopeSetKey: String,
        returnUrl: String?,
    ): ApiResult<OAuthStart> = error("stub")

    override fun discordStartUrl(baseUrl: String, channelId: String): String = error("stub")

    override suspend fun disconnectGeneric(channelId: String, provider: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun disconnectDiscord(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun spotifyCredentials(channelId: String): ApiResult<ChannelSpotifyCredentials> =
        ApiResult.Ok(current)

    override suspend fun saveSpotifyCredentials(
        channelId: String,
        clientId: String,
        clientSecret: String,
    ): ApiResult<ChannelSpotifyCredentials> {
        if (saveError != null) return ApiResult.Failure(ApiError(403, "FORBIDDEN", saveError))
        savedChannelId = channelId
        savedClientId = clientId
        savedClientSecret = clientSecret
        val result: ChannelSpotifyCredentials = afterSave ?: current
        current = result
        return ApiResult.Ok(result)
    }

    override suspend fun clearSpotifyCredentials(channelId: String): ApiResult<ChannelSpotifyCredentials> {
        clearedChannelId = channelId
        val result: ChannelSpotifyCredentials = afterClear ?: current
        current = result
        return ApiResult.Ok(result)
    }
}

private class FakeVaultForSpotifyCreds : SessionTokenStore {
    override suspend fun read(profileId: String): SessionTokens? = null

    override suspend fun write(profileId: String, tokens: SessionTokens) = Unit

    override suspend fun clear(profileId: String) = Unit
}
