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

// Tenant self-serve billing (BillingController). Billing endpoints are all Broadcaster-floor —
// mods/editors never touch billing. All operations target the channel identified by channelId.
// Self-host channels carry tier key "free" (or any "self_host_*" key), which makes every
// Stripe-dependent action fail closed on the backend — the read calls still succeed and surface
// the tier info so the UI can adapt.
interface BillingApi {
    /** The channel's active subscription record (tier, status, period dates). */
    suspend fun subscription(channelId: String): ApiResult<BillingSubscription>

    /** All public billing tiers with their limits. */
    suspend fun tiers(channelId: String): ApiResult<List<BillingTier>>

    /** The channel's effective entitlement (the single source for feature-gating + quota). */
    suspend fun entitlement(channelId: String): ApiResult<BillingEntitlement>

    /** Current-period usage metrics for all metered limits. */
    suspend fun usage(channelId: String): ApiResult<List<BillingUsageMetric>>

    /** Paginated invoice list for the channel. */
    suspend fun invoices(channelId: String): ApiResult<List<BillingInvoice>>

    /** Start a Stripe Checkout session for the given tier; returns the redirect URL. */
    suspend fun startCheckout(channelId: String, tierKey: String): ApiResult<CheckoutSession>

    /** Open the Stripe billing portal for the channel; returns the redirect URL. */
    suspend fun openPortal(channelId: String): ApiResult<BillingPortal>

    /** Cancel the active subscription (at period end by default). */
    suspend fun cancel(channelId: String, atPeriodEnd: Boolean = true): ApiResult<Unit>

    /** Resume a subscription that was cancelled but is still in the grace period. */
    suspend fun resume(channelId: String): ApiResult<Unit>

    /** Change the active tier (upgrade/downgrade). */
    suspend fun changeTier(channelId: String, tierKey: String, atPeriodEnd: Boolean = false): ApiResult<Unit>

    /** Validate an invite code before redeeming (pre-flight check). */
    suspend fun validateInvite(channelId: String, code: String): ApiResult<InviteCodeValidation>

    /** Redeem an invite code against the channel. */
    suspend fun redeemInvite(channelId: String, code: String): ApiResult<InviteRedeemResult>

    /** The channel's founders badge (null when none). */
    suspend fun foundersBadge(channelId: String): ApiResult<FoundersBadge?>
}

class RestBillingApi(private val client: ApiClient) : BillingApi {
    override suspend fun subscription(channelId: String): ApiResult<BillingSubscription> =
        client.getEnvelope("api/v1/channels/$channelId/billing/subscription")

    override suspend fun tiers(channelId: String): ApiResult<List<BillingTier>> =
        client.getEnvelope("api/v1/channels/$channelId/billing/tiers")

    override suspend fun entitlement(channelId: String): ApiResult<BillingEntitlement> =
        client.getEnvelope("api/v1/channels/$channelId/billing/entitlement")

    override suspend fun usage(channelId: String): ApiResult<List<BillingUsageMetric>> =
        client.getEnvelope("api/v1/channels/$channelId/billing/usage")

    override suspend fun invoices(channelId: String): ApiResult<List<BillingInvoice>> =
        when (val r: ApiResult<PaginatedEnvelope<BillingInvoice>> =
            client.getDirect("api/v1/channels/$channelId/billing/invoices?page=1&pageSize=50")) {
            is ApiResult.Failure -> ApiResult.Failure(r.error)
            is ApiResult.Ok -> ApiResult.Ok(r.value.data)
        }

    override suspend fun startCheckout(channelId: String, tierKey: String): ApiResult<CheckoutSession> =
        client.postEnvelope(
            "api/v1/channels/$channelId/billing/checkout",
            StartCheckoutBody(tierKey = tierKey),
        )

    override suspend fun openPortal(channelId: String): ApiResult<BillingPortal> =
        client.postEnvelope("api/v1/channels/$channelId/billing/portal")

    override suspend fun cancel(channelId: String, atPeriodEnd: Boolean): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/billing/cancel",
            CancelSubscriptionBody(atPeriodEnd = atPeriodEnd),
        )

    override suspend fun resume(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/billing/resume")

    override suspend fun changeTier(channelId: String, tierKey: String, atPeriodEnd: Boolean): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/billing/change-tier",
            ChangeTierBody(tierKey = tierKey, atPeriodEnd = atPeriodEnd),
        )

    override suspend fun validateInvite(channelId: String, code: String): ApiResult<InviteCodeValidation> =
        client.postEnvelope(
            "api/v1/channels/$channelId/billing/invite/validate",
            InviteCodeBody(code = code),
        )

    override suspend fun redeemInvite(channelId: String, code: String): ApiResult<InviteRedeemResult> =
        client.postEnvelope(
            "api/v1/channels/$channelId/billing/invite/redeem",
            InviteCodeBody(code = code),
        )

    override suspend fun foundersBadge(channelId: String): ApiResult<FoundersBadge?> =
        client.getEnvelope("api/v1/channels/$channelId/billing/founders-badge")
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

@Serializable
data class BillingSubscription(
    val id: String,
    val broadcasterId: String,
    val tierKey: String,
    val tierDisplayName: String,
    val status: String,
    val cancelAtPeriodEnd: Boolean = false,
    val currentPeriodEnd: String? = null,
    val trialEndsAt: String? = null,
    val gracePeriodEndsAt: String? = null,
    val isInviteOnlyGrant: Boolean = false,
    val allowsCustomBotName: Boolean = false,
    val prioritySupport: Boolean = false,
)

@Serializable
data class BillingTierLimit(val limitKey: String, val limitValue: Long)

@Serializable
data class BillingTier(
    val id: String,
    val key: String,
    val displayName: String,
    val priceCents: Int,
    val currency: String,
    val allowsCustomBotName: Boolean = false,
    val prioritySupport: Boolean = false,
    val sortOrder: Int = 0,
    val limits: List<BillingTierLimit> = emptyList(),
)

@Serializable
data class BillingEntitlement(
    val tierKey: String,
    val allowsCustomBotName: Boolean = false,
    val prioritySupport: Boolean = false,
    val limits: Map<String, Long> = emptyMap(),
)

@Serializable
data class BillingUsageMetric(
    val metricKey: String,
    val used: Long,
    val limit: Long,
    val remaining: Long,
    val periodStart: String,
    val periodEnd: String,
)

@Serializable
data class BillingInvoice(
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
)

@Serializable
data class CheckoutSession(val checkoutUrl: String, val stripeSessionId: String)

@Serializable
data class BillingPortal(val portalUrl: String)

@Serializable
data class FoundersBadge(
    val id: String,
    val grantedAt: String,
    val isActive: Boolean,
    val inviteCode: String? = null,
)

@Serializable
data class InviteCodeValidation(
    val isValid: Boolean,
    val code: String,
    val grantsFoundersBadge: Boolean = false,
    val grantsTierKey: String? = null,
    val remainingRedemptions: Int = 0,
    val expiresAt: String? = null,
)

@Serializable
data class InviteRedeemResult(
    val grantedFoundersBadge: Boolean = false,
    val grantedTierKey: String? = null,
    val foundersBadge: FoundersBadge? = null,
)

// ── Request bodies ────────────────────────────────────────────────────────────

@Serializable
private data class StartCheckoutBody(val tierKey: String, val successUrl: String? = null, val cancelUrl: String? = null)

@Serializable
private data class CancelSubscriptionBody(val atPeriodEnd: Boolean, val reason: String? = null)

@Serializable
private data class ChangeTierBody(val tierKey: String, val atPeriodEnd: Boolean)

@Serializable
private data class InviteCodeBody(val code: String)
