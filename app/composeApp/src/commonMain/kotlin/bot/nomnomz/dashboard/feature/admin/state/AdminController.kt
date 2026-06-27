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
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PlatformEvent
import kotlinx.coroutines.flow.MutableStateFlow
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
)

class AdminController(private val api: AdminApi) {
    private val _state: MutableStateFlow<AdminState> = MutableStateFlow(AdminState())
    val state: StateFlow<AdminState> = _state.asStateFlow()

    suspend fun load() {
        _state.value = _state.value.copy(isLoading = true, error = null)

        val statsResult = api.getStats()
        val channelsResult = api.getChannels()
        val usersResult = api.getUsers()
        val systemResult = api.getSystem()
        val healthResult = api.getHealth()
        val eventsResult = api.getEvents()
        val flagsResult = api.getFeatureFlags()
        val invitesResult = api.getInviteCodes()

        _state.value = AdminState(
            stats = (statsResult as? ApiResult.Ok)?.value,
            channels = (channelsResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            users = (usersResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            system = (systemResult as? ApiResult.Ok)?.value,
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
}
