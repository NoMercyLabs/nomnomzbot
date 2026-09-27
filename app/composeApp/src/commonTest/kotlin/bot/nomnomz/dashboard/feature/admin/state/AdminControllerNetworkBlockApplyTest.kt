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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CrossTenantAbuseSignal
import bot.nomnomz.dashboard.core.network.NetworkBlock
import bot.nomnomz.dashboard.core.network.NetworkBlockPreview
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.TrustSafetyApi
import bot.nomnomz.dashboard.core.network.TrustSafetyReviewItem
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * A network block bans the target on every tenant it touches, so a second confirm while the first apply is
 * still in flight must not send a second apply: the operator would otherwise action every tenant twice.
 */
class AdminControllerNetworkBlockApplyTest {

    @Test
    fun a_second_apply_while_the_first_is_in_flight_sends_nothing() = runTest {
        val trustSafety = GatedApplyTrustSafetyApi()
        val controller = AdminController(
            api = PagedOpsFakeAdminApi(),
            iamApi = PagedFakePlatformIamApi(),
            platformAdminApi = PagedFakePlatformAdminApi(),
            trustSafetyApi = trustSafety,
        )
        controller.setTrustSafetyJustification("ticket 4411: coordinated hate raid")
        controller.setNetworkBlockTargetTwitchUserId("555")
        controller.previewNetworkBlock()
        controller.requestApplyNetworkBlock()

        val first: Job = launch { controller.applyNetworkBlock() }
        runCurrent()
        assertTrue(controller.state.value.networkBlockApplyInFlight)

        controller.applyNetworkBlock()
        assertEquals(1, trustSafety.applyCalls)

        trustSafety.applyGate.complete(ApiResult.Ok(appliedBlock()))
        first.join()

        assertEquals(1, trustSafety.applyCalls)
        assertFalse(controller.state.value.networkBlockApplyInFlight)
        assertFalse(controller.state.value.networkBlockApplyConfirmOpen)
        assertNull(controller.state.value.networkBlockPreview)
        assertEquals(listOf("block-1"), controller.state.value.networkBlocks.map { it.id })
    }
}

private fun appliedBlock(): NetworkBlock =
    NetworkBlock(
        id = "block-1",
        targetUserId = "user-555",
        targetTwitchUserId = "555",
        justification = "ticket 4411: coordinated hate raid",
        appliedByPrincipalId = "principal-1",
        appliedAt = "2026-09-27T12:00:00Z",
        tenantCount = 3,
        channelCount = 3,
        status = "active",
    )

private class GatedApplyTrustSafetyApi : TrustSafetyApi {
    val applyGate: CompletableDeferred<ApiResult<NetworkBlock>> = CompletableDeferred()
    var applyCalls: Int = 0
        private set

    override suspend fun getCrossTenantSignals(justification: String): ApiResult<List<CrossTenantAbuseSignal>> =
        ApiResult.Ok(emptyList())

    override suspend fun getReviewQueue(
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<TrustSafetyReviewItem>> = ApiResult.Ok(PaginatedEnvelope())

    override suspend fun confirm(detectionId: String, justification: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun overturn(detectionId: String, justification: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun previewNetworkBlock(
        targetTwitchUserId: String,
        justification: String,
    ): ApiResult<NetworkBlockPreview> =
        ApiResult.Ok(
            NetworkBlockPreview(
                targetUserId = "user-$targetTwitchUserId",
                targetTwitchUserId = targetTwitchUserId,
                tenantCount = 3,
            ),
        )

    override suspend fun applyNetworkBlock(
        targetTwitchUserId: String,
        reason: String?,
        justification: String,
        confirmedTenantCount: Int,
    ): ApiResult<NetworkBlock> {
        applyCalls += 1
        return applyGate.await()
    }

    override suspend fun listNetworkBlocks(justification: String): ApiResult<List<NetworkBlock>> =
        if (applyGate.isCompleted) ApiResult.Ok(listOf(appliedBlock())) else ApiResult.Ok(emptyList())

    override suspend fun liftNetworkBlock(blockId: String, justification: String): ApiResult<NetworkBlock> =
        ApiResult.Ok(appliedBlock())
}
