// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

// Platform content's `Kind = "pipeline"` authoring surface (S-ADMIN-2d, platform-admin.md §2.2 "no second,
// worse pipeline editor"): a system pipeline is authored through the SAME tree editor (`ChainEditor`) the
// tenant-side Pipelines page uses — the nested if/switch/loop/try authoring completed today
// (d4a7b397/1f658e0b/2110f3b8/dcf73ed1) — rather than a parallel implementation bolted onto this tab.
//
// `PipelinesController` is channel- and backend-scoped by design: it resolves a real tenant channel, lists
// real tenant pipelines, and persists a save through a real `PipelinesApi.update` call. Platform-content
// authoring has none of that — there is no channel yet, and "saving" here means feeding the drafted
// `payloadJson` back into the surrounding create/draft dialog, not writing a tenant row. So this file wires
// the SAME controller + SAME `ChainEditor` composable to a tiny set of in-memory API adapters that stand in
// for the channel/backend: reads return the in-progress draft, `update` captures the saved graph instead of
// calling a server, and every other API surface `ChainEditor`'s cross-feature pickers touch (webhooks,
// pick-lists, widgets, sound, TTS, …) degrades to its existing best-effort empty-list behavior — the exact
// path a tenant's own editor already takes when one of those optional sources fails to load.

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
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
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListPreview
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdateInboundBody
import bot.nomnomz.dashboard.core.network.UpdateOutboundBody
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.WebhooksApi
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesState
import bot.nomnomz.dashboard.feature.pipelines.ui.ChainEditor
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_content_pipeline_editor_loading
import org.jetbrains.compose.resources.stringResource

/** The synthetic in-authoring pipeline's id/name — never sent anywhere; it only addresses the one draft
 * this editor session holds. */
private const val DraftPipelineId: String = "platform-content-pipeline-draft"
private const val DraftPipelineName: String = "System pipeline draft"

/**
 * Authors a `Kind = "pipeline"` `payloadJson` through the real tenant-side tree editor. [payloadJson] is the
 * current draft (the same `{ "steps": [...] }` shape `UpdatePipelineDto.GraphJsonCache` / the backend's
 * `PipelineEntity` graph use — see `platform-admin.md` §3.2); every tree edit re-derives it via
 * [onPayloadJsonChange] so the surrounding create/draft dialog always holds the latest chain, without
 * requiring a separate "commit" step inside the tree editor itself.
 */
@Composable
internal fun PipelinePayloadEditor(payloadJson: String, onPayloadJsonChange: (String) -> Unit) {
    val scope = rememberCoroutineScope()
    val initialGraph: JsonElement? = remember(Unit) { parsePipelineGraphOrNull(payloadJson) }

    val controller = remember {
        PipelinesController(
            channelsApi = DraftChannelsApi,
            pipelinesApi = DraftPipelinesApi(initialGraph),
            webhooksApi = DraftWebhooksApi,
            pickListsApi = DraftPickListsApi,
        )
    }

    LaunchedEffect(controller) {
        controller.load()
        controller.openEditor(PipelineSummary(id = DraftPipelineId, name = DraftPipelineName))
    }

    val state by controller.state.collectAsState()
    val editing: PipelinesState.Editing? = state as? PipelinesState.Editing

    LaunchedEffect(editing?.steps) {
        editing?.let { onPayloadJsonChange(PipelineGraph(it.steps).toJson().toString()) }
    }

    if (editing != null) {
        ChainEditor(
            editing = editing,
            manage = ManageDecision.Allowed,
            controller = controller,
            scope = scope,
            templateHelpersApi = DraftTemplateHelpersApi,
            onOpenCodeScript = {},
        )
    } else {
        val tokens = LocalTokens.current
        val spacing = LocalSpacing.current
        val typography = LocalTypography.current
        Box(modifier = Modifier.fillMaxWidth().padding(spacing.s6), contentAlignment = Alignment.Center) {
            if (state is PipelinesState.Error) {
                Text(
                    text = stringResource(Res.string.admin_content_pipeline_editor_loading),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            } else {
                Spinner(color = tokens.primary)
            }
        }
    }
}

/** Parses a draft `payloadJson` string into the graph [JsonElement] `PipelineGraph.fromJson` expects; a
 * blank/invalid payload (a brand-new definition with no draft yet) yields null, which decodes to an empty
 * chain — the same "no steps yet" starting point [WidgetPayloadFields.Empty] gives the widget kind. */
private fun parsePipelineGraphOrNull(payloadJson: String): JsonElement? {
    if (payloadJson.isBlank()) return null
    return runCatching { Json.parseToJsonElement(payloadJson) }.getOrNull()
}

private val AdapterNotSupported: ApiResult<Nothing> =
    ApiResult.Failure(
        ApiError(
            status = 501,
            code = "NOT_SUPPORTED_IN_AUTHORING",
            message = "Not available while authoring a system pipeline.",
        ),
    )

/** Stands in for the resolved tenant channel `PipelinesController.load()` expects — there is no real
 * channel while authoring platform content, only the one synthetic draft pipeline. */
private object DraftChannelsApi : ChannelsApi {
    private val draftChannel = ChannelSummary(id = DraftPipelineId)

    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(draftChannel)

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(listOf(draftChannel))

    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = AdapterNotSupported

    override suspend fun join(channelId: String): ApiResult<Unit> = AdapterNotSupported

    override suspend fun leave(channelId: String): ApiResult<Unit> = AdapterNotSupported

    override suspend fun reset(channelId: String): ApiResult<Unit> = AdapterNotSupported

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = AdapterNotSupported

    override suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse> = AdapterNotSupported

    override suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart> = AdapterNotSupported

    override suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail> = AdapterNotSupported

    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = AdapterNotSupported
}

/** Holds the one draft pipeline being authored. `get` always returns the current graph; `update` (fired by
 * [ChainEditor]'s own "Save" control) simply captures the newly-saved graph — [PipelinePayloadEditor] already
 * re-derives `payloadJson` reactively from every tree edit, so this exists mainly so the editor's own save
 * round-trip (which re-fetches via `get` afterward) reflects exactly what it just wrote, matching the tenant
 * surface's behavior. Never reaches a real backend. */
private class DraftPipelinesApi(initialGraph: JsonElement?) : PipelinesApi {
    private var currentGraph: JsonElement? = initialGraph

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        ApiResult.Ok(listOf(PipelineSummary(id = DraftPipelineId, name = DraftPipelineName)))

    // No live backend registry while authoring — ChainEditor's palette falls back to
    // `PipelineCatalogue.fallbackPalette()`, the same locally-known core block set the tenant editor uses
    // when its own catalogue fetch fails.
    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> = AdapterNotSupported

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = DraftPipelineId, name = DraftPipelineName, graph = currentGraph))

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = AdapterNotSupported

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        AdapterNotSupported

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> {
        currentGraph = body.graph
        return ApiResult.Ok(Unit)
    }

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = AdapterNotSupported

    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        AdapterNotSupported

    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> =
        AdapterNotSupported
}

/** The tree editor's webhook-endpoint picker degrades to an empty list while authoring, exactly as it does
 * for a tenant whose webhook fetch fails — never blocks the editor from opening. */
private object DraftWebhooksApi : WebhooksApi {
    override suspend fun listInbound(channelId: String) = AdapterNotSupported
    override suspend fun createInbound(channelId: String, body: CreateInboundBody) = AdapterNotSupported
    override suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody) = AdapterNotSupported
    override suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean) = AdapterNotSupported
    override suspend fun rotateInboundToken(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun deleteInbound(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun inboundBlastRadius(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun outboundEventCatalogue(channelId: String) = AdapterNotSupported
    override suspend fun listOutbound(channelId: String) = ApiResult.Ok(emptyList<OutboundWebhook>())
    override suspend fun createOutbound(channelId: String, body: CreateOutboundBody) = AdapterNotSupported
    override suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody) = AdapterNotSupported
    override suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean) = AdapterNotSupported
    override suspend fun reenableOutbound(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun rotateOutboundSecret(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun testOutbound(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun outboundDeliveries(channelId: String, endpointId: String) = AdapterNotSupported
    override suspend fun retryOutboundDelivery(channelId: String, endpointId: String, deliveryId: Long): ApiResult<OutboundDelivery> =
        AdapterNotSupported
    override suspend fun deleteOutbound(channelId: String, endpointId: String) = AdapterNotSupported
}

/** The tree editor's pick-list picker degrades to an empty list while authoring. */
private object DraftPickListsApi : PickListsApi {
    override suspend fun list(): ApiResult<List<PickList>> = ApiResult.Ok(emptyList())
    override suspend fun get(id: String): ApiResult<PickList> = AdapterNotSupported
    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> = AdapterNotSupported
    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> = AdapterNotSupported
    override suspend fun delete(id: String): ApiResult<Unit> = AdapterNotSupported
    override suspend fun blastRadius(id: String) = AdapterNotSupported
    override suspend fun pick(id: String): ApiResult<PickListPreview> = AdapterNotSupported
}

/** The `run_code` step field's template-helpers link degrades to an empty list while authoring. */
private object DraftTemplateHelpersApi : TemplateHelpersApi {
    override suspend fun helpers(context: TemplateHelperContext, eventType: String?): ApiResult<List<TemplateHelperDto>> =
        ApiResult.Ok(emptyList())
}
