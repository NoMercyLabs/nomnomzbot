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

// The typed supporters facade — the channel's monetization connections + the recorded supporter-event feed the
// Supporters page renders (supporter-events.md §5, item 13 slice 13a). Real data only: the backend lists the
// broadcaster's stored connections and events (no fabricated rows). The state holder depends on this interface
// and fakes it in tests without HTTP.
//
// Like the giveaways / pick-lists routes, the supporters controller resolves the tenant (channel) from the
// request, so the routes carry no `{channelId}` — every call is "my own channel". A connection is addressed by
// its opaque [sourceKey] (a lowercase provider slug, e.g. `kofi`), treated as a string end-to-end; an event by
// its opaque ULID [SupporterEvent.id], never parsed.
//
// Truthful contract (supporter-events.md §5): a connection is an ENFORCED enable-toggle — ingest is default-deny
// and only fires when an enabled connection exists — not a cosmetic control. Two Gate-2 floors back these routes:
// reading (connections + events) floors at Moderator (`supporters:read`); connect / disconnect are Broadcaster-
// only and Critical (`supporters:config:write`), so the page disables those controls below the floor.
//
// Backend routes (SupportersController):
//   GET    /api/v1/supporters/connections                →  StatusResponseDto<IReadOnlyList<SupporterConnectionDto>>
//   PUT    /api/v1/supporters/connections                →  StatusResponseDto<SupporterConnectionDto>
//   DELETE /api/v1/supporters/connections/{sourceKey}    →  StatusResponseDto<object>
//   GET    /api/v1/supporters/events?page=&take=&kind=&sourceKey= →  PaginatedResponse<SupporterEventDto>
interface SupportersApi {
    /** The broadcaster's supporter connections (one row per configured provider; empty when none). */
    suspend fun connections(): ApiResult<List<SupporterConnection>>

    /**
     * Create/update a connection (backend PUT upsert, keyed by [UpsertSupporterConnectionBody.sourceKey]). For a
     * webhook provider, pass the provider's verification secret as [UpsertSupporterConnectionBody.authSecret]:
     * the backend auto-provisions the inbound ingest endpoint from it and returns its URL as
     * [SupporterConnection.endpointUrl]. Re-sending a secret rotates that endpoint in place.
     */
    suspend fun upsertConnection(body: UpsertSupporterConnectionBody): ApiResult<Unit>

    /** Remove a connection, addressed by its [sourceKey] — stops ingest for that provider (backend DELETE). */
    suspend fun deleteConnection(sourceKey: String): ApiResult<Unit>

    /**
     * A page of recorded supporter events, newest first, optionally filtered by [kind] / [sourceKey] (null = no
     * filter). Paging uses [take] (the backend `PageRequestDto` reads `Take`, NOT `pageSize`).
     */
    suspend fun events(
        page: Int,
        take: Int,
        kind: String? = null,
        sourceKey: String? = null,
    ): ApiResult<SupporterEventsPage>
}

class RestSupportersApi(private val client: ApiClient) : SupportersApi {
    // The list comes back as a `StatusResponseDto<IReadOnlyList<…>>` (a `data: T` envelope whose payload IS the
    // list), so it is read with getEnvelope (unwraps `data`), not the flat PaginatedResponse getDirect path.
    override suspend fun connections(): ApiResult<List<SupporterConnection>> =
        client.getEnvelope("api/v1/supporters/connections")

    // The upsert response is a `StatusResponseDto<SupporterConnectionDto>`, but the controller re-lists after
    // every write, so the body is irrelevant here — any 2xx is success (the reload observes the real consequence).
    override suspend fun upsertConnection(body: UpsertSupporterConnectionBody): ApiResult<Unit> =
        client.putUnit("api/v1/supporters/connections", body)

    override suspend fun deleteConnection(sourceKey: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/supporters/connections/$sourceKey")

    override suspend fun events(
        page: Int,
        take: Int,
        kind: String?,
        sourceKey: String?,
    ): ApiResult<SupporterEventsPage> {
        // The events feed is a PaginatedResponse (`{ data: [...], hasMore }`), read with getDirect (whole-body
        // deserialize) — `take` is the backend's page-size query param. Blank filters are omitted from the query.
        val kindFilter: String = if (!kind.isNullOrBlank()) "&kind=$kind" else ""
        val sourceFilter: String = if (!sourceKey.isNullOrBlank()) "&sourceKey=$sourceKey" else ""
        return client.getDirect("api/v1/supporters/events?page=$page&take=$take$kindFilter$sourceFilter")
    }
}

/**
 * The create/update request body (backend `UpsertSupporterConnectionRequest`). camelCase JSON. [sourceKey] and
 * [connectionMode] identify the provider + how it ingests; [isEnabled] is the enforced enable-toggle. [authSecret]
 * is the provider's verification secret — for a webhook provider the backend auto-provisions the inbound ingest
 * endpoint from it (one-step connect) and returns its URL on the connection; null leaves any existing secret
 * untouched. [integrationConnectionId] links a socket/OAuth provider. `explicitNulls = false` on the shared Json
 * omits the null optionals from the body.
 */
@Serializable
data class UpsertSupporterConnectionBody(
    val sourceKey: String,
    val connectionMode: String,
    val authSecret: String? = null,
    val integrationConnectionId: String? = null,
    val isEnabled: Boolean,
)

/**
 * A supporter connection's public shape (backend `SupporterConnectionDto`) — the secret is never returned, only
 * [hasSecret]. [sourceKey] is the provider slug (one of [SupporterSourceKey]); [connectionMode] one of
 * [SupporterConnectionMode]; [status] one of [SupporterConnectionStatus]. [lastEventAt] is the backend's ISO-8601
 * string (null until the first event lands), left as text (the page shows it verbatim).
 */
@Serializable
data class SupporterConnection(
    val sourceKey: String = "",
    val connectionMode: String = "",
    val hasSecret: Boolean = false,
    val isEnabled: Boolean = false,
    val status: String = "",
    val lastEventAt: String? = null,
    /**
     * For a webhook provider whose inbound endpoint was one-step provisioned from the connect flow, the ingest
     * URL to paste into the provider's webhook settings. Null for socket/ws/poll providers and for webhook
     * connections without a provisioned endpoint.
     */
    val endpointUrl: String? = null,
)

/**
 * One recorded supporter event (backend `SupporterEventDto`): the opaque ULID [id], its [sourceKey] + [kind] (one
 * of [SupporterEventKind]), the [supporterDisplayName], and the payment shape. [amountMinor] is the amount in
 * MINOR units (cents) — divide by 100 for display (see `formatSupporterAmount`); null when the provider sent no
 * amount. [tier] / [quantity] / [messageText] are provider-optional; [isRecurring] marks a repeat (e.g. a
 * membership renewal). [receivedAt] is the backend's ISO-8601 string, left as text.
 */
@Serializable
data class SupporterEvent(
    val id: String = "",
    val sourceKey: String = "",
    val kind: String = "",
    val supporterDisplayName: String = "",
    val amountMinor: Long? = null,
    val currency: String? = null,
    val tier: String? = null,
    val quantity: Int? = null,
    val messageText: String? = null,
    val isRecurring: Boolean = false,
    val receivedAt: String = "",
)

/**
 * One page of the supporter-events feed (backend `PaginatedResponse<SupporterEventDto>`) — a flat `{ data, hasMore
 * }` object rather than the single-value `data: T` envelope. [hasMore] drives the "Next" pager; unknown wire keys
 * (`nextPage`) are ignored by the shared Json.
 */
@Serializable
data class SupporterEventsPage(
    val data: List<SupporterEvent> = emptyList(),
    val hasMore: Boolean = false,
)

/** The supported supporter-source slugs ([SupporterConnection.sourceKey]). Ko-fi is the one live this slice. */
object SupporterSourceKey {
    const val Kofi: String = "kofi"
}

/** How a source ingests ([SupporterConnection.connectionMode]). Ko-fi ingests via inbound webhook. */
object SupporterConnectionMode {
    const val Webhook: String = "webhook"
}

/**
 * The [SupporterConnection.status] values the backend records: `idle` (connected, no event yet) → `active` (has
 * received at least one event). Kept beside the DTO as the single source the page compares against.
 */
object SupporterConnectionStatus {
    const val Idle: String = "idle"
    const val Active: String = "active"
}

/** The [SupporterEvent.kind] values (supporter-events.md §5 domain). Ko-fi emits tip / membership / merch. */
object SupporterEventKind {
    const val Tip: String = "tip"
    const val Membership: String = "membership"
    const val Merch: String = "merch"
    const val Charity: String = "charity"
}
