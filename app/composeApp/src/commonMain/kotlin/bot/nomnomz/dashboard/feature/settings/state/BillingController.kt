// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BillingApi
import bot.nomnomz.dashboard.core.network.BillingEntitlement
import bot.nomnomz.dashboard.core.network.BillingInvoice
import bot.nomnomz.dashboard.core.network.BillingPortal
import bot.nomnomz.dashboard.core.network.BillingSubscription
import bot.nomnomz.dashboard.core.network.BillingTier
import bot.nomnomz.dashboard.core.network.BillingUsageMetric
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CheckoutSession
import bot.nomnomz.dashboard.core.network.FoundersBadge
import bot.nomnomz.dashboard.core.network.InviteCodeValidation
import bot.nomnomz.dashboard.core.network.InviteRedeemResult
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// State-holder for the billing section in Settings. Self-host channels carry tier key "free"
// (or "self_host_*"), and all Stripe-dependent write actions fail closed on the backend —
// reads still succeed and surface the tier info, so the section adapts its content.
class BillingController(
    private val channelsApi: ChannelsApi,
    private val billingApi: BillingApi,
) {
    private val _state: MutableStateFlow<BillingState> = MutableStateFlow(BillingState.Loading)

    /** The billing section's render state. */
    val state: StateFlow<BillingState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load subscription + tiers + usage in parallel. */
    suspend fun load() {
        _state.value = BillingState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = BillingState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val subscription: BillingSubscription =
            when (val result: ApiResult<BillingSubscription> = billingApi.subscription(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = BillingState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val tiers: List<BillingTier> =
            when (val result: ApiResult<List<BillingTier>> = billingApi.tiers(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        val entitlement: BillingEntitlement? =
            when (val result: ApiResult<BillingEntitlement> = billingApi.entitlement(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        val usage: List<BillingUsageMetric> =
            when (val result: ApiResult<List<BillingUsageMetric>> = billingApi.usage(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        val invoices: List<BillingInvoice> =
            when (val result: ApiResult<List<BillingInvoice>> = billingApi.invoices(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        val foundersBadge: FoundersBadge? =
            when (val result: ApiResult<FoundersBadge?> = billingApi.foundersBadge(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        _state.value =
            BillingState.Ready(
                subscription = subscription,
                tiers = tiers.sortedBy { it.sortOrder },
                entitlement = entitlement,
                usage = usage,
                invoices = invoices,
                foundersBadge = foundersBadge,
                isSelfHost = subscription.tierKey.startsWith("self_host") || subscription.tierKey == "free",
            )
    }

    /**
     * Open the Stripe billing portal. Returns the portal URL to open in the browser, or null on
     * failure (error surface on the state). Only meaningful for SaaS channels.
     */
    suspend fun openPortal(): String? {
        val target: String = channelId ?: return null
        return when (val result: ApiResult<BillingPortal> = billingApi.openPortal(target)) {
            is ApiResult.Failure -> {
                applyActionError(result.error.message)
                null
            }
            is ApiResult.Ok -> result.value.portalUrl
        }
    }

    /** Start a Stripe Checkout for the given tier. Returns the checkout URL, or null on failure. */
    suspend fun startCheckout(tierKey: String): String? {
        val target: String = channelId ?: return null
        return when (val result: ApiResult<CheckoutSession> = billingApi.startCheckout(target, tierKey)) {
            is ApiResult.Failure -> {
                applyActionError(result.error.message)
                null
            }
            is ApiResult.Ok -> result.value.checkoutUrl
        }
    }

    /** Validate an invite code before redeeming it. */
    suspend fun validateInvite(code: String): InviteCodeValidation? {
        val target: String = channelId ?: return null
        return when (val result: ApiResult<InviteCodeValidation> = billingApi.validateInvite(target, code)) {
            is ApiResult.Failure -> {
                applyActionError(result.error.message)
                null
            }
            is ApiResult.Ok -> result.value
        }
    }

    /** Redeem an invite code and reload billing state to reflect the new tier/badge. */
    suspend fun redeemInvite(code: String): InviteRedeemResult? {
        val target: String = channelId ?: return null
        return when (val result: ApiResult<InviteRedeemResult> = billingApi.redeemInvite(target, code)) {
            is ApiResult.Failure -> {
                applyActionError(result.error.message)
                null
            }
            is ApiResult.Ok -> {
                load()
                result.value
            }
        }
    }

    /** Cancel the active subscription at period end, then reload. */
    suspend fun cancel() {
        val target: String = channelId ?: return
        when (val result: ApiResult<Unit> = billingApi.cancel(target)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> applyActionError(result.error.message)
        }
    }

    /** Resume a cancelled subscription that is still within the grace period, then reload. */
    suspend fun resume() {
        val target: String = channelId ?: return
        when (val result: ApiResult<Unit> = billingApi.resume(target)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> applyActionError(result.error.message)
        }
    }

    private fun applyActionError(message: String) {
        val current: BillingState = _state.value
        _state.value =
            if (current is BillingState.Ready) current.copy(actionError = message)
            else current
    }
}

/** The billing section's render state. */
sealed interface BillingState {
    data object Loading : BillingState

    data class Ready(
        val subscription: BillingSubscription,
        val tiers: List<BillingTier>,
        val entitlement: BillingEntitlement?,
        val usage: List<BillingUsageMetric>,
        val invoices: List<BillingInvoice>,
        val foundersBadge: FoundersBadge?,
        /** True when the channel is a self-host install (tier "free" / "self_host_*"). */
        val isSelfHost: Boolean,
        val actionError: String? = null,
    ) : BillingState

    data class Error(val detail: String) : BillingState
}
