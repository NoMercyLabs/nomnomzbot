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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.EventSubReconcileReport
import bot.nomnomz.dashboard.core.network.EventSubSubscription
import bot.nomnomz.dashboard.core.network.LoginProvider
import bot.nomnomz.dashboard.core.network.MissingScopes
import bot.nomnomz.dashboard.core.network.ScopeRegrantStart
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import bot.nomnomz.dashboard.core.network.TwitchScopeDiagnostics
import bot.nomnomz.dashboard.core.network.TwitchScopeRequirement
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Settings "Permissions" section (S070) reflects the REAL backend scope/feature matrix (never a
// hardcoded list) and that clicking re-grant drives the real device-code re-grant endpoint + the normal
// streamer device poll — the exact mechanism IntegrationsController's missing-scope banner already uses.
class PermissionsControllerTest {

    @Test
    fun load_surfaces_the_backends_granted_and_missing_rows() = runTest {
        val matrix = TwitchScopeDiagnostics(
            connectionStatus = "connected",
            grantedScopes = listOf("channel:manage:raids"),
            requirements = listOf(
                TwitchScopeRequirement(
                    scope = "channel:manage:raids",
                    feature = "raids",
                    granted = true,
                ),
                TwitchScopeRequirement(
                    scope = "channel:read:polls",
                    feature = "polls",
                    granted = false,
                ),
            ),
        )
        val controller = PermissionsController(FakeDiagnosticsApi(scopes = ApiResult.Ok(matrix)), FakeAuthApi())

        controller.load()

        val state: PermissionsState = controller.state.value
        assertTrue(state is PermissionsState.Ready)
        val ready: PermissionsState.Ready = state as PermissionsState.Ready
        assertEquals(2, ready.matrix.requirements.size)
        assertTrue(ready.matrix.requirements.first { it.scope == "channel:manage:raids" }.granted)
        assertTrue(!ready.matrix.requirements.first { it.scope == "channel:read:polls" }.granted)
    }

    @Test
    fun load_errors_when_the_backend_call_fails() = runTest {
        val controller =
            PermissionsController(
                FakeDiagnosticsApi(scopes = ApiResult.Failure(ApiError(500, "ERR", "boom"))),
                FakeAuthApi(),
            )

        controller.load()

        assertTrue(controller.state.value is PermissionsState.Error)
    }

    @Test
    fun regrant_shows_the_device_code_then_re_reads_the_matrix_once_authorized() = runTest {
        val beforeMatrix = TwitchScopeDiagnostics(
            requirements = listOf(TwitchScopeRequirement(scope = "channel:read:polls", feature = "polls", granted = false)),
        )
        val afterMatrix = TwitchScopeDiagnostics(
            requirements = listOf(TwitchScopeRequirement(scope = "channel:read:polls", feature = "polls", granted = true)),
        )
        val diagnostics =
            FakeDiagnosticsApi(
                scopes = ApiResult.Ok(beforeMatrix),
                regrant = ScopeRegrantStart(
                    deviceCode = "device-1",
                    userCode = "ABCD-EFGH",
                    verificationUri = "https://twitch.tv/activate",
                    interval = 0,
                    expiresIn = 60,
                    requestedScopes = listOf("channel:read:polls"),
                ),
            )
        val authApi = FakeAuthApi(pollStatuses = listOf("pending", "authorized"))
        val controller = PermissionsController(diagnostics, authApi)
        controller.load() // first read: the pre-approval matrix.
        diagnostics.setScopesAfter(afterMatrix) // the backend's state once the operator approves.

        controller.regrant()

        // The real device poll was actually called with the device code the backend returned.
        assertEquals("device-1", authApi.polledDeviceCode)

        // Approval reconciles server-side, so the controller re-reads — the matrix reflects the NEW granted
        // state, and the transient re-grant panel is gone.
        val state: PermissionsState = controller.state.value
        assertTrue(state is PermissionsState.Ready)
        val ready: PermissionsState.Ready = state as PermissionsState.Ready
        assertNull(ready.regrant)
        assertTrue(ready.matrix.requirements.first().granted)
    }

    @Test
    fun regrant_surfaces_the_backends_failure_reason_without_a_silent_no_op() = runTest {
        val diagnostics =
            FakeDiagnosticsApi(
                scopes = ApiResult.Ok(TwitchScopeDiagnostics()),
                regrantFailure = ApiError(409, "NO_MISSING_SCOPES", "Nothing to grant."),
            )
        val controller = PermissionsController(diagnostics, FakeAuthApi())
        controller.load()

        controller.regrant()

        val state: PermissionsState = controller.state.value
        assertTrue(state is PermissionsState.Ready)
        val ready: PermissionsState.Ready = state as PermissionsState.Ready
        assertEquals("Nothing to grant.", ready.regrantError)
        assertNull(ready.regrant)
    }
}

private class FakeDiagnosticsApi(
    private val scopes: ApiResult<TwitchScopeDiagnostics>,
    private val regrant: ScopeRegrantStart? = null,
    private val regrantFailure: ApiError? = null,
) : TwitchDiagnosticsApi {
    private var scopesAfter: ApiResult<TwitchScopeDiagnostics>? = null

    /** Models the backend's real state changing between the re-grant START and the post-approval re-read. */
    fun setScopesAfter(matrix: TwitchScopeDiagnostics) {
        scopesAfter = ApiResult.Ok(matrix)
    }

    override suspend fun scopeDiagnostics(): ApiResult<TwitchScopeDiagnostics> = scopesAfter ?: scopes

    override suspend fun missingScopes(): ApiResult<MissingScopes> = ApiResult.Ok(MissingScopes())

    override suspend fun startRegrant(): ApiResult<ScopeRegrantStart> =
        if (regrant != null) ApiResult.Ok(regrant)
        else ApiResult.Failure(regrantFailure ?: ApiError(500, "ERR", "no regrant configured"))

    override suspend fun subscriptions(channelId: String): ApiResult<List<EventSubSubscription>> =
        ApiResult.Ok(emptyList())

    override suspend fun reconcile(channelId: String): ApiResult<EventSubReconcileReport> = error("stub")
}

private class FakeAuthApi(private val pollStatuses: List<String> = emptyList()) : AuthApi {
    var polledDeviceCode: String? = null
        private set
    private var pollIndex: Int = 0

    override suspend fun providers(): ApiResult<List<LoginProvider>> = ApiResult.Ok(emptyList())

    override suspend fun me(): ApiResult<CurrentUser> = ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))

    override suspend fun startDeviceLogin(provider: String): ApiResult<DeviceCodeStart> =
        ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))

    override suspend fun pollDeviceLogin(provider: String, deviceCode: String): ApiResult<DeviceLoginPoll> {
        polledDeviceCode = deviceCode
        val status: String = pollStatuses.getOrElse(pollIndex) { "pending" }
        pollIndex++
        val auth: AuthPayload? =
            if (status == "authorized") AuthPayload(accessToken = "a", refreshToken = "r", expiresIn = 3600L)
            else null
        return ApiResult.Ok(DeviceLoginPoll(status = status, auth = auth))
    }

    override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> =
        ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))

    override suspend fun logout(): ApiResult<Unit> = ApiResult.Ok(Unit)
}
