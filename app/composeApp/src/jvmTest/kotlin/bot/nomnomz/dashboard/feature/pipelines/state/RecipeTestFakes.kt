// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelBotStatusDetail
import bot.nomnomz.dashboard.core.network.ChannelScopesResponse
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateInboundBody
import bot.nomnomz.dashboard.core.network.CreateOutboundBody
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OAuthStart
import bot.nomnomz.dashboard.core.network.OutboundDelivery
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListPreview
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdateInboundBody
import bot.nomnomz.dashboard.core.network.UpdateOutboundBody
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.WebhooksApi

private val NotImplementedForRecipeTest: ApiResult<Nothing> =
    ApiResult.Failure(ApiError(status = 501, code = "NOT_IMPLEMENTED", message = "not implemented in this test fake"))

/** A controller wired to a [RecordingPipelinesApiForRecipeTest] so a test can read exactly what was saved. */
internal fun recipeTestController(pipelines: RecordingPipelinesApiForRecipeTest): PipelinesController =
    PipelinesController(
        channelsApi = FakeChannelsApiForRecipeTest(ChannelSummary(id = "chan-1")),
        pipelinesApi = pipelines,
        webhooksApi = FakeWebhooksApiForRecipeTest(),
        pickListsApi = FakePickListsApiForRecipeTest(),
    )

/** Records every create body. */
internal class RecordingPipelinesApiForRecipeTest : PipelinesApi {
    val created: MutableList<CreatePipelineBody> = mutableListOf()

    /** What the server currently stores for pipeline `p1` (the editor tests open it); null = no such pipeline. */
    var stored: PipelineDetail? = null
    val updated: MutableList<UpdatePipelineBody> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    /** What each write answers; a test sets a failure to prove the dialog that fired it stays open. */
    var createResult: ApiResult<Unit> = ApiResult.Ok(Unit)
    var updateResult: ApiResult<Unit> = ApiResult.Ok(Unit)
    var deleteResult: ApiResult<Unit> = NotImplementedForRecipeTest

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        ApiResult.Ok(listOfNotNull(stored?.let { PipelineSummary(id = it.id, name = it.name) }))

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        stored?.let { ApiResult.Ok(it) } ?: NotImplementedForRecipeTest

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> {
        created += body
        return createResult
    }

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        NotImplementedForRecipeTest

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> {
        updated += body
        if (updateResult is ApiResult.Ok) {
            body.graph?.let { graph -> stored = stored?.copy(graph = graph) }
            body.name?.let { name -> stored = stored?.copy(name = name) }
        }
        return updateResult
    }

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> {
        deleted += id
        if (deleteResult is ApiResult.Ok) stored = null
        return deleteResult
    }

    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        NotImplementedForRecipeTest

    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> =
        NotImplementedForRecipeTest
}

private class FakeChannelsApiForRecipeTest(private val channel: ChannelSummary) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(channel)

    override suspend fun list(): ApiResult<List<ChannelSummary>> = NotImplementedForRecipeTest

    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = NotImplementedForRecipeTest

    override suspend fun join(channelId: String): ApiResult<Unit> = NotImplementedForRecipeTest

    override suspend fun leave(channelId: String): ApiResult<Unit> = NotImplementedForRecipeTest

    override suspend fun reset(channelId: String): ApiResult<Unit> = NotImplementedForRecipeTest

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = NotImplementedForRecipeTest

    override suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse> = NotImplementedForRecipeTest

    override suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart> = NotImplementedForRecipeTest

    override suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail> = NotImplementedForRecipeTest

    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = NotImplementedForRecipeTest
}

private class FakeWebhooksApiForRecipeTest : WebhooksApi {
    override suspend fun listInbound(channelId: String) = NotImplementedForRecipeTest
    override suspend fun createInbound(channelId: String, body: CreateInboundBody) = NotImplementedForRecipeTest
    override suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody) = NotImplementedForRecipeTest
    override suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean) = NotImplementedForRecipeTest
    override suspend fun rotateInboundToken(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun deleteInbound(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun inboundBlastRadius(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun outboundEventCatalogue(channelId: String) = NotImplementedForRecipeTest
    override suspend fun listOutbound(channelId: String) = NotImplementedForRecipeTest
    override suspend fun createOutbound(channelId: String, body: CreateOutboundBody) = NotImplementedForRecipeTest
    override suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody) = NotImplementedForRecipeTest
    override suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean) = NotImplementedForRecipeTest
    override suspend fun reenableOutbound(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun rotateOutboundSecret(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun testOutbound(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun outboundDeliveries(channelId: String, endpointId: String) = NotImplementedForRecipeTest
    override suspend fun retryOutboundDelivery(channelId: String, endpointId: String, deliveryId: Long): ApiResult<OutboundDelivery> =
        NotImplementedForRecipeTest
    override suspend fun deleteOutbound(channelId: String, endpointId: String) = NotImplementedForRecipeTest
}

private class FakePickListsApiForRecipeTest : PickListsApi {
    override suspend fun list(): ApiResult<List<PickList>> = NotImplementedForRecipeTest
    override suspend fun get(id: String): ApiResult<PickList> = NotImplementedForRecipeTest
    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> = NotImplementedForRecipeTest
    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> = NotImplementedForRecipeTest
    override suspend fun delete(id: String): ApiResult<Unit> = NotImplementedForRecipeTest
    override suspend fun blastRadius(id: String) = NotImplementedForRecipeTest
    override suspend fun pick(id: String): ApiResult<PickListPreview> = NotImplementedForRecipeTest
}
