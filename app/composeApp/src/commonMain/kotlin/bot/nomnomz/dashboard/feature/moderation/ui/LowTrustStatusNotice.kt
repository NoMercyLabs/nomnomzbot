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
import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import androidx.compose.material3.Text
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_lowtrust_monitored
import nomnomzbot.composeapp.generated.resources.moderation_lowtrust_monitored_hint
import nomnomzbot.composeapp.generated.resources.moderation_lowtrust_restricted
import nomnomzbot.composeapp.generated.resources.moderation_lowtrust_restricted_hint
import org.jetbrains.compose.resources.stringResource

/** Wire values of the backend `lowTrustStatus` (Twitch's suspicious-user flag). */
internal const val LOW_TRUST_RESTRICTED: String = "restricted"
internal const val LOW_TRUST_MONITORED: String = "active_monitoring"

/** Moderation-queue `source` of a message held back because Twitch flagged its sender. */
internal const val SUSPICIOUS_USER_SOURCE: String = "suspicioususer"

/** The status badge alone (restricted reads destructive, monitored secondary); nothing for "none". */
@Composable
internal fun LowTrustStatusBadge(status: String) {
    val typography = LocalTypography.current
    when (status) {
        LOW_TRUST_RESTRICTED ->
            Badge(variant = BadgeVariant.Destructive) {
                Text(text = stringResource(Res.string.moderation_lowtrust_restricted), style = typography.xs)
            }
        LOW_TRUST_MONITORED ->
            Badge(variant = BadgeVariant.Secondary) {
                Text(text = stringResource(Res.string.moderation_lowtrust_monitored), style = typography.xs)
            }
    }
}

/** The badge plus a one-line explanation of what Twitch's flag means for this viewer; nothing for "none". */
@Composable
internal fun LowTrustStatusNotice(status: String) {
    if (status != LOW_TRUST_RESTRICTED && status != LOW_TRUST_MONITORED) return
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        LowTrustStatusBadge(status = status)
        Text(
            text =
                stringResource(
                    if (status == LOW_TRUST_RESTRICTED) {
                        Res.string.moderation_lowtrust_restricted_hint
                    } else {
                        Res.string.moderation_lowtrust_monitored_hint
                    }
                ),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
    }
}
