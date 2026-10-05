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
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_shield_confirm_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_confirm_cancel
import nomnomzbot.composeapp.generated.resources.moderation_shield_confirm_message
import nomnomzbot.composeapp.generated.resources.moderation_shield_confirm_message_for_channel
import nomnomzbot.composeapp.generated.resources.moderation_shield_confirm_title
import nomnomzbot.composeapp.generated.resources.moderation_shield_confirm_title_for_channel
import nomnomzbot.composeapp.generated.resources.moderation_shield_description
import nomnomzbot.composeapp.generated.resources.moderation_shield_description_for_channel
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_state_off
import nomnomzbot.composeapp.generated.resources.moderation_shield_state_on
import nomnomzbot.composeapp.generated.resources.moderation_shield_title
import nomnomzbot.composeapp.generated.resources.moderation_shield_title_for_channel
import org.jetbrains.compose.resources.stringResource

// The emergency Shield Mode control: one row that turns Twitch's chat lockdown on or off for ONE channel. Every
// surface that can flip Shield Mode (Moderation -> Desk, the single-channel Chat page, the multi-channel Chat
// lane) renders THIS composable, wired to the same PATCH .../moderation/shield route via
// [ModerationApi.setShieldMode] — there is exactly one way to flip Shield Mode in the dashboard.
//
// Safety contract (incident 2026-10-05: a row that showed only a channel name and "Enable" locked down another
// streamer's live chat with one click, because nothing on it said Shield Mode):
//  * the row always says "Shield Mode", names the channel when the page does not ([channelName]), and carries one
//    plain line on the effect;
//  * the current state is spelled out (On / Off), never left to the button label alone;
//  * turning it ON opens a destructive confirm that names the channel and the effect — [onToggle] fires only on
//    confirm, never on the first click; cancel fires nothing;
//  * turning it OFF is immediate — ending a lockdown must never wait on a second click.
// Visual weight: a quiet destructive-ghost action, not a primary button — Shield Mode is rare, so it must not be
// the most eye-catching thing on a page whose job is reading chat (Sleak: scarce accent, one primary per group).
@Composable
fun ShieldModeToggle(
    enabled: Boolean,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
    channelName: String? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var confirmOpen: Boolean by remember { mutableStateOf(false) }

    val title: String =
        if (channelName == null) stringResource(Res.string.moderation_shield_title)
        else stringResource(Res.string.moderation_shield_title_for_channel, channelName)
    val description: String =
        if (channelName == null) stringResource(Res.string.moderation_shield_description)
        else stringResource(Res.string.moderation_shield_description_for_channel, channelName)
    val stateLabel: String =
        stringResource(if (enabled) Res.string.moderation_shield_state_on else Res.string.moderation_shield_state_off)
    // Folds the title into the accessible name: the multi-channel Chat lane renders one row per watched channel
    // with the SAME action label, so without the title two off channels would expose identical buttons.
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
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = title,
                style = typography.base,
                color = if (enabled) tokens.destructive else tokens.cardForeground,
                maxLines = 1,
            )
            Text(text = description, style = typography.sm, color = tokens.mutedForeground)
        }
        Badge(variant = if (enabled) BadgeVariant.Destructive else BadgeVariant.Outline) {
            Text(text = stateLabel, maxLines = 1)
        }
        ManageGate(decision = manage) { canManage ->
            if (enabled) {
                OutlinedButton(
                    onClick = { onToggle(false) },
                    enabled = canManage,
                    modifier = Modifier.semantics { contentDescription = actionLabel },
                ) {
                    Text(text = stringResource(Res.string.moderation_shield_disable), maxLines = 1)
                }
            } else {
                Button(
                    onClick = { confirmOpen = true },
                    enabled = canManage,
                    variant = ButtonVariant.DestructiveGhost,
                    size = ButtonSize.Sm,
                    modifier = Modifier.semantics { contentDescription = actionLabel },
                ) {
                    Text(text = stringResource(Res.string.moderation_shield_enable), maxLines = 1)
                }
            }
        }
    }

    if (confirmOpen) {
        ConfirmDialog(
            title =
                if (channelName == null) stringResource(Res.string.moderation_shield_confirm_title)
                else stringResource(Res.string.moderation_shield_confirm_title_for_channel, channelName),
            message =
                if (channelName == null) stringResource(Res.string.moderation_shield_confirm_message)
                else stringResource(Res.string.moderation_shield_confirm_message_for_channel, channelName),
            confirmLabel = stringResource(Res.string.moderation_shield_confirm_action),
            dismissLabel = stringResource(Res.string.moderation_shield_confirm_cancel),
            destructive = true,
            onConfirm = {
                confirmOpen = false
                onToggle(true)
            },
            onDismiss = { confirmOpen = false },
        )
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
