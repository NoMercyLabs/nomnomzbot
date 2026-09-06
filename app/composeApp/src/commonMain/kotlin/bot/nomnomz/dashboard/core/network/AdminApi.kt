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

// Platform-admin REST client (GET /api/v1/admin/*, /api/v1/admin/billing/*, /api/v1/admin/feature-flags).
// All endpoints are gated on platform:admin (isAdmin == true on CurrentUser); callers must verify before
// showing the Admin area — the backend re-checks on every call.

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// ─── DTOs ────────────────────────────────────────────────────────────────────

@Serializable
data class AdminStats(
    val totalChannels: Int,
    val activeChannels: Int,
    val totalUsers: Int,
    val systemStatus: String,
    val botUptimeSeconds: Long,
    val eventsProcessedToday: Int,
)

@Serializable
data class AdminChannel(
    val id: String,
    val displayName: String,
    val login: String,
    val isLive: Boolean,
    val isActive: Boolean,
    val viewerCount: Int,
    val plan: String,
    val createdAt: String,
)

@Serializable
data class AdminUser(
    val id: String,
    val displayName: String,
    val login: String,
    val email: String? = null,
    val role: String,
    val channelCount: Int,
    val createdAt: String,
    val lastActive: String? = null,
)

@Serializable
data class AdminServiceHealth(
    val name: String,
    val status: String,
)

@Serializable
data class AdminSystem(
    val overall: String,
    val services: List<AdminServiceHealth>,
    val botVersion: String,
    val memoryUsageMb: Long,
    val cpuPercent: Double,
)

@Serializable
data class PlatformEvent(
    val message: String,
    val time: String,
    val type: String,
)

// ─── Feature Flags ───────────────────────────────────────────────────────────

/**
 * A staged-rollout feature flag's global definition (backend `FeatureFlagDto`). [requiresConsent] is a consent
 * TYPE key the tenant must hold for the flag to apply (or null), not a boolean — the flag can require a specific
 * consent grant, not merely "some consent". [deploymentMode] is `saas` | `self_host` | null (both).
 */
@Serializable
data class FeatureFlag(
    val key: String = "",
    val description: String? = null,
    val isEnabledGlobally: Boolean = false,
    val rolloutPercentage: Int = 0,
    val minTierKey: String? = null,
    val requiresConsent: String? = null,
    val deploymentMode: String? = null,
)

@Serializable
data class AdminSetFeatureFlagRequest(
    val key: String,
    val description: String? = null,
    val isEnabledGlobally: Boolean,
    val rolloutPercentage: Int,
    val minTierKey: String? = null,
    val requiresConsent: String? = null,
    val deploymentMode: String? = null,
)

@Serializable
data class AdminSetFeatureFlagOverrideRequest(
    val isEnabled: Boolean,
    val reason: String? = null,
    val expiresAt: String? = null,
)

/**
 * The counted blast radius of flipping a flag's GLOBAL toggle (backend `FeatureFlagBlastRadiusDto`) — fetched
 * BEFORE a kill-switch commits so the operator sees exactly how many active channels it reaches. A per-tenant
 * override insulates that channel from the global toggle, so overridden channels are never counted here.
 */
@Serializable
data class FeatureFlagBlastRadiusDto(
    val tenantsAffected: Int = 0,
    val sampleChannelNames: List<String> = emptyList(),
)

// ─── Admin Billing ───────────────────────────────────────────────────────────

@Serializable
data class InviteCode(
    val id: String,
    val code: String,
    val maxRedemptions: Int,
    val redemptionCount: Int,
    val grantsFoundersBadge: Boolean,
    val grantsTierId: String? = null,
    val grantsTierKey: String? = null,
    val expiresAt: String? = null,
)

@Serializable
data class AdminCreateInviteCodeRequest(
    val maxRedemptions: Int,
    @SerialName("grantsFoundersBadge") val grantsFoundersBadge: Boolean,
    @SerialName("grantsTierId") val grantsTierId: String? = null,
    val expiresAt: String? = null,
)

@Serializable
data class AdminGrantTierRequest(
    @SerialName("tierId") val tierId: String,
    val isInviteOnlyGrant: Boolean,
)

// ─── Tier authoring (S-ADMIN-4a) ─────────────────────────────────────────────

/** One quota lever a tier carries (backend `TierLimitDto`). `-1` means unlimited. */
@Serializable
data class AdminTierLimit(
    val limitKey: String,
    val limitValue: Long,
)

/** A billing plan as seen by the admin tier editor — the backend `TierDto`, listing EVERY tier
 * (public and internal), not only the public catalogue `IBillingTierService` exposes to pricing. */
@Serializable
data class AdminTier(
    val id: String,
    val key: String,
    val displayName: String,
    val priceCents: Int,
    val currency: String,
    val allowsCustomBotName: Boolean,
    val prioritySupport: Boolean,
    val sortOrder: Int,
    val limits: List<AdminTierLimit> = emptyList(),
    val isPublic: Boolean = true,
)

/**
 * The counted blast radius of editing a tier (backend `TierChangePreviewDto`) — the real, live number of
 * tenants on the tier right now. Fetched BEFORE an edit to an in-use tier commits; [affectedTenantCount]
 * must be echoed back on [AdminUpdateTierRequest.confirmedAffectedTenantCount] or the save is rejected.
 */
@Serializable
data class AdminTierChangePreview(
    val affectedTenantCount: Int = 0,
    val sampleChannelNames: List<String> = emptyList(),
)

/** Author a brand-new tier. Zero blast radius by construction — no confirmation required. */
@Serializable
data class AdminCreateTierRequest(
    val key: String,
    val displayName: String,
    val priceCents: Int,
    val currency: String,
    val allowsCustomBotName: Boolean,
    val prioritySupport: Boolean,
    val isPublic: Boolean,
    val sortOrder: Int,
    val limits: List<AdminTierLimit>,
)

/** Edit an existing tier. [confirmedAffectedTenantCount] must equal the count a fresh
 * [AdminApi.previewTierChange] just returned — the server rejects (`PREVIEW_STALE`) a stale count. */
@Serializable
data class AdminUpdateTierRequest(
    val displayName: String,
    val priceCents: Int,
    val currency: String,
    val allowsCustomBotName: Boolean,
    val prioritySupport: Boolean,
    val isPublic: Boolean,
    val sortOrder: Int,
    val limits: List<AdminTierLimit>,
    val confirmedAffectedTenantCount: Int,
)

// ─── Priced-unit authoring (S-ADMIN-4d) ──────────────────────────────────────

/**
 * The owner-authored real-world price of one usage unit (backend `PricedUnitDto`). Money is integer minor
 * units, never a float or a bare number without its currency: [priceMinorUnitsPerBatch] is charged per
 * [batchSize] raw units of [unitKey] — a batch greater than 1 lets a real fraction-of-a-cent cost per raw
 * unit (a TTS character, a millisecond of sandbox CPU) be represented exactly in integers.
 */
@Serializable
data class AdminPricedUnit(
    val id: String,
    val unitKey: String,
    val currency: String,
    val priceMinorUnitsPerBatch: Long,
    val batchSize: Long,
)

/** Author (create or reprice) one usage unit. Upsert by [unitKey] — a key with no existing row is created,
 * an already-priced key has its price overwritten. */
@Serializable
data class AdminAuthorPricedUnitRequest(
    val unitKey: String,
    val currency: String,
    val priceMinorUnitsPerBatch: Long,
    val batchSize: Long,
)

// ─── Comps and entitlement grants (S-ADMIN-4b) ───────────────────────────────

/** A live comp (backend `EntitlementGrantDto`) — a time-boxed elevation of one tenant's effective tier. */
@Serializable
data class AdminEntitlementGrant(
    val id: String,
    val broadcasterId: String,
    val grantedTierId: String,
    val grantedTierKey: String,
    val reason: String,
    val expiresAt: String,
    val issuedAt: String,
    val issuedByAdminId: String? = null,
)

/**
 * The counted blast radius of comping a tenant to a tier (backend `EntitlementGrantPreviewDto`) — the limit
 * keys that would actually change for THIS tenant. [changedLimitCount] must be echoed back on
 * [AdminIssueEntitlementGrantRequest.confirmedChangedLimitCount] or the issue is rejected.
 */
@Serializable
data class AdminEntitlementGrantPreview(
    val currentTierKey: String = "",
    val grantedTierKey: String = "",
    val changedLimitCount: Int = 0,
    val changedLimitKeys: List<String> = emptyList(),
)

/** Issue a comp. [confirmedChangedLimitCount] must equal the count a fresh
 * [AdminApi.previewEntitlementGrant] just returned — the server rejects (`PREVIEW_STALE`) a stale count. */
@Serializable
data class AdminIssueEntitlementGrantRequest(
    val tierId: String,
    val reason: String,
    val expiresAt: String,
    val confirmedChangedLimitCount: Int,
)

// ─── Invoices, dunning and refunds (S-ADMIN-4c) ──────────────────────────────

/**
 * A billing invoice for one tenant (backend `InvoiceDto`). [dunningStatus] is the backend enum name
 * (`NotDunning` | `Current` | `PastDue`) computed by `Invoice.ResolveDunningStatus` — the SAME rule the rest
 * of the backend would apply, never a value invented on the client. Amounts are integer minor units
 * ([currency] says which).
 */
@Serializable
data class AdminInvoice(
    val id: String,
    val number: String? = null,
    val status: String,
    val amountDueCents: Int,
    val amountPaidCents: Int,
    val currency: String,
    val periodStart: String? = null,
    val periodEnd: String? = null,
    val issuedAt: String,
    val paidAt: String? = null,
    val hostedInvoiceUrl: String? = null,
    val dueAt: String? = null,
    val dunningStatus: String,
    val amountRefundedCents: Int = 0,
    val refundedAt: String? = null,
)

// ─── Impersonation (admin act-as) ────────────────────────────────────────────

/**
 * The minted act-as session — the server `ImpersonationTokenDto`. Minting REQUIRES an already-open,
 * audited support session (a [TenantAccessGrant]); the token's [expiresAt] is clamped to that grant's
 * remaining time server-side. [sessionId] is what [AdminApi.endImpersonation] ends the session with.
 * [user] is the backend `UserDto`; reuses [UserSearchResult] (the contract-guarded `UserDto` map) rather
 * than [AdminUser] — the latter's required `login`/`role`/`channelCount` are absent from `UserDto` and
 * would fail deserialization. Only [UserSearchResult.displayName] is needed here (the "Acting as …" banner).
 */
@Serializable
data class ImpersonationTokenDto(
    val accessToken: String,
    val expiresAt: String,
    val sessionId: String,
    val user: UserSearchResult,
)

/** Request body for [AdminApi.impersonate] — the open support session this act-as token is minted under, plus
 * the mandatory, audited [justification] the server records for it. */
@Serializable
data class ImpersonateUserRequest(val accessGrantId: String, val justification: String)

// ── EventSub subscription health + outbound webhook delivery log/replay (S-ADMIN-6a) ──

/** One topic in the REAL EventSub registry `TwitchEventSubHostedService` maintains for one tenant. */
@Serializable
data class AdminEventSubTopicHealth(
    val subscriptionId: String,
    val eventType: String,
    val version: String,
    val status: String,
    val enabled: Boolean,
    val lastError: String? = null,
    val lastConfirmedAt: String,
)

/** One tenant's EventSub registry rows, grouped for the 2am operator console. */
@Serializable
data class AdminEventSubTenantHealth(
    val broadcasterId: String,
    val channelDisplayName: String,
    val topics: List<AdminEventSubTopicHealth> = emptyList(),
)

/** One outbound webhook delivery attempt across ALL tenants. [endpointName] reads "(deleted endpoint)" when
 * the endpoint has since been soft-deleted; [endpointCanReplay] is false when the endpoint is deleted or
 * disabled — the row is kept either way, never hidden. */
@Serializable
data class AdminWebhookDelivery(
    val id: Long,
    val broadcasterId: String,
    val endpointId: String,
    val endpointName: String,
    val endpointCanReplay: Boolean,
    val eventType: String,
    val attempt: Int,
    val status: String,
    val responseCode: Int? = null,
    val durationMs: Int? = null,
    val error: String? = null,
    val createdAt: String,
)

/** The outcome of an admin-initiated replay: a genuinely NEW delivery row was created and sent. */
@Serializable
data class AdminWebhookReplayResult(
    val originalDeliveryId: Long,
    val newDeliveryId: Long,
    val status: String,
    val responseCode: Int? = null,
)

// ── Background job queue + retry, per-tenant usage (S-ADMIN-6b) ──

/**
 * One row of the REAL background job queue — a `ScheduledPipelineTask` (the deferred one-shot pipeline
 * dispatch primitive, e.g. a voice-swap auto-revert), never a fabricated parallel queue. [status] is the raw
 * persisted value (`pending`/`fired`/`cancelled`/`expired`); [displayState] is the operator-facing label
 * (`queued`/`running`/`succeeded`/`failed`/`cancelled`). [canRetry] is true only for a genuinely failed job
 * whose target pipeline still exists.
 */
@Serializable
data class AdminScheduledJob(
    val id: String,
    val broadcasterId: String,
    val channelDisplayName: String,
    val pipelineId: String,
    val pipelineName: String? = null,
    val pipelineExists: Boolean,
    val status: String,
    val displayState: String,
    val dueAt: String,
    val firedAt: String? = null,
    val createdAt: String,
    val triggeredByDisplayName: String,
    val canRetry: Boolean,
)

/** The outcome of an admin-initiated job retry: a brand-new task row was appended, due immediately — the
 * original failed attempt is never mutated. */
@Serializable
data class AdminScheduledJobRetryResult(
    val originalTaskId: String,
    val newTaskId: String,
    val pipelineId: String,
    val pipelineName: String,
    val newDueAt: String,
)

/**
 * One metered quantity for a tenant's usage period, straight off the real recorded rows, joined against the
 * owner-authored priced-unit catalogue (S-ADMIN-4d). [costMinorUnits] and [currency] are both `null` when
 * [metricKey] has no priced-unit row — UNPRICED, never reported as a zero cost.
 */
@Serializable
data class AdminTenantUsageMetric(
    val metricKey: String,
    val quantity: Long,
    val costMinorUnits: Long? = null,
    val currency: String? = null,
)

/** One currency's worth of a tenant's total priced usage cost for the period (backend `AdminTenantUsageCostDto`). */
@Serializable
data class AdminTenantUsageCost(
    val currency: String,
    val minorUnits: Long,
)

/**
 * One tenant's usage for its most recent metering period — computed purely from recorded usage.
 * [periodStart]/[periodEnd] state exactly which window the figures cover. Cost (S-ADMIN-4d) is reported ONLY
 * for units the owner has actually priced: [ttsCostMinorUnits] is `null` when TTS characters are unpriced,
 * [totalCostsByCurrency] sums every priced figure, and [unpricedUnitKeys] names every unit this tenant
 * actually used that still has no price — so an unpriced unit is never silently shown as costing nothing.
 */
@Serializable
data class AdminTenantUsage(
    val broadcasterId: String,
    val channelDisplayName: String,
    val periodStart: String,
    val periodEnd: String,
    val metrics: List<AdminTenantUsageMetric> = emptyList(),
    val ttsCharacterCount: Long = 0,
    val ttsCostMinorUnits: Long? = null,
    val ttsCurrency: String? = null,
    val totalCostsByCurrency: List<AdminTenantUsageCost> = emptyList(),
    val unpricedUnitKeys: List<String> = emptyList(),
)

// ── Error budget + event-store replay (S-ADMIN-6c) ──

/** One tenant's error budget for the trailing 24-hour window, computed purely from real outbound webhook
 * delivery outcomes — never a fabricated percentage. [errorRate]/[budgetRemainingFraction] are null when
 * [attempts] is 0 (nothing measured yet). [targetSuccessRate] is a stated policy threshold, not a measured
 * quantity — the allowed error rate it implies is what [budgetRemainingFraction] is computed against. */
@Serializable
data class AdminTenantErrorBudget(
    val broadcasterId: String,
    val channelDisplayName: String,
    val windowStartUtc: String,
    val windowEndUtc: String,
    val attempts: Long,
    val errors: Long,
    val errorRate: Double? = null,
    val targetSuccessRate: Double,
    val budgetRemainingFraction: Double? = null,
)

/** One projection an admin replay can target, and the REAL event types it subscribes to — reflected straight
 * off the registered projections, never a hardcoded dropdown. */
@Serializable
data class AdminReplayableProjection(
    val projectionName: String,
    val isGlobal: Boolean,
    val subscribedEventTypes: List<String> = emptyList(),
)

/** The REAL count of journal events a tenant + window + optional event-type scope would replay into the
 * named projection, computed at preview time — never an estimate. */
@Serializable
data class AdminEventReplayPreview(
    val broadcasterId: String,
    val projectionName: String,
    val fromUtc: String,
    val toUtc: String,
    val eventType: String? = null,
    val matchingEventCount: Long,
)

/** The outcome of an admin-initiated replay: [appliedCount] journal events in the stated scope were
 * re-applied, in order, to the named projection. Never mutates the journal itself. */
@Serializable
data class AdminEventReplayResult(
    val broadcasterId: String,
    val projectionName: String,
    val fromUtc: String,
    val toUtc: String,
    val eventType: String? = null,
    val appliedCount: Long,
)

@Serializable
data class AdminEventReplayPreviewRequest(
    val broadcasterId: String,
    val projectionName: String,
    val fromUtc: String,
    val toUtc: String,
    val eventType: String? = null,
)

/** [expectedCount] must match the scope's REAL count at execution time — the server fails closed
 * (STALE_COUNT) otherwise, so an operator can only ever act on a number they were actually shown. */
@Serializable
data class AdminEventReplayExecuteRequest(
    val broadcasterId: String,
    val projectionName: String,
    val fromUtc: String,
    val toUtc: String,
    val eventType: String? = null,
    val expectedCount: Long,
)

// ─── API interface + implementation ──────────────────────────────────────────

interface AdminApi {
    // Platform stats
    suspend fun getStats(): ApiResult<AdminStats>
    suspend fun getChannels(
        search: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
        sort: String? = null,
        isLive: Boolean? = null,
    ): ApiResult<PaginatedEnvelope<AdminChannel>>
    suspend fun getUsers(
        search: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
        sort: String? = null,
        role: String? = null,
    ): ApiResult<PaginatedEnvelope<AdminUser>>
    suspend fun getSystem(): ApiResult<AdminSystem>
    suspend fun getHealth(): ApiResult<List<AdminServiceHealth>>
    suspend fun getEvents(): ApiResult<List<PlatformEvent>>

    // Feature flags
    suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>>
    suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag>
    suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit>
    suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit>
    suspend fun previewFeatureFlagBlastRadius(flagKey: String): ApiResult<FeatureFlagBlastRadiusDto>

    // Admin billing
    suspend fun getInviteCodes(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<InviteCode>>
    suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode>
    suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit>
    suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit>
    suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit>

    // Tier authoring (S-ADMIN-4a)
    suspend fun getTiers(): ApiResult<List<AdminTier>>
    suspend fun previewTierChange(tierId: String): ApiResult<AdminTierChangePreview>
    suspend fun createTier(body: AdminCreateTierRequest): ApiResult<AdminTier>
    suspend fun updateTier(tierId: String, body: AdminUpdateTierRequest): ApiResult<AdminTier>

    // Priced-unit authoring (S-ADMIN-4d). Same NOT_IMPLEMENTED default as the S-ADMIN-6a/6b/6c methods
    // above — a fake AdminApi written before this surface existed should not have to grow overrides for it.
    suspend fun getPricedUnits(): ApiResult<List<AdminPricedUnit>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    suspend fun authorPricedUnit(body: AdminAuthorPricedUnitRequest): ApiResult<AdminPricedUnit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    // Comps and entitlement grants (S-ADMIN-4b)
    suspend fun getEntitlementGrants(broadcasterId: String): ApiResult<List<AdminEntitlementGrant>>
    suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String): ApiResult<AdminEntitlementGrantPreview>
    suspend fun issueEntitlementGrant(broadcasterId: String, body: AdminIssueEntitlementGrantRequest): ApiResult<AdminEntitlementGrant>

    // Invoices, dunning and refunds (S-ADMIN-4c)
    suspend fun getInvoices(broadcasterId: String): ApiResult<List<AdminInvoice>>
    suspend fun refundInvoice(invoiceId: String): ApiResult<AdminInvoice>

    // Impersonation (admin act-as)
    /** Mints an act-as token for [subjectUserId], scoped to the already-open [accessGrantId] support session,
     * carrying the mandatory, audited [justification]. */
    suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto>

    /** Ends the act-as session — the minted token is revoked server-side and stops authenticating immediately. */
    suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit>

    // Provider app credentials (platform OAuth apps)
    /** Every provider's credential state. No secret is ever returned — only whether one exists and its source. */
    suspend fun getProviderCredentials(): ApiResult<List<ProviderCredential>>

    /** Stores a client id and/or secret. A blank field is left untouched; clearing is [clearProviderCredential]. */
    suspend fun saveProviderCredential(
        provider: String,
        body: SaveProviderCredentialBody,
    ): ApiResult<ProviderCredential>

    /** Removes the stored rows so the environment resolves again. Destructive, and deliberately separate. */
    suspend fun clearProviderCredential(provider: String): ApiResult<ProviderCredential>

    // EventSub subscription health + outbound webhook delivery log/replay (S-ADMIN-6a). Defaulted to
    // NOT_IMPLEMENTED so the many pre-existing fakes across the admin test suite (each built for an
    // unrelated tab) do not need a mechanical touch just to keep compiling — a fake that DOES need these
    // for a real assertion overrides them explicitly, same as every other method here.

    /** The real EventSub registry, grouped by tenant. */
    suspend fun getEventSubHealth(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminEventSubTenantHealth>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** Cross-tenant outbound webhook delivery log, newest attempt first. */
    suspend fun getWebhookDeliveries(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminWebhookDelivery>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** Replays one delivery: a genuinely new attempt is sent and appended; the original is untouched. */
    suspend fun replayWebhookDelivery(deliveryId: Long): ApiResult<AdminWebhookReplayResult> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    // Background job queue + retry, per-tenant usage (S-ADMIN-6b). Same NOT_IMPLEMENTED default as the
    // S-ADMIN-6a methods above, for the same reason.

    /** The real background job queue — every `ScheduledPipelineTask` row across every tenant, newest first. */
    suspend fun getScheduledJobs(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminScheduledJob>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** Retries one failed job: a brand-new deferred run is scheduled and appended; the original is untouched. */
    suspend fun retryScheduledJob(taskId: String): ApiResult<AdminScheduledJobRetryResult> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** Per-tenant usage for each tenant's most recent metering period, computed from recorded usage. */
    suspend fun getTenantUsage(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminTenantUsage>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    // Error budget + event-store replay (S-ADMIN-6c). Same NOT_IMPLEMENTED default as the S-ADMIN-6a/6b
    // methods above, for the same reason.

    /** Per-tenant error budget for the trailing 24-hour window, computed from real webhook delivery outcomes. */
    suspend fun getErrorBudget(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminTenantErrorBudget>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** The registered projections a replay can target, and the real event types each subscribes to. */
    suspend fun getReplayableProjections(): ApiResult<List<AdminReplayableProjection>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** Counts, WITHOUT replaying anything, how many events the given scope would replay. */
    suspend fun previewEventReplay(
        broadcasterId: String,
        projectionName: String,
        fromUtc: String,
        toUtc: String,
        eventType: String?,
    ): ApiResult<AdminEventReplayPreview> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    /** Re-applies every event in the scope to the named projection. Refused (STALE_COUNT) unless
     * [expectedCount] matches the scope's REAL count at execution time. */
    suspend fun executeEventReplay(
        broadcasterId: String,
        projectionName: String,
        fromUtc: String,
        toUtc: String,
        eventType: String?,
        expectedCount: Long,
    ): ApiResult<AdminEventReplayResult> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

/**
 * One provider's app-credential state.
 *
 * [clientId] is the RESOLVED id — what the OAuth flows will actually send — and is safe to show: it appears
 * in every OAuth URL a viewer's browser already sees. There is no secret field, by design; [secretSource]
 * says only whether one exists and which source wins.
 */
@Serializable
data class ProviderCredential(
    val provider: String = "",
    val clientId: String? = null,
    val clientIdSource: String = "unset",
    val secretSource: String = "unset",
    val appDecisionRecorded: Boolean = false,
    val supported: Boolean = true,
)

@Serializable
data class SaveProviderCredentialBody(
    val clientId: String? = null,
    val clientSecret: String? = null,
)

class AdminApiImpl(private val client: ApiClient) : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> =
        client.getEnvelope("api/v1/admin/stats")

    override suspend fun getChannels(
        search: String?,
        page: Int,
        pageSize: Int,
        sort: String?,
        isLive: Boolean?,
    ): ApiResult<PaginatedEnvelope<AdminChannel>> =
        client.getDirect(
            "api/v1/admin/channels?page=$page&pageSize=$pageSize" +
                searchQuery(search) +
                sortQuery(sort) +
                (isLive?.let { "&isLive=$it" } ?: "")
        )

    override suspend fun getUsers(
        search: String?,
        page: Int,
        pageSize: Int,
        sort: String?,
        role: String?,
    ): ApiResult<PaginatedEnvelope<AdminUser>> =
        client.getDirect(
            "api/v1/admin/users?page=$page&pageSize=$pageSize" +
                searchQuery(search) +
                sortQuery(sort) +
                (role?.takeIf { it.isNotBlank() }?.let { "&role=${it.encodeQuery()}" } ?: "")
        )

    override suspend fun getSystem(): ApiResult<AdminSystem> =
        client.getEnvelope("api/v1/admin/system")

    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> =
        client.getEnvelope("api/v1/admin/health")

    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> =
        client.getEnvelope("api/v1/admin/events")

    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> =
        client.getEnvelope("api/v1/admin/feature-flags")

    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        client.putEnvelope("api/v1/admin/feature-flags", body)

    override suspend fun setFeatureFlagOverride(
        flagKey: String,
        broadcasterId: String,
        body: AdminSetFeatureFlagOverrideRequest,
    ): ApiResult<Unit> =
        client.putUnit("api/v1/admin/feature-flags/$flagKey/overrides/$broadcasterId", body)

    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/admin/feature-flags/$flagKey/overrides/$broadcasterId")

    override suspend fun previewFeatureFlagBlastRadius(flagKey: String): ApiResult<FeatureFlagBlastRadiusDto> =
        client.getEnvelope("api/v1/admin/feature-flags/$flagKey/blast-radius")

    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        client.getDirect("api/v1/admin/billing/invites?page=$page&pageSize=$pageSize")

    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        client.postEnvelope("api/v1/admin/billing/invites", body)

    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> =
        client.postUnit("api/v1/admin/billing/invites/$inviteCodeId/revoke")

    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> =
        client.postUnit("api/v1/admin/billing/channels/$broadcasterId/grant-tier", body)

    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> =
        client.postUnit("api/v1/admin/billing/channels/$broadcasterId/grant-founder")

    override suspend fun getTiers(): ApiResult<List<AdminTier>> =
        client.getEnvelope("api/v1/admin/billing/tiers")

    override suspend fun previewTierChange(tierId: String): ApiResult<AdminTierChangePreview> =
        client.getEnvelope("api/v1/admin/billing/tiers/$tierId/preview")

    override suspend fun createTier(body: AdminCreateTierRequest): ApiResult<AdminTier> =
        client.postEnvelope("api/v1/admin/billing/tiers", body)

    override suspend fun updateTier(tierId: String, body: AdminUpdateTierRequest): ApiResult<AdminTier> =
        client.putEnvelope("api/v1/admin/billing/tiers/$tierId", body)

    override suspend fun getPricedUnits(): ApiResult<List<AdminPricedUnit>> =
        client.getEnvelope("api/v1/admin/billing/priced-units")

    override suspend fun authorPricedUnit(body: AdminAuthorPricedUnitRequest): ApiResult<AdminPricedUnit> =
        client.postEnvelope("api/v1/admin/billing/priced-units", body)

    override suspend fun getEntitlementGrants(broadcasterId: String): ApiResult<List<AdminEntitlementGrant>> =
        client.getEnvelope("api/v1/admin/billing/channels/$broadcasterId/grants")

    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String): ApiResult<AdminEntitlementGrantPreview> =
        client.getEnvelope("api/v1/admin/billing/channels/$broadcasterId/grants/preview?tierId=$tierId")

    override suspend fun issueEntitlementGrant(broadcasterId: String, body: AdminIssueEntitlementGrantRequest): ApiResult<AdminEntitlementGrant> =
        client.postEnvelope("api/v1/admin/billing/channels/$broadcasterId/grants", body)

    override suspend fun getInvoices(broadcasterId: String): ApiResult<List<AdminInvoice>> =
        client.getEnvelope("api/v1/admin/billing/channels/$broadcasterId/invoices")

    override suspend fun refundInvoice(invoiceId: String): ApiResult<AdminInvoice> =
        client.postEnvelope("api/v1/admin/billing/invoices/$invoiceId/refund")

    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> =
        client.postEnvelope(
            "api/v1/admin/users/$subjectUserId/impersonate",
            ImpersonateUserRequest(accessGrantId = accessGrantId, justification = justification),
        )

    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/admin/impersonation/$accessGrantId")

    override suspend fun getProviderCredentials(): ApiResult<List<ProviderCredential>> =
        client.getEnvelope("api/v1/admin/providers")

    override suspend fun saveProviderCredential(
        provider: String,
        body: SaveProviderCredentialBody,
    ): ApiResult<ProviderCredential> = client.putEnvelope("api/v1/admin/providers/$provider", body)

    override suspend fun clearProviderCredential(provider: String): ApiResult<ProviderCredential> =
        client.deleteEnvelope("api/v1/admin/providers/$provider")

    override suspend fun getEventSubHealth(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminEventSubTenantHealth>> =
        client.getDirect("api/v1/admin/eventsub/health?page=$page&pageSize=$pageSize")

    override suspend fun getWebhookDeliveries(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminWebhookDelivery>> =
        client.getDirect("api/v1/admin/webhooks/deliveries?page=$page&pageSize=$pageSize")

    override suspend fun replayWebhookDelivery(deliveryId: Long): ApiResult<AdminWebhookReplayResult> =
        client.postEnvelope("api/v1/admin/webhooks/deliveries/$deliveryId/replay")

    override suspend fun getScheduledJobs(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminScheduledJob>> =
        client.getDirect("api/v1/admin/jobs?page=$page&pageSize=$pageSize")

    override suspend fun retryScheduledJob(taskId: String): ApiResult<AdminScheduledJobRetryResult> =
        client.postEnvelope("api/v1/admin/jobs/$taskId/retry")

    override suspend fun getTenantUsage(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantUsage>> =
        client.getDirect("api/v1/admin/usage?page=$page&pageSize=$pageSize")

    override suspend fun getErrorBudget(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantErrorBudget>> =
        client.getDirect("api/v1/admin/error-budget?page=$page&pageSize=$pageSize")

    override suspend fun getReplayableProjections(): ApiResult<List<AdminReplayableProjection>> =
        client.getEnvelope("api/v1/admin/eventstore/projections")

    override suspend fun previewEventReplay(
        broadcasterId: String,
        projectionName: String,
        fromUtc: String,
        toUtc: String,
        eventType: String?,
    ): ApiResult<AdminEventReplayPreview> =
        client.postEnvelope(
            "api/v1/admin/eventstore/replay/preview",
            AdminEventReplayPreviewRequest(broadcasterId, projectionName, fromUtc, toUtc, eventType),
        )

    override suspend fun executeEventReplay(
        broadcasterId: String,
        projectionName: String,
        fromUtc: String,
        toUtc: String,
        eventType: String?,
        expectedCount: Long,
    ): ApiResult<AdminEventReplayResult> =
        client.postEnvelope(
            "api/v1/admin/eventstore/replay/execute",
            AdminEventReplayExecuteRequest(broadcasterId, projectionName, fromUtc, toUtc, eventType, expectedCount),
        )

    private fun searchQuery(search: String?): String =
        search?.takeIf { it.isNotBlank() }?.let { "&search=${it.encodeQuery()}" } ?: ""

    /** The server binds ordering from `sort`; an unknown value falls back to its default rather than failing. */
    private fun sortQuery(sort: String?): String =
        sort?.takeIf { it.isNotBlank() }?.let { "&sort=${it.encodeQuery()}" } ?: ""
}
