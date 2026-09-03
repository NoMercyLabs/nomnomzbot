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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.feature.admin.ui.EmptyLine
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.SpamDetection
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.spam_detection_confidence_high
import nomnomzbot.composeapp.generated.resources.spam_detection_confidence_low
import nomnomzbot.composeapp.generated.resources.spam_detection_confidence_medium
import nomnomzbot.composeapp.generated.resources.spam_detection_confidence_zero
import nomnomzbot.composeapp.generated.resources.spam_detection_outcome_delete_escalate
import nomnomzbot.composeapp.generated.resources.spam_detection_outcome_delete_queue
import nomnomzbot.composeapp.generated.resources.spam_detection_outcome_flag
import nomnomzbot.composeapp.generated.resources.spam_detection_outcome_none
import nomnomzbot.composeapp.generated.resources.spam_detection_overturn
import nomnomzbot.composeapp.generated.resources.spam_detection_overturned
import nomnomzbot.composeapp.generated.resources.spam_detection_would_have
import nomnomzbot.composeapp.generated.resources.spam_detections_dry_run_notice
import nomnomzbot.composeapp.generated.resources.spam_detections_empty
import nomnomzbot.composeapp.generated.resources.spam_review_queue_empty
import nomnomzbot.composeapp.generated.resources.spam_tier_established
import nomnomzbot.composeapp.generated.resources.spam_tier_known
import nomnomzbot.composeapp.generated.resources.spam_tier_newcomer
import nomnomzbot.composeapp.generated.resources.spam_tier_regular
import nomnomzbot.composeapp.generated.resources.spam_tier_semi_trusted
import nomnomzbot.composeapp.generated.resources.spam_tier_trusted
import nomnomzbot.composeapp.generated.resources.spam_tier_untrusted
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

/**
 * The verdict log, and the review queue that is a filtered view of it.
 *
 * <p>Both render the same row because they are the same fact seen twice: the queue is the subset a
 * human still has to decide on. Building them as two different components is how the two drift into
 * showing different things about one event.</p>
 *
 * Every row leads with the REASON rather than the verdict. A moderator scanning this needs to know why
 * the system did something before they can judge whether it was right, and "DeleteAndEscalate" tells
 * them nothing they can act on.
 */
@Composable
internal fun SpamDetectionsSection(
    detections: List<SpamDetection>,
    manage: ManageDecision,
    reviewQueueOnly: Boolean,
    onOverturn: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val rows: List<SpamDetection> =
        if (reviewQueueOnly) {
            detections.filter { it.outcome == "DeleteAndQueue" && it.overturnedAt == null }
        } else {
            detections
        }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (rows.isEmpty()) {
            EmptyLine(
                text =
                    stringResource(
                        if (reviewQueueOnly) Res.string.spam_review_queue_empty
                        else Res.string.spam_detections_empty
                    )
            )
            return@Column
        }

        // Shown once at the top rather than on every row: during the observation week EVERY row is a
        // counterfactual, and repeating that on each one would bury the rows themselves.
        if (rows.any { it.wasDryRun }) {
            Text(
                text = stringResource(Res.string.spam_detections_dry_run_notice),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }

        rows.forEachIndexed { index, detection ->
            if (index > 0) Separator()
            SpamDetectionRow(detection, manage, onOverturn)
        }
    }
}

@Composable
private fun SpamDetectionRow(
    detection: SpamDetection,
    manage: ManageDecision,
    onOverturn: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s2),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = detection.subjectDisplayName,
                style = typography.base,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(tierLabel(detection.tier)),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            Text(
                text = stringResource(confidenceLabel(detection.confidence)),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }

        // The reason, first — it is the only field that lets a moderator judge whether the call was
        // right, and it is the whole of SD7's promise that there are no black-box verdicts.
        Text(text = detection.reason, style = typography.sm, color = tokens.cardForeground)

        Text(
            text = detection.messageText,
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Text(
            text =
                if (detection.wasDryRun) {
                    stringResource(
                        Res.string.spam_detection_would_have,
                        stringResource(outcomeLabel(detection.wouldHaveBeen)),
                    )
                } else {
                    stringResource(outcomeLabel(detection.outcome))
                },
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        if (detection.overturnedAt != null) {
            Text(
                text = stringResource(Res.string.spam_detection_overturned),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Button(
                onClick = { onOverturn(detection.id) },
                enabled = manage is ManageDecision.Allowed,
                variant = ButtonVariant.Outline,
            ) {
                Text(text = stringResource(Res.string.spam_detection_overturn))
            }
        }
    }
}

/**
 * Backend enum names are deliberately NOT shown. "DeleteAndEscalate" and "SemiTrusted" are our
 * vocabulary; the person reading this is a streamer, and an unmapped value falls back to the mildest
 * wording rather than leaking the raw name onto the page.
 */
private fun outcomeLabel(outcome: String): StringResource =
    when (outcome) {
        "Flag" -> Res.string.spam_detection_outcome_flag
        "DeleteAndQueue" -> Res.string.spam_detection_outcome_delete_queue
        "DeleteAndEscalate" -> Res.string.spam_detection_outcome_delete_escalate
        else -> Res.string.spam_detection_outcome_none
    }

private fun confidenceLabel(confidence: String): StringResource =
    when (confidence) {
        "Low" -> Res.string.spam_detection_confidence_low
        "Medium" -> Res.string.spam_detection_confidence_medium
        "High" -> Res.string.spam_detection_confidence_high
        else -> Res.string.spam_detection_confidence_zero
    }

private fun tierLabel(tier: String): StringResource =
    when (tier) {
        "Newcomer" -> Res.string.spam_tier_newcomer
        "Known" -> Res.string.spam_tier_known
        "Regular" -> Res.string.spam_tier_regular
        "Trusted" -> Res.string.spam_tier_trusted
        "SemiTrusted" -> Res.string.spam_tier_semi_trusted
        "Established" -> Res.string.spam_tier_established
        else -> Res.string.spam_tier_untrusted
    }
