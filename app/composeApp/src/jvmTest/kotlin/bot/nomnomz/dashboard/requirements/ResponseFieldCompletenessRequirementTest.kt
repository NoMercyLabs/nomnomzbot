// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.requirements

import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.ChannelAsset
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CurrencyLedgerEntry
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.EarningRule
import bot.nomnomz.dashboard.core.network.GameSession
import bot.nomnomz.dashboard.core.network.Giveaway
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.MarketplaceItem
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.RedemptionSummary
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.SupporterEvent
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.UserModerationContext
import bot.nomnomz.dashboard.core.network.WidgetSummary
import kotlin.test.Test
import kotlin.test.fail
import kotlinx.serialization.KSerializer
import kotlinx.serialization.descriptors.elementNames

// REQUIREMENT: every field the backend RETURNS must be carried by the dashboard DTO, so nothing the API sends
// is silently dropped before it can reach the UI.
//
// This is the exact inverse of ApiContractTest. That test asserts `dtoFields - backendProps == ∅` (no drift —
// a DTO must not invent fields). This one asserts `backendProps - dtoFields == ∅` (no loss — a DTO must not
// omit fields). A field on the backend schema that the Kotlin DTO lacks deserializes to nothing: the value the
// backend computed and sent is invisible to the entire frontend. That omission is the backlog item.
//
// Scope note: this checks DTO field CARRIAGE (the reflection-visible wire contract), which is the layer the
// project's "every response field shown" rule first depends on. A field present on the DTO but not yet painted
// into a Compose screen is a separate, UI-level gap that a source/reflection scan cannot see; it is out of
// scope here by construction.
class ResponseFieldCompletenessRequirementTest {

    // (Kotlin response DTO serializer, backend schema name, human label for the failure line).
    // Schema names match ApiContractTest / the committed OpenAPI snapshot; they are verified to exist by the
    // test itself (a missing name is reported, never silently skipped).
    private val responseContracts: List<Triple<KSerializer<*>, String, String>> =
        listOf(
            Triple(CommunityMember.serializer(), "CommunityUserDto", "community member activity + stats"),
            Triple(CatalogItem.serializer(), "CatalogItemDto", "store catalog item config"),
            Triple(CurrencyLedgerEntry.serializer(), "CurrencyLedgerEntryDto", "currency ledger audit trail"),
            Triple(EarningRule.serializer(), "EarningRuleDto", "currency earning rule"),
            Triple(ModerationRule.serializer(), "ModerationRuleDetail", "moderation filter rule"),
            Triple(RewardSummary.serializer(), "RewardDetail", "channel-point reward"),
            Triple(WidgetSummary.serializer(), "WidgetDetail", "widget (runtime health incl.)"),
            Triple(Giveaway.serializer(), "GiveawayDto", "giveaway config + lifecycle"),
            Triple(GameSession.serializer(), "GameSessionDto", "live game session"),
            Triple(MarketplaceItem.serializer(), "MarketplaceItemDto", "marketplace item"),
            Triple(RedemptionSummary.serializer(), "RedemptionListItem", "reward redemption"),
            Triple(LeaderboardEntry.serializer(), "LeaderboardEntryDto", "leaderboard entry"),
            Triple(TtsVoice.serializer(), "TtsVoiceDto", "TTS voice"),
            Triple(SupporterEvent.serializer(), "SupporterEventDto", "supporter event"),
            Triple(ChannelAsset.serializer(), "ChannelAssetDto", "overlay/widget media asset"),
            Triple(BannedUser.serializer(), "BannedUserDto", "banned viewer"),
            Triple(ModLogEntry.serializer(), "ModLogEntryDto", "moderator action-log entry"),
            Triple(UserModerationContext.serializer(), "UserModerationContextDto", "viewer moderation rap sheet"),
            Triple(DashboardStats.serializer(), "DashboardStatsDto", "dashboard home stats"),
        )

    @Test
    fun every_response_dto_carries_every_field_the_backend_returns() {
        val problems: MutableList<String> = mutableListOf()
        var complete = 0

        for ((serializer, schemaName, label) in responseContracts) {
            val backend: Set<String>? = BackendContract.backendProperties(schemaName)
            if (backend == null) {
                problems += "$schemaName: no such schema in the OpenAPI document (fix the mapping)"
                continue
            }
            val dtoFields: Set<String> = serializer.descriptor.elementNames.toSet()
            val dropped: Set<String> = backend - dtoFields
            if (dropped.isEmpty()) {
                complete++
            } else {
                problems +=
                    "$schemaName ($label): DTO drops ${dropped.sorted()} — the backend returns these but " +
                        "the dashboard cannot surface them (DTO carries ${dtoFields.sorted()})"
            }
        }

        println("[response-completeness] $complete/${responseContracts.size} response DTOs carry every backend field")

        if (problems.isNotEmpty()) {
            fail(
                "The dashboard drops response fields the backend returns (a complete reflection must carry " +
                    "every field):\n" + problems.joinToString("\n") { "  • $it" }
            )
        }
    }
}
