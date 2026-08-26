// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ImpersonationInfo
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminTenantDetail
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.IamRoleAssignment
import bot.nomnomz.dashboard.core.network.ImpersonateUserRequest
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.LoginProvider
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.network.TenantAccessGrant
import bot.nomnomz.dashboard.core.network.UserSearchResult
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves the S089b impersonation flow end to end over the state holder (no Compose, no network):
//   1. A blank justification never reaches the network — the endpoint is called with zero requests.
//   2. A real justification opens the support session (carrying it) and mints the act-as token scoped to
//      the grant it returns — both calls, in order, with the right arguments.
//   3. A NoOpenSupportSession refusal is classified onto AdminState.impersonationRefusal, not left as a raw
//      string the UI would otherwise render as a generic toast.
//   4. Stop-impersonating calls the end endpoint with the held grant id and restores the operator session.
@OptIn(kotlin.time.ExperimentalTime::class)
class AdminControllerImpersonationTest {

    private val profile = ConnectionProfile(
        id = "p1",
        displayName = "Self-host",
        baseUrl = "http://localhost:5080",
        source = ProfileSource.Manual,
    )
    private val operatorTokens = SessionTokens(accessToken = "operator-jwt", refreshToken = null, expiresAt = null)

    private fun newSessionStore(): SessionStore {
        val store = SessionStore(FakeTokenVault(), FakeProfileVault(), FakeChannelVault())
        return store
    }

    private fun newController(
        api: FakeAdminApi,
        platformAdminApi: FakePlatformAdminApi,
        authApi: FakeAuthApi,
        sessionStore: SessionStore,
    ): AdminController =
        AdminController(
            api = api,
            iamApi = FakePlatformIamApi(),
            platformAdminApi = platformAdminApi,
            sessionStore = sessionStore,
            authApi = authApi,
        )

    @Test
    fun blank_justification_never_calls_the_network() = runTest {
        val api = FakeAdminApi()
        val platformAdminApi = FakePlatformAdminApi()
        val sessionStore = newSessionStore()
        sessionStore.connect(profile, operatorTokens)
        val controller = newController(api, platformAdminApi, FakeAuthApi(), sessionStore)

        controller.impersonateTenantOwner(
            broadcasterId = "chan-1",
            subjectUserId = "user-1",
            subjectDisplayName = "Some Streamer",
            justification = "   ",
        )

        assertEquals(0, platformAdminApi.beginAccessCalls.size)
        assertEquals(0, api.impersonateCalls.size)
        assertNull(sessionStore.impersonating.value)
    }

    @Test
    fun a_real_justification_opens_the_support_session_then_mints_the_token_scoped_to_its_grant() = runTest {
        val api = FakeAdminApi(
            impersonateResult = ApiResult.Ok(
                ImpersonationTokenDto(
                    accessToken = "target-jwt",
                    expiresAt = "2030-01-01T00:00:00Z",
                    sessionId = "session-1",
                    user = UserSearchResult(id = "user-1", displayName = "Some Streamer"),
                ),
            ),
        )
        val platformAdminApi = FakePlatformAdminApi(
            beginAccessResult = ApiResult.Ok(
                TenantAccessGrant(
                    id = "grant-1",
                    principalId = "operator-1",
                    targetBroadcasterId = "chan-1",
                    justification = "Investigating a billing ticket",
                    grantedAt = "2026-01-01T00:00:00Z",
                    expiresAt = "2030-01-01T00:00:00Z",
                ),
            ),
        )
        val sessionStore = newSessionStore()
        sessionStore.connect(profile, operatorTokens)
        val authApi = FakeAuthApi(
            meResult = ApiResult.Ok(
                CurrentUser(id = "user-1", username = "some_streamer", displayName = "Some Streamer"),
            ),
        )
        val controller = newController(api, platformAdminApi, authApi, sessionStore)

        controller.impersonateTenantOwner(
            broadcasterId = "chan-1",
            subjectUserId = "user-1",
            subjectDisplayName = "Some Streamer",
            justification = "Investigating a billing ticket",
        )

        // 1. The support session was opened for THIS tenant, carrying the justification.
        assertEquals(1, platformAdminApi.beginAccessCalls.size)
        val (broadcasterId, body) = platformAdminApi.beginAccessCalls.single()
        assertEquals("chan-1", broadcasterId)
        assertEquals("Investigating a billing ticket", body.justification)

        // 2. The impersonate call carries the grant id THAT session returned.
        assertEquals(1, api.impersonateCalls.size)
        val (subjectUserId, accessGrantId, capturedJustification) = api.impersonateCalls.single()
        assertEquals("user-1", subjectUserId)
        assertEquals("grant-1", accessGrantId)
        assertEquals("Investigating a billing ticket", capturedJustification)

        // 3. The session swapped onto the minted token and the "acting as" state carries the grant + expiry.
        assertEquals("target-jwt", sessionStore.accessToken())
        val info: ImpersonationInfo? = sessionStore.impersonating.value
        assertEquals("Some Streamer", info?.displayName)
        // Ending impersonation targets the MINTED SESSION (ImpersonationTokenDto.sessionId), not the tenant
        // access grant that authorized minting it — they are two different ids on the real contract.
        assertEquals("session-1", info?.accessGrantId)
        assertEquals(kotlinx.datetime.Instant.parse("2030-01-01T00:00:00Z"), info?.expiresAt)
        assertNull(controller.state.value.impersonationRefusal)
    }

    @Test
    fun no_open_support_session_refusal_is_classified_not_left_generic() = runTest {
        val api = FakeAdminApi()
        val platformAdminApi = FakePlatformAdminApi(
            beginAccessResult = ApiResult.Failure(
                ApiError(status = 409, code = "409", message = "No open support session for this tenant."),
            ),
        )
        val sessionStore = newSessionStore()
        sessionStore.connect(profile, operatorTokens)
        val controller = newController(api, platformAdminApi, FakeAuthApi(), sessionStore)

        controller.impersonateTenantOwner(
            broadcasterId = "chan-1",
            subjectUserId = "user-1",
            subjectDisplayName = "Some Streamer",
            justification = "Investigating",
        )

        assertEquals(ImpersonationRefusal.NoOpenSupportSession, controller.state.value.impersonationRefusal)
        // Refused before minting — the impersonate endpoint is never called.
        assertEquals(0, api.impersonateCalls.size)
        assertNull(sessionStore.impersonating.value)
    }

    @Test
    fun not_permitted_refusal_from_the_mint_call_is_also_classified() = runTest {
        val api = FakeAdminApi(
            impersonateResult = ApiResult.Failure(
                ApiError(status = 403, code = "403", message = "Not permitted."),
            ),
        )
        val platformAdminApi = FakePlatformAdminApi(
            beginAccessResult = ApiResult.Ok(
                TenantAccessGrant(
                    id = "grant-1",
                    principalId = "operator-1",
                    targetBroadcasterId = "chan-1",
                    justification = "Investigating",
                    grantedAt = "2026-01-01T00:00:00Z",
                ),
            ),
        )
        val sessionStore = newSessionStore()
        sessionStore.connect(profile, operatorTokens)
        val controller = newController(api, platformAdminApi, FakeAuthApi(), sessionStore)

        controller.impersonateTenantOwner(
            broadcasterId = "chan-1",
            subjectUserId = "user-1",
            subjectDisplayName = "Some Streamer",
            justification = "Investigating",
        )

        assertEquals(ImpersonationRefusal.NotPermitted, controller.state.value.impersonationRefusal)
        assertNull(sessionStore.impersonating.value)
    }

    @Test
    fun stop_impersonating_calls_the_end_endpoint_with_the_held_grant_and_restores_the_operator() = runTest {
        val api = FakeAdminApi()
        val sessionStore = newSessionStore()
        sessionStore.connect(profile, operatorTokens)
        sessionStore.beginImpersonation(
            targetAccessToken = "target-jwt",
            targetDisplayName = "Some Streamer",
            expiresAt = kotlinx.datetime.Instant.parse("2030-01-01T00:00:00Z"),
            accessGrantId = "grant-1",
        )
        val authApi = FakeAuthApi(
            meResult = ApiResult.Ok(CurrentUser(id = "operator-1", username = "operator", displayName = "Operator")),
        )
        val controller = newController(api, FakePlatformAdminApi(), authApi, sessionStore)

        controller.exitImpersonation()

        assertEquals(listOf("grant-1"), api.endImpersonationCalls)
        assertNull(sessionStore.impersonating.value)
        assertEquals("operator-jwt", sessionStore.accessToken())
    }

    @Test
    fun stop_impersonating_when_not_impersonating_is_a_no_op() = runTest {
        val api = FakeAdminApi()
        val sessionStore = newSessionStore()
        sessionStore.connect(profile, operatorTokens)
        val controller = newController(api, FakePlatformAdminApi(), FakeAuthApi(), sessionStore)

        controller.exitImpersonation()

        assertTrue(api.endImpersonationCalls.isEmpty())
        assertEquals("operator-jwt", sessionStore.accessToken())
    }
}

// ─── Fakes ─────────────────────────────────────────────────────────────────

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

private class FakeProfileVault : ActiveProfileStore {
    private var stored: ConnectionProfile? = null
    override suspend fun read(): ConnectionProfile? = stored
    override suspend fun write(profile: ConnectionProfile) {
        stored = profile
    }
    override suspend fun clear() {
        stored = null
    }
}

private class FakeChannelVault : ActiveChannelStore {
    private var stored: String? = null
    override suspend fun read(): String? = stored
    override suspend fun write(channelId: String) {
        stored = channelId
    }
    override suspend fun clear() {
        stored = null
    }
}

private class FakeAdminApi(
    private val impersonateResult: ApiResult<ImpersonationTokenDto> = ApiResult.Failure(
        ApiError(status = 500, code = null, message = "not stubbed"),
    ),
    private val endImpersonationResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : AdminApi {
    val impersonateCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val endImpersonationCalls: MutableList<String> = mutableListOf()

    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminChannel>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getUsers(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminUser>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Ok(AdminSystem("ok", emptyList(), "1.0", 0, 0.0))
    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> = ApiResult.Ok(emptyList())
    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> = ApiResult.Ok(emptyList())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(emptyList())
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        ApiResult.Ok(FeatureFlag(featureKey = body.key, isEnabled = body.isEnabledGlobally))
    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        ApiResult.Ok(InviteCode("id", "code", body.maxRedemptions, 0, body.grantsFoundersBadge))
    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> {
        impersonateCalls += Triple(subjectUserId, accessGrantId, justification)
        return impersonateResult
    }

    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> {
        endImpersonationCalls += accessGrantId
        return endImpersonationResult
    }
}

private class FakePlatformAdminApi(
    private val beginAccessResult: ApiResult<TenantAccessGrant> = ApiResult.Failure(
        ApiError(status = 500, code = null, message = "not stubbed"),
    ),
) : PlatformAdminApi {
    val beginAccessCalls: MutableList<Pair<String, BeginTenantAccessBody>> = mutableListOf()

    override suspend fun listTenants(search: String?, status: String?, isLive: Boolean?, page: Int, pageSize: Int) =
        ApiResult.Ok(PaginatedEnvelope(emptyList<bot.nomnomz.dashboard.core.network.AdminTenant>()))
    override suspend fun getTenant(broadcasterId: String): ApiResult<AdminTenantDetail> =
        ApiResult.Failure(ApiError(404, null, "not stubbed"))
    override suspend fun suspendTenant(broadcasterId: String, body: SuspendTenantBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reinstateTenant(broadcasterId: String, body: ReinstateTenantBody): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun beginAccess(broadcasterId: String, body: BeginTenantAccessBody): ApiResult<TenantAccessGrant> {
        beginAccessCalls += broadcasterId to body
        return beginAccessResult
    }

    override suspend fun endAccess(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun searchAudit(
        principalId: String?,
        targetBroadcasterId: String?,
        permission: String?,
        outcome: String?,
        from: String?,
        to: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<IamAuditEntry>> = ApiResult.Ok(PaginatedEnvelope(emptyList()))
}

private class FakePlatformIamApi : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> = ApiResult.Ok(emptyList())
    override suspend fun effectivePermissions(principalId: String, scopeChannelId: String?): ApiResult<List<String>> =
        ApiResult.Ok(emptyList())
    override suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal> =
        ApiResult.Ok(IamPrincipal(id = "p", name = body.displayName))
    override suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reactivatePrincipal(principalId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun assignRole(body: AssignRoleBody): ApiResult<IamRoleAssignment> =
        ApiResult.Ok(IamRoleAssignment(id = "a", principalId = body.principalId, roleId = body.roleId, roleName = "role", createdAt = "2026-01-01T00:00:00Z"))
    override suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeAuthApi(
    private val meResult: ApiResult<CurrentUser> = ApiResult.Failure(ApiError(500, null, "not stubbed")),
) : AuthApi {
    override suspend fun providers(): ApiResult<List<LoginProvider>> = ApiResult.Ok(emptyList())
    override suspend fun me(): ApiResult<CurrentUser> = meResult
    override suspend fun startDeviceLogin(provider: String): ApiResult<DeviceCodeStart> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun pollDeviceLogin(provider: String, deviceCode: String): ApiResult<DeviceLoginPoll> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun logout(): ApiResult<Unit> = ApiResult.Ok(Unit)
}
