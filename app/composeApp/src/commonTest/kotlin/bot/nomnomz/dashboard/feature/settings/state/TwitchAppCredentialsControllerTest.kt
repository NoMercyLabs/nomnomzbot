// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the in-dashboard Twitch-app credential card's state machine — the management capability the card
// surfaces. These assert the resulting STATE (configured vs shared, the exact redirect URL derived from the
// active backend), the SIDE EFFECTS (which credentials were PUT through the wizard's own endpoint, that the
// status is re-read after a save rather than optimistically flipped), and the EMITTED feedback (success /
// error announced on the frame) — not surface calls. The secret is optional: a save with only a client id
// must go through and never gate on the secret.
class TwitchAppCredentialsControllerTest {

    private fun controller(
        api: FakeSystemApi,
        baseUrl: String? = "https://bot.example.test",
        feedback: RecordingFeedback = RecordingFeedback(),
    ): TwitchAppCredentialsController =
        TwitchAppCredentialsController(api, { baseUrl }, feedback)

    @Test
    fun load_renders_shared_state_and_the_exact_redirect_url_when_no_personal_app_is_configured() = runTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val controller = controller(api, baseUrl = "https://bot.example.test/")

        controller.load()

        val ready: TwitchAppCredentialsState.Ready =
            controller.state.value as TwitchAppCredentialsState.Ready
        // Not configured ⇒ the bot runs on the shared client (the state the card reflects).
        assertFalse(ready.configured)
        // The redirect URL is rooted at the ACTIVE backend (trailing slash trimmed) + the fixed callback path.
        assertEquals(
            "https://bot.example.test/api/v1/auth/twitch/callback",
            ready.redirectUrl,
        )
    }

    @Test
    fun load_renders_configured_state_when_a_personal_app_is_set() = runTest {
        val api = FakeSystemApi(twitchConfigured = true)
        val controller = controller(api)

        controller.load()

        val ready: TwitchAppCredentialsState.Ready =
            controller.state.value as TwitchAppCredentialsState.Ready
        assertTrue(ready.configured)
    }

    @Test
    fun load_surfaces_the_error_state_when_the_status_read_fails() = runTest {
        val api = FakeSystemApi(twitchConfigured = false, statusError = "backend unreachable")
        val controller = controller(api)

        controller.load()

        val error: TwitchAppCredentialsState.Error =
            controller.state.value as TwitchAppCredentialsState.Error
        assertEquals("backend unreachable", error.detail)
    }

    @Test
    fun save_puts_the_credentials_through_the_wizard_endpoint_then_reflects_configured_from_the_reread() = runTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val feedback = RecordingFeedback()
        val controller = controller(api, feedback = feedback)
        controller.load()
        assertFalse((controller.state.value as TwitchAppCredentialsState.Ready).configured)

        // The backend becomes configured once the personal app is saved.
        api.twitchConfiguredAfter = true
        controller.save(clientId = "  myclientid  ", clientSecret = "  mysecret  ")

        // The exact (trimmed) credentials were PUT — no bot username from this surface.
        assertEquals("myclientid", api.savedClientId)
        assertEquals("mysecret", api.savedClientSecret)
        assertNull(api.savedBotUsername)
        // The card now reflects the backend's re-read: configured, not saving, no error.
        val ready: TwitchAppCredentialsState.Ready =
            controller.state.value as TwitchAppCredentialsState.Ready
        assertTrue(ready.configured)
        assertFalse(ready.saving)
        assertNull(ready.saveError)
        // The outcome was announced on the frame.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
    }

    @Test
    fun save_with_only_a_client_id_goes_through_the_secret_is_optional() = runTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val controller = controller(api)
        controller.load()

        api.twitchConfiguredAfter = true
        // No secret at all — the device-code path needs none; the save must still PUT and succeed.
        controller.save(clientId = "idonly", clientSecret = "")

        assertEquals("idonly", api.savedClientId)
        assertEquals("", api.savedClientSecret)
        assertTrue((controller.state.value as TwitchAppCredentialsState.Ready).configured)
    }

    @Test
    fun save_with_a_blank_client_id_surfaces_the_missing_id_error_and_never_calls_the_backend() = runTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val controller = controller(api)
        controller.load()

        controller.save(clientId = "   ", clientSecret = "asecret")

        val ready: TwitchAppCredentialsState.Ready =
            controller.state.value as TwitchAppCredentialsState.Ready
        assertEquals(SaveError.MissingClientId, ready.saveError)
        // The required-field guard is client-side: nothing reached the backend.
        assertNull(api.savedClientId)
    }

    @Test
    fun a_failed_save_surfaces_the_backend_error_inline_and_on_the_frame_without_losing_the_configured_state() = runTest {
        val api = FakeSystemApi(twitchConfigured = true, saveError = "forbidden")
        val feedback = RecordingFeedback()
        val controller = controller(api, feedback = feedback)
        controller.load()

        controller.save(clientId = "newid", clientSecret = "newsecret")

        val ready: TwitchAppCredentialsState.Ready =
            controller.state.value as TwitchAppCredentialsState.Ready
        // The backend rejected the overwrite; the inline error carries the detail and the card stays usable.
        val saveError: SaveError? = ready.saveError
        assertTrue(saveError is SaveError.Backend && saveError.detail == "forbidden")
        assertFalse(ready.saving)
        // The prior configured state is preserved (no spurious flip to "shared").
        assertTrue(ready.configured)
        // The failure was announced on the frame.
        assertEquals(FeedbackKind.Error, feedback.only.kind)
    }

    @Test
    fun the_redirect_url_is_null_when_no_backend_is_active() = runTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val controller = controller(api, baseUrl = null)

        controller.load()

        val ready: TwitchAppCredentialsState.Ready =
            controller.state.value as TwitchAppCredentialsState.Ready
        assertNull(ready.redirectUrl)
    }
}

// ── Fakes ─────────────────────────────────────────────────────────────────────

private fun checks(twitch: Boolean): SystemChecks =
    SystemChecks(
        // `twitch` models the personal-app-with-secret signal the card reads (twitchApp.ok). A client id alone
        // is still ready (device-code), so ready stays true regardless of the secret.
        twitchApp =
            SystemCheck(
                ok = twitch,
                ready = true,
                status = if (twitch) "ready_redirect" else "ready_device",
            ),
        platformBot = SystemCheck(ok = true, ready = true, status = "connected"),
    )

// A minimal SystemApi fake: this surface only reads status + saves Twitch credentials, so the wizard /
// bot / complete calls are unused and never asserted here.
private class FakeSystemApi(
    private val twitchConfigured: Boolean,
    private val statusError: String? = null,
    private val saveError: String? = null,
) : SystemApi {
    // Model the backend flipping to "configured" between the save and the post-save status re-read.
    var twitchConfiguredAfter: Boolean? = null

    var savedClientId: String? = null
    var savedClientSecret: String? = null
    var savedBotUsername: String? = null

    override suspend fun status(): ApiResult<SystemStatus> {
        if (statusError != null) {
            return ApiResult.Failure(ApiError(status = 0, code = "X", message = statusError))
        }
        val twitch: Boolean = twitchConfiguredAfter ?: twitchConfigured
        return ApiResult.Ok(SystemStatus(ready = twitch, checks = checks(twitch)))
    }

    override suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit> {
        if (saveError != null) {
            return ApiResult.Failure(ApiError(status = 403, code = "FORBIDDEN", message = saveError))
        }
        savedClientId = clientId
        savedClientSecret = clientSecret
        savedBotUsername = botUsername
        return ApiResult.Ok(Unit)
    }

    override suspend fun wizard(): ApiResult<SetupWizard> = ApiResult.Ok(SetupWizard(complete = false))

    override suspend fun saveSpotifyCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun saveYouTubeCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun saveDiscordCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> = ApiResult.Ok(BotOAuthUrl("https://unused"))

    override suspend fun botStatus(): ApiResult<BotStatus> = ApiResult.Ok(BotStatus(connected = true))

    override suspend fun completeSetup(): ApiResult<Unit> = ApiResult.Ok(Unit)
}
