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

// The typed spam-defence facade. Two things live behind it: the weights the operator controls, and the
// log of every verdict the engine has reached.
//
// Backend routes (SpamDefenseController):
//   GET  .../spam-defense/policy                             →  SpamDefensePolicy   (spam:policy:read)
//   PUT  .../spam-defense/policy                             →  SpamDefenseSettings (spam:policy:manage)
//   GET  .../spam-defense/detections?page&pageSize           →  List<SpamDetection> (spam:detections:read)
//   POST .../spam-defense/detections/{id}/overturn           →  Unit                (spam:detections:manage)
interface SpamDefenseApi {
    /** Values, the metadata to render an editor for them, and the guarantees that have no switch. */
    suspend fun policy(channelId: String): ApiResult<SpamDefensePolicy>

    /** Replace the whole settings record; the backend validates ranges and returns the saved values. */
    suspend fun saveSettings(
        channelId: String,
        body: SpamDefenseSettings,
    ): ApiResult<SpamDefenseSettings>

    /** Recorded verdicts, newest first — the review queue and the dry-run report. */
    suspend fun detections(
        channelId: String,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<List<SpamDetection>>

    /** Mark a verdict wrong. */
    suspend fun overturn(channelId: String, detectionId: String): ApiResult<Unit>

    /** Correlated cohorts, newest first. */
    suspend fun campaigns(channelId: String): ApiResult<List<SpamCampaign>>

    /** Follow-bot blocks, each carrying the evidence that justified it. */
    suspend fun followBotBlocks(channelId: String): ApiResult<List<FollowBotBlockEntry>>

    /** Restore an entire spike batch at once. */
    suspend fun restoreFollowBotBatch(channelId: String, batchId: String): ApiResult<Unit>

    /** The platform-wide defaults every new channel inherits. Same shape as a channel's own policy. */
    suspend fun platformDefaults(): ApiResult<SpamDefensePolicy>

    /** Save the platform-wide defaults. */
    suspend fun savePlatformDefaults(body: SpamDefenseSettings): ApiResult<SpamDefenseSettings>
}

class RestSpamDefenseApi(private val client: ApiClient) : SpamDefenseApi {
    override suspend fun policy(channelId: String): ApiResult<SpamDefensePolicy> =
        client.getEnvelope("api/v1/channels/$channelId/spam-defense/policy")

    override suspend fun saveSettings(
        channelId: String,
        body: SpamDefenseSettings,
    ): ApiResult<SpamDefenseSettings> =
        client.putEnvelope("api/v1/channels/$channelId/spam-defense/policy", body)

    override suspend fun detections(
        channelId: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<List<SpamDetection>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/spam-defense/detections?page=$page&pageSize=$pageSize"
        )

    override suspend fun overturn(channelId: String, detectionId: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/spam-defense/detections/$detectionId/overturn"
        )

    override suspend fun campaigns(channelId: String): ApiResult<List<SpamCampaign>> =
        client.getEnvelope("api/v1/channels/$channelId/spam-defense/campaigns")

    override suspend fun followBotBlocks(
        channelId: String
    ): ApiResult<List<FollowBotBlockEntry>> =
        client.getEnvelope("api/v1/channels/$channelId/spam-defense/follow-bot-blocks")

    override suspend fun restoreFollowBotBatch(
        channelId: String,
        batchId: String,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/spam-defense/follow-bot-blocks/$batchId/restore"
        )

    override suspend fun platformDefaults(): ApiResult<SpamDefensePolicy> =
        client.getEnvelope("api/v1/admin/spam-defense/defaults")

    override suspend fun savePlatformDefaults(
        body: SpamDefenseSettings
    ): ApiResult<SpamDefenseSettings> =
        client.putEnvelope("api/v1/admin/spam-defense/defaults", body)
}

/**
 * The whole configuration surface in one response. [catalogue] travels WITH [settings] on purpose: a
 * client that hardcoded its own idea of the bounds would start rejecting saves the backend accepts the
 * first time a range moved server-side.
 *
 * [isPinned] is false while the channel has never saved anything, so the editor can show what is a
 * shipped default and what the operator actually chose.
 */
@Serializable
data class SpamDefensePolicy(
    val settings: SpamDefenseSettings = SpamDefenseSettings(),
    val catalogue: List<SpamSettingDescriptor> = emptyList(),
    val invariants: List<SpamInvariant> = emptyList(),
    val enforcementEligibleAt: String? = null,
    val isPinned: Boolean = false,
)

/**
 * Every knob (backend `SpamDefenseSettings`). The Kotlin defaults mirror the backend defaults, so an
 * editor rendered before the first load already shows the values a fresh channel runs on — and, crucially,
 * [dryRun] defaults to true here as it does there. A client that defaulted it to false would offer to
 * switch enforcement on for a channel that had never looked at its own results.
 */
@Serializable
data class SpamDefenseSettings(
    val isEnabled: Boolean = true,
    val dryRun: Boolean = true,
    val semiTrustedWatchHoursHere: Double = 10.0,
    val semiTrustedWatchHoursInstance: Double = 25.0,
    val nearDuplicateSimilarity: Double = 0.6,
    val minimumSkeletonLength: Int = 8,
    val nonLatinScriptGate: Boolean = false,
    val qualifyNoStandingShare: Double = 0.80,
    val dequalifyNoStandingShare: Double = 0.65,
    val minimumCohortSize: Int = 5,
    val windowSeconds: Int = 600,
    val maxWindowSeconds: Int = 1800,
    val actionDelaySeconds: Int = 8,
    val autoReverseOnDequalify: Boolean = true,
    val followSpikeFactor: Double = 5.0,
    val joinBurstFactor: Double = 4.0,
    val lockdownMinutes: Int = 15,
    val lockdownAutoExtend: Boolean = true,
    val lockdownMaxMinutes: Int = 60,
    val networkSubscribe: Boolean = true,
    val networkContribute: Boolean = false,
    val requiredCorroborations: Int = 3,
)

/**
 * How one knob is presented and bounded. The backend sends resource KEYS rather than words, because the
 * product ships in English and Dutch — the copy lives in `strings.xml` and is guarded there.
 */
@Serializable
data class SpamSettingDescriptor(
    val key: String = "",
    val group: String = "",
    val labelKey: String = "",
    val explanationKey: String = "",
    val costKey: String = "",
    val minimum: Double? = null,
    val maximum: Double? = null,
    val isToggle: Boolean = false,
)

/** A protection the operator gets for free and cannot turn off. */
@Serializable data class SpamInvariant(val decision: String = "", val guaranteeKey: String = "")

/** One recorded verdict (backend `SpamDetectionDto`). */
@Serializable
data class SpamDetection(
    val id: String = "",
    val subjectPlatformUserId: String = "",
    val subjectDisplayName: String = "",
    val provider: String = "",
    val messageId: String = "",
    val messageText: String = "",
    val signals: String = "",
    val confidence: String = "Zero",
    val tier: String = "Untrusted",
    val outcome: String = "None",
    val wouldHaveBeen: String = "None",
    val wasDryRun: Boolean = false,
    val reason: String = "",
    val overturnedAt: String? = null,
    val detectedAt: String = "",
)

/** One correlated cohort (backend `SpamCampaignDto`). */
@Serializable
data class SpamCampaign(
    val id: String = "",
    val skeleton: String = "",
    val verdict: String = "Watching",
    val qualificationCount: Int = 0,
    val actionableCount: Int = 0,
    val actionedCount: Int = 0,
    val noStandingShare: Double = 0.0,
    val mayContributeToNetwork: Boolean = true,
    val reversedAt: String? = null,
    val reversalReason: String? = null,
    val firstSeenAt: String = "",
    val lastSeenAt: String = "",
)

/**
 * One follow-bot block (backend `FollowBotBlockDto`). [indicators] is what makes the block reviewable,
 * and [batchExamined] is how an operator sees the sweep looked at more accounts than it acted on.
 */
@Serializable
data class FollowBotBlockEntry(
    val id: String = "",
    val batchId: String = "",
    val subjectPlatformUserId: String = "",
    val subjectUsername: String = "",
    val indicators: String = "",
    val batchExamined: Int = 0,
    val restoredAt: String? = null,
    val blockedAt: String = "",
)
