// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_title
import org.jetbrains.compose.resources.stringResource

// The emergency Shield Mode toggle: a prominent row that turns Twitch's automated lockdown on/off for one
// channel. The title reads destructive (red) when active — the single visual cue this row needs, so it never
// competes for accent with anything else on the page (Sleak: scarce accent). Every surface that can toggle
// Shield Mode (Moderation -> Desk, the single-channel Chat page, the multi-channel Chat lane) renders THIS ONE
// composable, wired to the same backend route (PATCH .../moderation/shield) via [ModerationApi.setShieldMode] —
// there is exactly one way to flip Shield Mode in the dashboard, never a second bespoke control per page.
//
// [title] defaults to the generic "Shield Mode" label (Moderation -> Desk, and the single-channel Chat page,
// where the page itself already names the channel); the multi-channel Chat lane passes the WATCHED channel's own
// display name instead, since one row exists per watched channel there and the title is what tells them apart.
@Composable
fun ShieldModeToggle(
    enabled: Boolean,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
    title: String = stringResource(Res.string.moderation_shield_title),
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Composed from two already-localized pieces (never a new hardcoded phrase): on the single-toggle pages
    // (Moderation -> Desk, single-channel Chat) this just reads "Shield Mode: Enable Shield Mode" — a little
    // redundant, but correct. It matters on the multi-channel Chat lane, where several of these rows render at
    // once (one per watched channel) with the SAME base action label ("Enable Shield Mode") — without [title]
    // folded in, two off channels would expose two identically-labelled buttons to assistive tech.
    val actionLabel: String =
        title + ": " + stringResource(
            if (enabled) Res.string.moderation_shield_disable_action
            else Res.string.moderation_shield_enable_action
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = title,
            style = typography.base,
            color = if (enabled) tokens.destructive else tokens.cardForeground,
            maxLines = 1,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { canManage ->
            TextButton(
                onClick = { onToggle(!enabled) },
                enabled = canManage,
                modifier = Modifier.semantics { contentDescription = actionLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (enabled) Res.string.moderation_shield_disable
                            else Res.string.moderation_shield_enable
                        ),
                    color = if (canManage) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

/** A muted, generic-copy notice for when Shield Mode's live-Twitch read/write failed here (missing scope / bot
 * not installed on this channel) — rendered INSTEAD of the toggle so the page never phantom-lies "off" when the
 * truth is "you can't see or control this here". */
@Composable
fun ShieldModeUnavailableNotice(message: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = message,
        style = typography.sm,
        color = tokens.mutedForeground,
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
    )
}
