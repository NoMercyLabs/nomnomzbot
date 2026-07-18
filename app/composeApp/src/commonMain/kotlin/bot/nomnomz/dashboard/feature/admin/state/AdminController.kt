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

import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTenantDetail
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.realtime.AdminHubClient
import bot.nomnomz.dashboard.core.realtime.AdminHubEvent
import bot.nomnomz.dashboard.core.realtime.AdminLogEntry
import bot.nomnomz.dashboard.core.realtime.AdminRegistryUpdate
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

data class AdminState(
    val stats: AdminStats? = null,
    val channels: List<AdminChannel> = emptyList(),
    val users: List<AdminUser> = emptyList(),
    val system: AdminSystem? = null,
    val health: List<AdminServiceHealth> = emptyList(),
    val events: List<PlatformEvent> = emptyList(),
    val featureFlags: List<FeatureFlag> = emptyList(),
    val inviteCodes: List<InviteCode> = emptyList(),
    val isLoading: Boolean = false,
    val error: String? = null,
    // ── Live operator-hub feed (AdminHub) ──
    /** True once the AdminHub WebSocket handshake completes — drives the "live" indicator (not polling). */
    val hubLive: Boolean = false,
    /** Live channel-registry rows keyed by broadcasterId, most-recently-updated first. */
    val registry: List<AdminRegistryUpdate> = emptyList(),
    /** Live operator log lines, newest first (capped). */
    val logs: List<AdminLogEntry> = emptyList(),
    // ── IAM ──
    val roles: List<IamRole> = emptyList(),
    val principals: List<IamPrincipalSummary> = emptyList(),
    val iamLoading: Boolean = false,
    val iamError: String? = null,
    /** Effective permission keys for the last-inspected principal (principalId → keys). */
    val effectivePermissions: Map<String, List<String>> = emptyMap(),
    /** A service-account key returned exactly once on creation — shown, then cleared. */
    val issuedServiceAccountKey: String? = null,
    // ── Tenants ──
    val tenants: List<AdminTenant> = emptyList(),
    val tenantSearch: String = "",
    val tenantStatusFilter: String? = null,
    val tenantsLoading: Boolean = false,
    val tenantsError: String? = null,
    val selectedTenant: AdminTenantDetail? = null,
    // ── Audit ──
    val auditEntries: List<IamAuditEntry> = emptyList(),
    val auditOutcomeFilter: String? = null,
    val auditPermissionFilter: String = "",
    val auditLoading: Boolean = false,
    val auditError: String? = null,
    /** A surfaced write-action error (e.g. self-deactivation VALIDATION_FAILED). Cleared on next action. */
    val actionError: String? = null,
)

/**
 * The platform-admin panel's holder. Beyond the read-only stats/channels/users/flags/billing it drives the
 * Plane-C management surfaces — IAM (principals/roles), tenant operations (suspend/reinstate/detail), and the
 * audit log — and subscribes to the live [AdminHubClient] so the home status panel + channel registry move
 * without polling. Every write re-loads its slice so the UI reflects the persisted truth after reload.
 */
class AdminController(
    private val api: AdminApi,
    private val iamApi: PlatformIamApi,
    private val platformAdminApi: PlatformAdminApi,
    private val hubClient: AdminHubClient? = null,
    private val baseUrl: () -> String? = { null },
    private val accessToken: () -> String? = { null },
    private val refreshToken: (suspend () -> Boolean)? = null,
) {
    private val _state: MutableStateFlow<AdminState> = MutableStateFlow(AdminState())
    val state: StateFlow<AdminState> = _state.asStateFlow()

    /** The live operator-hub event stream, for the screen to collect via [subscribeToHub]. */
    val hubEvents: SharedFlow<AdminHubEvent>? = hubClient?.events

    suspend fun load() {
        _state.value = _state.value.copy(isLoading = true, error = null)

        // Connect the operator hub once — the handshake is gated on the caller's iam:manage grant, so a
        // non-privileged admin simply never establishes and the panel falls back to the REST snapshot.
        val url: String? = baseUrl()
        if (hubClient != null && url != null) {
            hubClient.connect(url, accessToken, refreshToken)
        }

        val statsResult = api.getStats()
        val channelsResult = api.getChannels()
        val usersResult = api.getUsers()
        val systemResult = api.getSystem()
        val healthResult = api.getHealth()
        val eventsResult = api.getEvents()
        val flagsResult = api.getFeatureFlags()
        val invitesResult = api.getInviteCodes()

        _state.value = _state.value.copy(
            stats = (statsResult as? ApiResult.Ok)?.value ?: _state.value.stats,
            channels = (channelsResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            users = (usersResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            system = (systemResult as? ApiResult.Ok)?.value ?: _state.value.system,
            health = (healthResult as? ApiResult.Ok)?.value ?: emptyList(),
            events = (eventsResult as? ApiResult.Ok)?.value ?: emptyList(),
            featureFlags = (flagsResult as? ApiResult.Ok)?.value ?: emptyList(),
            inviteCodes = (invitesResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            isLoading = false,
            error = listOf(statsResult, channelsResult, usersResult, systemResult)
                .filterIsInstance<ApiResult.Failure>()
                .firstOrNull()
                ?.error
                ?.message,
        )
    }

    /**
     * Fold live operator-hub pushes into state: the 15 s system heartbeat overwrites the health/stats panel,
     * a registry update lands in the live channel-registry list, and a log push prepends the operator log.
     */
    suspend fun subscribeToHub(events: SharedFlow<AdminHubEvent>) {
        events.collect { evt ->
            val current: AdminState = _state.value
            when (evt) {
                is AdminHubEvent.SystemStatus ->
                    _state.value = current.copy(
                        hubLive = true,
                        system = evt.system ?: current.system,
                        stats = evt.stats ?: current.stats,
                    )
                is AdminHubEvent.RegistryUpdate -> {
                    val without: List<AdminRegistryUpdate> =
                        current.registry.filterNot { it.broadcasterId == evt.update.broadcasterId }
                    _state.value = current.copy(
                        hubLive = true,
                        registry = (listOf(evt.update) + without).take(REGISTRY_CAP),
                    )
                }
                is AdminHubEvent.Log ->
                    _state.value = current.copy(
                        hubLive = true,
                        logs = (listOf(evt.entry) + current.logs).take(LOG_CAP),
                    )
                is AdminHubEvent.Unknown -> Unit
            }
        }
    }

    // ── Feature flags & billing (unchanged read-then-reload actions) ──────────

    suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest) {
        api.setFeatureFlag(body)
        load()
    }

    suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest) {
        api.setFeatureFlagOverride(flagKey, broadcasterId, body)
        load()
    }

    suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String) {
        api.deleteFeatureFlagOverride(flagKey, broadcasterId)
        load()
    }

    suspend fun createInviteCode(body: AdminCreateInviteCodeRequest) {
        api.createInviteCode(body)
        load()
    }

    suspend fun revokeInviteCode(inviteCodeId: String) {
        api.revokeInviteCode(inviteCodeId)
        load()
    }

    suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest) {
        api.grantTier(broadcasterId, body)
        load()
    }

    suspend fun grantFounderBadge(broadcasterId: String) {
        api.grantFounderBadge(broadcasterId)
        load()
    }

    // ── IAM ───────────────────────────────────────────────────────────────────

    suspend fun loadIam() {
        _state.value = _state.value.copy(iamLoading = true, iamError = null)
        val rolesResult = iamApi.listRoles()
        val principalsResult = iamApi.listPrincipals()
        _state.value = _state.value.copy(
            roles = (rolesResult as? ApiResult.Ok)?.value ?: emptyList(),
            principals = (principalsResult as? ApiResult.Ok)?.value ?: emptyList(),
            iamLoading = false,
            iamError = listOf(rolesResult, principalsResult)
                .filterIsInstance<ApiResult.Failure>()
                .firstOrNull()
                ?.error
                ?.message,
        )
    }

    suspend fun loadEffectivePermissions(principalId: String) {
        when (val result: ApiResult<List<String>> = iamApi.effectivePermissions(principalId)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    effectivePermissions = _state.value.effectivePermissions + (principalId to result.value),
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(iamError = result.error.message)
        }
    }

    /** Promote a user (employee) — [userId] set, [principalType] 0. The panel shows no key. */
    suspend fun promoteUser(userId: String, displayName: String, roleIds: List<String>) {
        _state.value = _state.value.copy(actionError = null)
        val body = CreatePrincipalBody(principalType = 0, userId = userId, displayName = displayName, roleIds = roleIds)
        when (val result: ApiResult<IamPrincipal> = iamApi.createPrincipal(body)) {
            is ApiResult.Ok -> loadIam()
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    /** Create a service account — [principalType] 1. Its key is returned ONCE; stash it for the show-once dialog. */
    suspend fun createServiceAccount(serviceAccountName: String, roleIds: List<String>) {
        _state.value = _state.value.copy(actionError = null)
        val body = CreatePrincipalBody(principalType = 1, displayName = serviceAccountName, roleIds = roleIds, serviceAccountName = serviceAccountName)
        when (val result: ApiResult<IamPrincipal> = iamApi.createPrincipal(body)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(issuedServiceAccountKey = result.value.serviceAccountKey)
                loadIam()
            }
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    /** Clears the show-once service-account key once the operator has copied/dismissed it. */
    fun dismissIssuedKey() {
        _state.value = _state.value.copy(issuedServiceAccountKey = null)
    }

    fun clearActionError() {
        _state.value = _state.value.copy(actionError = null)
    }

    suspend fun deactivatePrincipal(principalId: String, reason: String?) {
        _state.value = _state.value.copy(actionError = null)
        when (val result: ApiResult<Unit> = iamApi.deactivatePrincipal(principalId, reason)) {
            is ApiResult.Ok -> loadIam()
            // Self-deactivation returns VALIDATION_FAILED — surface the message, don't swallow it.
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    suspend fun reactivatePrincipal(principalId: String) {
        _state.value = _state.value.copy(actionError = null)
        when (val result: ApiResult<Unit> = iamApi.reactivatePrincipal(principalId)) {
            is ApiResult.Ok -> loadIam()
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    suspend fun assignRole(principalId: String, roleId: String, reason: String?) {
        _state.value = _state.value.copy(actionError = null)
        val body = AssignRoleBody(principalId = principalId, roleId = roleId, reason = reason)
        when (val result = iamApi.assignRole(body)) {
            is ApiResult.Ok -> loadIam()
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    suspend fun revokeAssignment(assignmentId: String, reason: String?) {
        _state.value = _state.value.copy(actionError = null)
        when (val result: ApiResult<Unit> = iamApi.revokeAssignment(assignmentId, reason)) {
            is ApiResult.Ok -> loadIam()
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    // ── Tenants ─────────────────────────────────────────────────────────────

    suspend fun loadTenants(search: String? = null, status: String? = null) {
        val effectiveSearch: String = search ?: _state.value.tenantSearch
        val effectiveStatus: String? = status ?: _state.value.tenantStatusFilter
        _state.value = _state.value.copy(
            tenantsLoading = true,
            tenantsError = null,
            tenantSearch = effectiveSearch,
            tenantStatusFilter = effectiveStatus,
        )
        when (val result = platformAdminApi.listTenants(search = effectiveSearch, status = effectiveStatus)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(tenants = result.value.data, tenantsLoading = false)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(tenantsLoading = false, tenantsError = result.error.message)
        }
    }

    suspend fun openTenant(broadcasterId: String) {
        when (val result: ApiResult<AdminTenantDetail> = platformAdminApi.getTenant(broadcasterId)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(selectedTenant = result.value)
            is ApiResult.Failure -> _state.value = _state.value.copy(tenantsError = result.error.message)
        }
    }

    fun closeTenant() {
        _state.value = _state.value.copy(selectedTenant = null)
    }

    suspend fun suspendTenant(broadcasterId: String, newStatus: String, reason: String) {
        _state.value = _state.value.copy(actionError = null)
        when (val result: ApiResult<Unit> = platformAdminApi.suspendTenant(broadcasterId, SuspendTenantBody(newStatus, reason))) {
            is ApiResult.Ok -> {
                loadTenants()
                if (_state.value.selectedTenant?.id == broadcasterId) openTenant(broadcasterId)
            }
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    suspend fun reinstateTenant(broadcasterId: String, justification: String) {
        _state.value = _state.value.copy(actionError = null)
        when (val result: ApiResult<Unit> = platformAdminApi.reinstateTenant(broadcasterId, ReinstateTenantBody(justification))) {
            is ApiResult.Ok -> {
                loadTenants()
                if (_state.value.selectedTenant?.id == broadcasterId) openTenant(broadcasterId)
            }
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    suspend fun beginTenantAccess(broadcasterId: String, justification: String, breakGlass: Boolean) {
        _state.value = _state.value.copy(actionError = null)
        val body = BeginTenantAccessBody(justification = justification, breakGlass = breakGlass)
        when (val result = platformAdminApi.beginAccess(broadcasterId, body)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> _state.value = _state.value.copy(actionError = result.error.message)
        }
    }

    // ── Audit ─────────────────────────────────────────────────────────────────

    suspend fun loadAudit(outcome: String? = null, permission: String? = null) {
        val effectiveOutcome: String? = outcome ?: _state.value.auditOutcomeFilter
        val effectivePermission: String = permission ?: _state.value.auditPermissionFilter
        _state.value = _state.value.copy(
            auditLoading = true,
            auditError = null,
            auditOutcomeFilter = effectiveOutcome,
            auditPermissionFilter = effectivePermission,
        )
        when (val result = platformAdminApi.searchAudit(permission = effectivePermission, outcome = effectiveOutcome)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(auditEntries = result.value.data, auditLoading = false)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(auditLoading = false, auditError = result.error.message)
        }
    }

    private companion object {
        const val REGISTRY_CAP: Int = 50
        const val LOG_CAP: Int = 50
    }
}
