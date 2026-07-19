// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed webhooks facade — the channel's inbound and outbound webhook endpoints. Inbound endpoints
// receive events from external services (Streamlabs, StreamElements, Zapier, …) and dispatch them
// into the pipeline engine. Outbound endpoints POST signed notifications to external URLs when channel
// events fire.
//
// Backend routes (WebhooksController, all under /channels/{channelId}/webhooks):
//   GET    .../inbound                              →  PaginatedResponse<InboundWebhookEndpointDto>
//   POST   .../inbound                              →  StatusResponseDto<InboundWebhookEndpointDto>
//   PUT    .../inbound/{id}                         →  StatusResponseDto<InboundWebhookEndpointDto>
//   POST   .../inbound/{id}/rotate-token            →  StatusResponseDto<{ ingestUrl: string }>
//   DELETE .../inbound/{id}                         →  204 No Content
//   PUT    .../inbound/{id}                         →  StatusResponseDto<InboundWebhookEndpointDto>  (full edit)
//   GET    .../outbound/event-catalogue             →  StatusResponseDto<List<OutboundWebhookEventCatalogueEntry>>
//   GET    .../outbound                             →  PaginatedResponse<OutboundWebhookEndpointDto>
//   POST   .../outbound                             →  StatusResponseDto<OutboundWebhookEndpointCreatedDto>
//   PUT    .../outbound/{id}                        →  StatusResponseDto<OutboundWebhookEndpointDto>  (full edit)
//   POST   .../outbound/{id}/rotate-secret          →  StatusResponseDto<{ signingSecret: string }>
//   POST   .../outbound/{id}/reenable               →  204 No Content
//   POST   .../outbound/{id}/test                   →  StatusResponseDto<WebhookTestResultDto>
//   GET    .../outbound/{id}/deliveries             →  PaginatedResponse<OutboundWebhookDeliveryDto>
//   DELETE .../outbound/{id}                        →  204 No Content
interface WebhooksApi {
    suspend fun listInbound(channelId: String): ApiResult<List<InboundWebhook>>
    suspend fun createInbound(channelId: String, body: CreateInboundBody): ApiResult<InboundWebhook>
    suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody): ApiResult<InboundWebhook>
    suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit>
    suspend fun rotateInboundToken(channelId: String, endpointId: String): ApiResult<String>
    suspend fun deleteInbound(channelId: String, endpointId: String): ApiResult<Unit>

    suspend fun outboundEventCatalogue(channelId: String): ApiResult<List<OutboundEventCatalogueEntry>>
    suspend fun listOutbound(channelId: String): ApiResult<List<OutboundWebhook>>
    suspend fun createOutbound(channelId: String, body: CreateOutboundBody): ApiResult<OutboundWebhookCreated>
    suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody): ApiResult<OutboundWebhook>
    suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit>
    suspend fun reenableOutbound(channelId: String, endpointId: String): ApiResult<Unit>
    suspend fun rotateOutboundSecret(channelId: String, endpointId: String): ApiResult<String>
    suspend fun testOutbound(channelId: String, endpointId: String): ApiResult<WebhookTestResult>
    suspend fun outboundDeliveries(channelId: String, endpointId: String): ApiResult<List<OutboundDelivery>>
    suspend fun deleteOutbound(channelId: String, endpointId: String): ApiResult<Unit>
}

class RestWebhooksApi(private val client: ApiClient) : WebhooksApi {

    override suspend fun listInbound(channelId: String): ApiResult<List<InboundWebhook>> =
        when (
            val page: ApiResult<PaginatedEnvelope<InboundWebhook>> =
                client.getDirect("api/v1/channels/$channelId/webhooks/inbound?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun createInbound(channelId: String, body: CreateInboundBody): ApiResult<InboundWebhook> =
        client.postEnvelope("api/v1/channels/$channelId/webhooks/inbound", body)

    override suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody): ApiResult<InboundWebhook> =
        client.putEnvelope("api/v1/channels/$channelId/webhooks/inbound/$endpointId", body)

    override suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/webhooks/inbound/$endpointId", UpdateEnabledBody(enabled))

    override suspend fun rotateInboundToken(channelId: String, endpointId: String): ApiResult<String> =
        client.postEnvelope("api/v1/channels/$channelId/webhooks/inbound/$endpointId/rotate-token", Unit)

    override suspend fun deleteInbound(channelId: String, endpointId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/webhooks/inbound/$endpointId")

    override suspend fun outboundEventCatalogue(channelId: String): ApiResult<List<OutboundEventCatalogueEntry>> =
        client.getEnvelope("api/v1/channels/$channelId/webhooks/outbound/event-catalogue")

    override suspend fun listOutbound(channelId: String): ApiResult<List<OutboundWebhook>> =
        when (
            val page: ApiResult<PaginatedEnvelope<OutboundWebhook>> =
                client.getDirect("api/v1/channels/$channelId/webhooks/outbound?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun createOutbound(channelId: String, body: CreateOutboundBody): ApiResult<OutboundWebhookCreated> =
        client.postEnvelope("api/v1/channels/$channelId/webhooks/outbound", body)

    override suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody): ApiResult<OutboundWebhook> =
        client.putEnvelope("api/v1/channels/$channelId/webhooks/outbound/$endpointId", body)

    override suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/webhooks/outbound/$endpointId", UpdateEnabledBody(enabled))

    override suspend fun reenableOutbound(channelId: String, endpointId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/webhooks/outbound/$endpointId/reenable")

    override suspend fun rotateOutboundSecret(channelId: String, endpointId: String): ApiResult<String> =
        client.postEnvelope("api/v1/channels/$channelId/webhooks/outbound/$endpointId/rotate-secret", Unit)

    override suspend fun testOutbound(channelId: String, endpointId: String): ApiResult<WebhookTestResult> =
        client.postEnvelope("api/v1/channels/$channelId/webhooks/outbound/$endpointId/test", Unit)

    override suspend fun outboundDeliveries(channelId: String, endpointId: String): ApiResult<List<OutboundDelivery>> =
        when (
            val page: ApiResult<PaginatedEnvelope<OutboundDelivery>> =
                client.getDirect("api/v1/channels/$channelId/webhooks/outbound/$endpointId/deliveries?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun deleteOutbound(channelId: String, endpointId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/webhooks/outbound/$endpointId")
}

/** An inbound webhook endpoint (backend `InboundWebhookEndpointDto`). */
@Serializable
data class InboundWebhook(
    val id: String = "",
    val name: String = "",
    val adapter: String = "",
    val ingestUrl: String = "",
    val verificationSecretSet: Boolean = false,
    val targetPipelineId: String? = null,
    val targetEventType: String? = null,
    val isEnabled: Boolean = false,
    val lastReceivedAt: String? = null,
    val receiveCount: Long = 0,
    val createdAt: String = "",
    val updatedAt: String = "",
    /** Custom-adapter signing config — present only for the `Generic` adapter; lets the edit form pre-fill. */
    val genericConfig: GenericInboundConfig? = null,
)

/**
 * The generic / Standard-Webhooks custom-adapter signing config (backend `GenericInboundConfig`) — how a
 * Zapier / IFTTT / Make / Stream Deck / custom sender's requests are verified. [eventKindJsonPath] and
 * [providerEventIdJsonPath] are required (they locate the event kind + dedupe id in the JSON body); the
 * signature fields are optional (a sender that signs sets them, an unsigned one leaves them blank).
 */
@Serializable
data class GenericInboundConfig(
    val signatureHeaderName: String? = null,
    val signaturePrefix: String? = null,
    val signingStringTemplate: String? = null,
    val timestampHeaderName: String? = null,
    val sharedSecretBodyField: String? = null,
    val eventKindJsonPath: String = "",
    val providerEventIdJsonPath: String = "",
)

/** One subscribable business event in the outbound catalogue (backend `OutboundWebhookEventCatalogueEntry`). */
@Serializable
data class OutboundEventCatalogueEntry(
    val eventType: String = "",
    val label: String = "",
    val category: String = "",
)

/** One row of an outbound endpoint's delivery log (backend `OutboundWebhookDeliveryDto`). */
@Serializable
data class OutboundDelivery(
    val id: Long = 0,
    val endpointId: String = "",
    val eventType: String = "",
    val attempt: Int = 0,
    val status: String = "",
    val responseCode: Int? = null,
    val durationMs: Int? = null,
    val nextRetryAt: String? = null,
    val error: String? = null,
    val createdAt: String = "",
)

/** An outbound webhook endpoint (backend `OutboundWebhookEndpointDto`). */
@Serializable
data class OutboundWebhook(
    val id: String = "",
    val name: String = "",
    val fqdn: String = "",
    val path: String? = null,
    val subscribedEventTypes: List<String> = emptyList(),
    val isEnabled: Boolean = false,
    val consecutiveFailureCount: Int = 0,
    val disabledAt: String? = null,
    val disabledReason: String? = null,
    val lastDeliveryAt: String? = null,
    val lastSuccessAt: String? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/** Create-outbound result — carries the plaintext signing secret ONCE (never re-readable after this response). */
@Serializable
data class OutboundWebhookCreated(
    val endpoint: OutboundWebhook = OutboundWebhook(),
    val signingSecret: String = "",
)

/** Test-delivery result (backend `WebhookTestResultDto`). */
@Serializable
data class WebhookTestResult(
    val delivered: Boolean = false,
    val responseCode: Int? = null,
    val durationMs: Int = 0,
    val error: String? = null,
)

/**
 * Create-inbound body (backend `CreateInboundWebhookRequest`). [adapter] is the kind key (e.g. `"generic"`,
 * `"streamlabs"`). The routing is exactly one of: [targetPipelineId] (a verified receive runs that pipeline)
 * OR [targetEventType] (it triggers that event-response); both null leaves the endpoint inert. The payload
 * reaches the pipeline/event as `payload.*` template variables plus `webhook.provider` / `webhook.event_type`.
 */
@Serializable
data class CreateInboundBody(
    val name: String,
    val adapter: String,
    val verificationSecret: String,
    val targetPipelineId: String? = null,
    val targetEventType: String? = null,
    /** Required when [adapter] is `Generic`; ignored (and omitted) for the built-in provider adapters. */
    val genericConfig: GenericInboundConfig? = null,
    val isEnabled: Boolean = true,
)

/**
 * Full inbound update (backend `UpdateInboundWebhookRequest`). Every field is a null-omitted patch: a null
 * leaves that facet unchanged. [verificationSecret] rotates the sealed secret only when non-blank.
 */
@Serializable
data class UpdateInboundBody(
    val name: String? = null,
    val verificationSecret: String? = null,
    val targetPipelineId: String? = null,
    val targetEventType: String? = null,
    val genericConfig: GenericInboundConfig? = null,
    val isEnabled: Boolean? = null,
)

/** Create-outbound body (backend `CreateOutboundWebhookRequest`). */
@Serializable
data class CreateOutboundBody(
    val name: String,
    val fqdn: String,
    val path: String? = null,
    val subscribedEventTypes: List<String>,
    val isEnabled: Boolean = true,
)

/**
 * Full outbound update (backend `UpdateOutboundWebhookRequest`). Null-omitted patch: the FQDN/path are fixed
 * at create (egress-allowlist bound) so they are not editable here; the signing secret rotates via its own
 * endpoint. Name, the subscribed-event set, and the enabled flag are the editable facets.
 */
@Serializable
data class UpdateOutboundBody(
    val name: String? = null,
    val subscribedEventTypes: List<String>? = null,
    val bodyTemplate: String? = null,
    val customHeaders: Map<String, String>? = null,
    val isEnabled: Boolean? = null,
)

/** Partial update body — isEnabled only (the row switch). */
@Serializable
private data class UpdateEnabledBody(val isEnabled: Boolean)
