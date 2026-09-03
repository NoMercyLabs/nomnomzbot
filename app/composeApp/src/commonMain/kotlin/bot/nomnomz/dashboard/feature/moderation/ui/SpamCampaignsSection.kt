// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.FollowBotBlockEntry
import bot.nomnomz.dashboard.core.network.SpamCampaign
import bot.nomnomz.dashboard.feature.admin.ui.EmptyLine
import kotlin.math.roundToInt
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.spam_campaign_counts
import nomnomzbot.composeapp.generated.resources.spam_campaign_not_shared
import nomnomzbot.composeapp.generated.resources.spam_campaign_reversed
import nomnomzbot.composeapp.generated.resources.spam_campaign_strangers
import nomnomzbot.composeapp.generated.resources.spam_campaign_verdict_campaign
import nomnomzbot.composeapp.generated.resources.spam_campaign_verdict_community
import nomnomzbot.composeapp.generated.resources.spam_campaign_verdict_watching
import nomnomzbot.composeapp.generated.resources.spam_campaigns_empty
import nomnomzbot.composeapp.generated.resources.spam_follow_block_examined
import nomnomzbot.composeapp.generated.resources.spam_follow_block_restore
import nomnomzbot.composeapp.generated.resources.spam_follow_block_restored
import nomnomzbot.composeapp.generated.resources.spam_follow_blocks_empty
import nomnomzbot.composeapp.generated.resources.spam_follow_indicator_generated_handle
import nomnomzbot.composeapp.generated.resources.spam_follow_indicator_known_bot
import nomnomzbot.composeapp.generated.resources.spam_follow_indicator_oscillation
import nomnomzbot.composeapp.generated.resources.spam_follow_indicator_zero_history
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

/**
 * Coordinated groups.
 *
 * Shows the two counts the verdict actually turned on — how many accounts posted the phrase and how
 * many of them were strangers — because those are what let an operator judge whether the call was
 * right. A row that only said "campaign" would be a verdict with no working.
 */
@Composable
internal fun SpamCampaignsSection(campaigns: List<SpamCampaign>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (campaigns.isEmpty()) {
            EmptyLine(text = stringResource(Res.string.spam_campaigns_empty))
            return@Column
        }

        campaigns.forEachIndexed { index, campaign ->
            if (index > 0) Separator()

            Column(
                modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s2),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = stringResource(verdictLabel(campaign.verdict)),
                    style = typography.base,
                    color = tokens.cardForeground,
                )
                Text(
                    text = campaign.skeleton,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Text(
                    text =
                        stringResource(
                            Res.string.spam_campaign_counts,
                            campaign.qualificationCount,
                            campaign.actionableCount,
                            campaign.actionedCount,
                        ),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Text(
                    text =
                        stringResource(
                            Res.string.spam_campaign_strangers,
                            (campaign.noStandingShare * 100).roundToInt(),
                        ),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )

                campaign.reversalReason?.let { reason ->
                    Text(
                        text = stringResource(Res.string.spam_campaign_reversed, reason),
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )
                }

                // Surfaced because it is a promise being kept, not a detail: a phrase any regular
                // posted is never sent to other servers, and the operator can see that it wasn't.
                if (!campaign.mayContributeToNetwork) {
                    Text(
                        text = stringResource(Res.string.spam_campaign_not_shared),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

/**
 * Follow-bot blocks.
 *
 * Every row shows its own evidence and the size of the sweep it came from. Restoring is per WAVE
 * rather than per account: a misread viral moment is hundreds of people, and a recovery path that
 * needs hundreds of clicks is one nobody uses.
 */
@Composable
internal fun FollowBotBlocksSection(
    blocks: List<FollowBotBlockEntry>,
    manage: ManageDecision,
    onRestoreBatch: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (blocks.isEmpty()) {
            EmptyLine(text = stringResource(Res.string.spam_follow_blocks_empty))
            return@Column
        }

        blocks
            .groupBy { it.batchId }
            .forEach { (batchId, wave) ->
                Separator()

                Text(
                    text =
                        stringResource(
                            Res.string.spam_follow_block_examined,
                            wave.firstOrNull()?.batchExamined ?: 0,
                        ),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )

                wave.forEach { block ->
                    Column(
                        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s1),
                        verticalArrangement = Arrangement.spacedBy(spacing.s1),
                    ) {
                        Text(
                            text = block.subjectUsername,
                            style = typography.base,
                            color = tokens.cardForeground,
                        )
                        block.indicators
                            .split(',')
                            .filter { it.isNotBlank() }
                            .forEach { indicator ->
                                Text(
                                    text = stringResource(indicatorLabel(indicator.trim())),
                                    style = typography.sm,
                                    color = tokens.mutedForeground,
                                )
                            }
                    }
                }

                if (wave.any { it.restoredAt != null }) {
                    Text(
                        text = stringResource(Res.string.spam_follow_block_restored),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                } else {
                    Button(
                        onClick = { onRestoreBatch(batchId) },
                        enabled = manage is ManageDecision.Allowed,
                        variant = ButtonVariant.Outline,
                    ) {
                        Text(text = stringResource(Res.string.spam_follow_block_restore))
                    }
                }
            }
    }
}

private fun verdictLabel(verdict: String): StringResource =
    when (verdict) {
        "Campaign" -> Res.string.spam_campaign_verdict_campaign
        "CommunityPattern" -> Res.string.spam_campaign_verdict_community
        else -> Res.string.spam_campaign_verdict_watching
    }

/**
 * An unmapped indicator falls back to the vaguest wording rather than printing the raw enum name. The
 * person reading this is a streamer deciding whether a block was fair.
 */
private fun indicatorLabel(indicator: String): StringResource =
    when (indicator) {
        "KnownBotId" -> Res.string.spam_follow_indicator_known_bot
        "GeneratedHandlePattern" -> Res.string.spam_follow_indicator_generated_handle
        "FollowUnfollowOscillation" -> Res.string.spam_follow_indicator_oscillation
        else -> Res.string.spam_follow_indicator_zero_history
    }
