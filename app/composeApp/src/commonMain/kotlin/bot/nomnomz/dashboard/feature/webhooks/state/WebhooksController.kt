// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.webhooks.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateInboundBody
import bot.nomnomz.dashboard.core.network.CreateOutboundBody
import bot.nomnomz.dashboard.core.network.InboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhookCreated
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.WebhookTestResult
import bot.nomnomz.dashboard.core.network.WebhooksApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Webhooks page's state-holder: loads inbound + outbound webhook endpoints in parallel and drives all
// mutations — create, toggle, delete, rotate token/secret, test, re-enable. Reloads on every successful
// write so the page always reflects the backend's truth.
class WebhooksController(
    private val channelsApi: ChannelsApi,
    private val webhooksApi: WebhooksApi,
    private val pipelinesApi: PipelinesApi,
) {
    private val _state: MutableStateFlow<WebhooksState> = MutableStateFlow(WebhooksState.Loading)

    /** The page render state. */
    val state: StateFlow<WebhooksState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load inbound and outbound webhook endpoints. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is WebhooksState.Ready) _state.value = WebhooksState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = WebhooksState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val inbound: List<InboundWebhook> =
            when (val result: ApiResult<List<InboundWebhook>> = webhooksApi.listInbound(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = WebhooksState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val outbound: List<OutboundWebhook> =
            when (val result: ApiResult<List<OutboundWebhook>> = webhooksApi.listOutbound(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = WebhooksState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The channel's pipelines back the inbound "run a pipeline" routing picker. Best-effort: a failure just
        // leaves the picker empty (the form falls back to a pipeline-id field), never blocking the page.
        val pipelines: List<PipelineSummary> =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel.id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        _state.value = WebhooksState.Ready(inbound = inbound, outbound = outbound, pipelines = pipelines)
    }

    // ── Inbound ──────────────────────────────────────────────────────────────

    /**
     * Create an inbound endpoint with its routing: exactly one of [targetPipelineId] (run a pipeline on receive)
     * or [targetEventType] (trigger an event-response); both null leaves it inert until edited.
     */
    suspend fun createInbound(
        name: String,
        adapter: String,
        secret: String,
        targetPipelineId: String?,
        targetEventType: String?,
    ) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        val body =
            CreateInboundBody(
                name = name,
                adapter = adapter,
                verificationSecret = secret,
                targetPipelineId = targetPipelineId?.takeIf { it.isNotBlank() },
                targetEventType = targetEventType?.takeIf { it.isNotBlank() },
            )
        when (val result: ApiResult<InboundWebhook> = webhooksApi.createInbound(channel, body)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    suspend fun toggleInbound(endpointId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        afterUnit(webhooksApi.toggleInbound(channel, endpointId, enabled))
    }

    /**
     * Rotate the inbound token and return the new ingest URL to the caller so the UI can show it once.
     * Returns null on failure (error surfaced on state).
     */
    suspend fun rotateInboundToken(endpointId: String): String? {
        val channel: String = channelId ?: run { failWrite("No active channel."); return null }
        return when (val result: ApiResult<String> = webhooksApi.rotateInboundToken(channel, endpointId)) {
            is ApiResult.Ok -> { load(); result.value }
            is ApiResult.Failure -> { failWrite(result.error.message); null }
        }
    }

    suspend fun deleteInbound(endpointId: String) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        afterUnit(webhooksApi.deleteInbound(channel, endpointId))
    }

    // ── Outbound ─────────────────────────────────────────────────────────────

    /**
     * Create an outbound endpoint. Returns the signing secret (shown ONCE) on success, null on failure.
     */
    suspend fun createOutbound(name: String, fqdn: String, path: String?, events: List<String>): OutboundWebhookCreated? {
        val channel: String = channelId ?: run { failWrite("No active channel."); return null }
        return when (
            val result: ApiResult<OutboundWebhookCreated> =
                webhooksApi.createOutbound(channel, CreateOutboundBody(name, fqdn, path?.takeIf { it.isNotBlank() }, events))
        ) {
            is ApiResult.Ok -> { load(); result.value }
            is ApiResult.Failure -> { failWrite(result.error.message); null }
        }
    }

    suspend fun toggleOutbound(endpointId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        afterUnit(webhooksApi.toggleOutbound(channel, endpointId, enabled))
    }

    suspend fun reenableOutbound(endpointId: String) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        afterUnit(webhooksApi.reenableOutbound(channel, endpointId))
    }

    /**
     * Rotate the outbound signing secret. Returns the new plaintext secret (shown ONCE), null on failure.
     */
    suspend fun rotateOutboundSecret(endpointId: String): String? {
        val channel: String = channelId ?: run { failWrite("No active channel."); return null }
        return when (val result: ApiResult<String> = webhooksApi.rotateOutboundSecret(channel, endpointId)) {
            is ApiResult.Ok -> { load(); result.value }
            is ApiResult.Failure -> { failWrite(result.error.message); null }
        }
    }

    /** Test-deliver to an outbound endpoint. Returns the result DTO, null on call failure. */
    suspend fun testOutbound(endpointId: String): WebhookTestResult? {
        val channel: String = channelId ?: run { failWrite("No active channel."); return null }
        return when (val result: ApiResult<WebhookTestResult> = webhooksApi.testOutbound(channel, endpointId)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> { failWrite(result.error.message); null }
        }
    }

    suspend fun deleteOutbound(endpointId: String) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        afterUnit(webhooksApi.deleteOutbound(channel, endpointId))
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private suspend fun afterUnit(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: WebhooksState = _state.value
        _state.value =
            if (current is WebhooksState.Ready) current.copy(actionError = detail)
            else WebhooksState.Error(detail)
    }
}

/** The Webhooks page render state. */
sealed interface WebhooksState {
    data object Loading : WebhooksState

    data class Ready(
        val inbound: List<InboundWebhook>,
        val outbound: List<OutboundWebhook>,
        val pipelines: List<PipelineSummary> = emptyList(),
        val actionError: String? = null,
    ) : WebhooksState

    data class Error(val detail: String) : WebhooksState
}
