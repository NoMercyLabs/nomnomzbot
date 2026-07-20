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

// The typed rewards facade — the channel's real Twitch channel-point rewards, sourced by the backend from the
// Helix Custom Rewards endpoint (no fabricated rewards). The list is a `PaginatedResponse<RewardListItem>` (a
// flat `{ data: [...] }`), so it is read with getDirect like the channel/community lists. The same facade also
// drives the page's writes — create / update / toggle / delete — each treated as a Unit result because the
// Rewards page re-lists after every successful write. State holders depend on this interface and fake it in
// tests without HTTP.
//
// Backend routes (RewardsController):
//   GET    /api/v1/channels/{channelId}/rewards                     →  PaginatedResponse<RewardDetail>
//   POST   /api/v1/channels/{channelId}/rewards                     →  StatusResponseDto<RewardDetail> (201)
//   PUT    /api/v1/channels/{channelId}/rewards/{rewardId}          →  StatusResponseDto<RewardDetail>
//   DELETE /api/v1/channels/{channelId}/rewards/{rewardId}          →  204 No Content
//   POST   /api/v1/channels/{channelId}/rewards/import              →  StatusResponseDto<object> (summary)
//   POST   /api/v1/channels/{channelId}/rewards/{rewardId}/recreate →  StatusResponseDto<RewardDetail>
interface RewardsApi {
    /** The channel's channel-point rewards — the first page the backend resolves. */
    suspend fun list(channelId: String): ApiResult<List<RewardSummary>>

    /** Create a new channel-point reward on the channel (backend POST). */
    suspend fun create(channelId: String, body: CreateRewardBody): ApiResult<Unit>

    /**
     * Update an existing reward, addressed by its [rewardId] (the backend PUT route is keyed by the Twitch
     * reward id). A partial update: only the non-null [body] fields are applied — this is how a toggle is
     * expressed (flip `isEnabled`, leave the rest null).
     */
    suspend fun update(channelId: String, rewardId: String, body: UpdateRewardBody): ApiResult<Unit>

    /** Delete a reward, addressed by its [rewardId] (the backend DELETE route is keyed by the reward id). */
    suspend fun delete(channelId: String, rewardId: String): ApiResult<Unit>

    /**
     * The channel-points redemption queue (backend `RewardsController.ListRedemptions`, newest-first). Pass
     * [status] = "unfulfilled" for the pending lane, or null for the whole queue. Flat `PaginatedResponse`.
     */
    suspend fun redemptions(
        channelId: String,
        status: String? = null,
    ): ApiResult<List<RedemptionSummary>>

    /** Fulfil a queued redemption (Helix FULFILLED) — it leaves the pending lane. */
    suspend fun fulfillRedemption(channelId: String, redemptionId: String): ApiResult<Unit>

    /** Refund a queued redemption (Helix CANCELED — the viewer's points are returned). */
    suspend fun refundRedemption(channelId: String, redemptionId: String): ApiResult<Unit>

    /**
     * Trigger a full re-pull of the channel's custom rewards from Twitch (backend `POST /rewards/sync`). Useful
     * when rewards were created/modified directly on the Twitch dashboard and the bot's read model is stale.
     * The backend echoes a plain 200 with a status message; no body needed here.
     */
    suspend fun sync(channelId: String): ApiResult<Unit>

    /**
     * Import ALL of the channel's Twitch rewards — including EXTERNAL ones created outside the bot (in the Twitch
     * dashboard or by another app such as StreamElements) — into the read model (backend `POST /rewards/import`).
     * Distinct from [sync], which only refreshes the rewards the bot itself created: import brings the read-only
     * external rewards into view so they can be seen and, one by one, taken control of via [recreate]. Bodyless
     * POST; the backend echoes a status summary, so any 2xx is success and the page re-lists.
     */
    suspend fun import(channelId: String): ApiResult<Unit>

    /**
     * Take control of an EXTERNAL reward by recreating an equivalent reward under the bot's own Twitch client
     * (backend `POST /rewards/{rewardId}/recreate`), so the bot can subsequently manage (edit / toggle / delete)
     * it. Addressed by the external reward's [rewardId]. The backend returns the recreated `RewardDetail`, but the
     * page re-lists after the write, so any 2xx is success here.
     */
    suspend fun recreate(channelId: String, rewardId: String): ApiResult<Unit>

    /**
     * The channel's redemption countdown timers (backend `GET /rewards/redemption-timers`), active first then
     * recent history. `remainingSeconds` is LIVE clock-derived at response time, so a client can safely tick from
     * it between fetches. Returned as a `StatusResponseDto<IReadOnlyList<RedemptionTimerDto>>` → read with the
     * envelope unwrap.
     */
    suspend fun redemptionTimers(channelId: String): ApiResult<List<RedemptionTimer>>

    /** Pause a running redemption timer (it stops counting down; resume continues it). Addressed by [timerId]. */
    suspend fun pauseTimer(channelId: String, timerId: String): ApiResult<Unit>

    /** Resume a paused redemption timer (it continues counting down). Addressed by [timerId]. */
    suspend fun resumeTimer(channelId: String, timerId: String): ApiResult<Unit>

    /** Complete a redemption timer now — fulfils the redemption on Twitch (as expiry would). Addressed by [timerId]. */
    suspend fun completeTimer(channelId: String, timerId: String): ApiResult<Unit>

    /** Cancel a redemption timer — it just stops counting (refund stays the separate refund endpoint). */
    suspend fun cancelTimer(channelId: String, timerId: String): ApiResult<Unit>
}

class RestRewardsApi(private val client: ApiClient) : RewardsApi {

    override suspend fun list(channelId: String): ApiResult<List<RewardSummary>> {
        // Walk every page so ALL rewards show — PaginatedResponse is a flat `{ data, hasMore, nextPage }`.
        return client.getAllPages { page -> "api/v1/channels/$channelId/rewards?page=$page&pageSize=100" }
    }

    // The create response is a `StatusResponseDto<RewardDetail>` (201), but the controller re-fetches the list
    // after every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun create(channelId: String, body: CreateRewardBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards", body)

    override suspend fun update(
        channelId: String,
        rewardId: String,
        body: UpdateRewardBody,
    ): ApiResult<Unit> = client.putUnit("api/v1/channels/$channelId/rewards/$rewardId", body)

    override suspend fun delete(channelId: String, rewardId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/rewards/$rewardId")

    override suspend fun redemptions(
        channelId: String,
        status: String?,
    ): ApiResult<List<RedemptionSummary>> {
        // Flat PaginatedResponse like the rewards list — read with getDirect. First page only here; the pager
        // layers on later. The optional status filters the lane (e.g. "unfulfilled" = pending).
        val statusQuery: String = if (status.isNullOrBlank()) "" else "&status=$status"
        return when (
            val page: ApiResult<PaginatedEnvelope<RedemptionSummary>> =
                client.getDirect(
                    "api/v1/channels/$channelId/rewards/redemptions?page=1&pageSize=25$statusQuery"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // Bodyless POSTs — the backend resolves the reward id from the queue read model and re-lists are driven by
    // the controller, so any 2xx is success.
    override suspend fun fulfillRedemption(channelId: String, redemptionId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/redemptions/$redemptionId/fulfill")

    override suspend fun refundRedemption(channelId: String, redemptionId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/redemptions/$redemptionId/refund")

    // Bodyless POST — the backend re-pulls from Twitch and returns a plain status message; no body needed.
    override suspend fun sync(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/sync")

    // Bodyless POST — the backend pulls every Twitch reward (incl. external ones) into the read model and returns
    // a status summary; the page re-lists, so any 2xx is success.
    override suspend fun import(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/import")

    // Bodyless POST — the backend recreates the external reward under the bot's own client and returns the new
    // RewardDetail; the page re-lists, so any 2xx is success.
    override suspend fun recreate(channelId: String, rewardId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/$rewardId/recreate")

    // StatusResponseDto<IReadOnlyList<RedemptionTimerDto>> (a single-value envelope where the payload is the list),
    // so it is read with getEnvelope's `data: T` unwrap — like the games list.
    override suspend fun redemptionTimers(channelId: String): ApiResult<List<RedemptionTimer>> =
        client.getEnvelope("api/v1/channels/$channelId/rewards/redemption-timers")

    // Bodyless POSTs — the backend mutates the timer and the page re-fetches the list, so any 2xx is success.
    override suspend fun pauseTimer(channelId: String, timerId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/redemption-timers/$timerId/pause")

    override suspend fun resumeTimer(channelId: String, timerId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/redemption-timers/$timerId/resume")

    override suspend fun completeTimer(channelId: String, timerId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/redemption-timers/$timerId/complete")

    override suspend fun cancelTimer(channelId: String, timerId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/rewards/redemption-timers/$timerId/cancel")
}

/**
 * The create-reward request body (backend `CreateRewardRequest`). camelCase JSON. [title] and [cost] are the
 * required essentials the create dialog collects; [prompt] is the optional viewer-facing text. The backend's
 * create DTO has no enabled flag — a freshly created Twitch reward is live by default — so enabling/disabling
 * is an update concern, not a create one.
 */
@Serializable
data class CreateRewardBody(
    val title: String,
    val cost: Int,
    val prompt: String? = null,
    // The on-redeem bot response text (backend CreateRewardRequest.response).
    val response: String? = null,
    // Whether redeeming requires the viewer to type text (Twitch "require viewer input"). Maps to
    // CreateRewardRequest.isUserInputRequired.
    val isUserInputRequired: Boolean? = null,
    // The reward's card background colour as a hex string ("#RRGGBB"); null leaves Twitch's default.
    val backgroundColor: String? = null,
    // Twitch redemption limits: max redemptions per stream, max per user per stream, and a global cooldown in
    // seconds between redemptions. Null/0 on any = no limit for that dimension.
    val maxPerStream: Int? = null,
    val maxPerUserPerStream: Int? = null,
    val globalCooldownSeconds: Int? = null,
    // A countdown a redemption of this reward auto-starts (seconds; null/0 = no timer, capped 24h server-side).
    val timerDurationSeconds: Int? = null,
    // A pipeline to run when this reward is redeemed (a ULID; null = none). `explicitNulls = false` omits it when
    // absent so an unset binding never clears the stored one.
    val pipelineId: String? = null,
    // The reward's built-in typed action + its settings (backend actionType / actionSettings, an arbitrary JSON
    // object) — so a reward can be created to run a typed action, not only a bound pipeline.
    val actionType: String? = null,
    val actionSettings: kotlinx.serialization.json.JsonObject? = null,
)

/**
 * The update-reward request body (backend `UpdateRewardRequest`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; an edit sends [title] / [cost] / [prompt] (and may flip
 * [isEnabled]); all other fields stay null and the backend leaves them untouched. `explicitNulls = false` on
 * the shared Json means null fields are omitted from the wire body.
 */
@Serializable
data class UpdateRewardBody(
    val title: String? = null,
    val cost: Int? = null,
    val prompt: String? = null,
    val isEnabled: Boolean? = null,
    // Pause redemptions without disabling the reward (Twitch "pause"). Null leaves it unchanged.
    val isPaused: Boolean? = null,
    // Whether redeeming requires the viewer to type text. Null leaves it unchanged.
    val isUserInputRequired: Boolean? = null,
    // The reward card's background colour as a hex string ("#RRGGBB"). Null leaves it unchanged.
    val backgroundColor: String? = null,
    // Twitch redemption limits (max per stream, max per user per stream, global cooldown seconds). 0 clears a
    // limit; null leaves it unchanged.
    val maxPerStream: Int? = null,
    val maxPerUserPerStream: Int? = null,
    val globalCooldownSeconds: Int? = null,
    // The countdown a redemption auto-starts (seconds; 0 clears, capped 24h). Null omits it from the patch so a
    // toggle/edit that doesn't touch the timer leaves it unchanged.
    val timerDurationSeconds: Int? = null,
    // The pipeline bound to this reward (a ULID). Null omits it from the patch (unchanged).
    val pipelineId: String? = null,
    // The reward's built-in typed action + its settings (backend actionType / actionSettings). Null omits from
    // the patch (unchanged).
    val actionType: String? = null,
    val actionSettings: kotlinx.serialization.json.JsonObject? = null,
)

/**
 * A channel-point reward (backend `RewardDetail` — the list endpoint returns the same schema as get/create). The
 * field names are the serialized (camelCase) names of `RewardDetail`; the client reads the subset the row renders
 * (ApiClient's Json ignores unknown keys), so the heavier detail-only fields (cooldowns, paused) are omitted
 * here. [prompt] IS read — the edit dialog pre-fills it. [isManageable] is the one that drives the page's read-only gating: `true` when the bot's own
 * Twitch client created the reward (full CRUD), `false` for EXTERNAL rewards made in the Twitch dashboard or by
 * another app — those are read-only to us until recreated under the bot ("Take control").
 */
@Serializable
data class RewardSummary(
    val id: String,
    val title: String = "",
    val cost: Int = 0,
    // The viewer-facing prompt the operator set on Twitch (backend RewardDetail.prompt). The list endpoint returns
    // the full RewardDetail schema, so this arrives on every row and the edit dialog pre-fills it.
    val prompt: String? = null,
    val isEnabled: Boolean = false,
    val isManageable: Boolean = false,
    // Paused = live but temporarily not redeemable (Twitch "pause"); read so the edit dialog pre-fills the toggle.
    val isPaused: Boolean = false,
    // Whether redeeming requires the viewer to type text; read so the edit dialog pre-fills the toggle.
    val isUserInputRequired: Boolean = false,
    val backgroundColor: String? = null,
    val imageUrl: String? = null,
    // Twitch redemption limits (null/0 = no limit) — read so the edit dialog pre-fills the number fields.
    val maxPerStream: Int? = null,
    val maxPerUserPerStream: Int? = null,
    val globalCooldownSeconds: Int? = null,
    // The reward's countdown length (seconds; null/0 = none) and bound pipeline (a ULID; null = none). Read so the
    // edit dialog pre-fills the timer field + pipeline picker.
    val timerDurationSeconds: Int? = null,
    val pipelineId: String? = null,
    // The reward's built-in typed action + its settings (backend RewardDetail.actionType / actionSettings, an
    // arbitrary JSON object) — so the operator can read/configure a reward's action, not only a bound pipeline.
    val actionType: String? = null,
    val actionSettings: kotlinx.serialization.json.JsonObject? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * A redemption countdown timer (backend `RedemptionTimerDto`): the timer's identity, the reward it belongs to,
 * who redeemed it, the configured [durationSeconds], the LIVE [remainingSeconds] (clock-derived at fetch time, so
 * a client can safely tick from it), the [status] (`running` | `paused` | `completed` | `canceled`), and when it
 * [startedAt]. Powers the rewards page's live countdown list.
 */
@Serializable
data class RedemptionTimer(
    val id: String = "",
    val redemptionId: String = "",
    val rewardId: String = "",
    val rewardTitle: String = "",
    val redeemedBy: String = "",
    val durationSeconds: Int = 0,
    val remainingSeconds: Int = 0,
    val status: String = "",
    val startedAt: String = "",
)

/**
 * A redemption-queue row (backend `RedemptionListItem`). camelCase serialized names; the client reads the
 * subset the queue renders. [status] is `unfulfilled` (pending) / `fulfilled` / `canceled`; [redeemedAt] is the
 * ISO-8601 redeem time. [userInput] is the viewer's text for input-required rewards (e.g. a song link).
 */
@Serializable
data class RedemptionSummary(
    val redemptionId: String,
    val rewardId: String = "",
    val rewardTitle: String = "",
    val userId: String = "",
    val userDisplayName: String = "",
    val cost: Int = 0,
    val userInput: String? = null,
    val status: String = "",
    val redeemedAt: String = "",
)
