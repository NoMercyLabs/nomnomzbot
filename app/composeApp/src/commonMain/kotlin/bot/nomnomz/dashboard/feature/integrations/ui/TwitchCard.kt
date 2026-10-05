// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.integrations_bot_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_bot_title
import nomnomzbot.composeapp.generated.resources.integrations_twitch_subtitle
import nomnomzbot.composeapp.generated.resources.integrations_twitch_title
import org.jetbrains.compose.resources.stringResource

// The ONE Twitch card on the Integrations screen: the Twitch app the bot signs in through and the bot
// account it signs in are one thing to the operator, so they share one surface. The bot account comes first
// and carries the card's single primary action (Connect); the app part below it is secondary (ghost Edit,
// outline Save). Internal (not private) so TwitchCardTest can mount it against a real credentials controller.
@Composable
internal fun TwitchCard(
    appController: TwitchAppCredentialsController,
    botConnected: Boolean,
    botAccountName: String?,
    botBusy: Boolean,
    manage: ManageDecision,
    onConnectBot: () -> Unit,
    onDisconnectBot: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card, RoundedCornerShape(tokens.radius.lg))
            .border(width = spacing.s0_5 / 2, color = tokens.border, shape = RoundedCornerShape(tokens.radius.lg))
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
            Text(
                text = stringResource(Res.string.integrations_twitch_title),
                style = typography.xl,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.integrations_twitch_subtitle),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }

        IntegrationRow(
            title = stringResource(Res.string.integrations_bot_title),
            subtitle = stringResource(Res.string.integrations_bot_subtitle),
            connected = botConnected,
            accountName = botAccountName,
            needsReauth = false,
            busy = botBusy,
            manage = manage,
            onConnect = onConnectBot,
            // Disconnect is admin-gated server-side (the ManageGate hides it for non-admins). Disconnecting then
            // connecting a different account is how the operator CHANGES the bot.
            onDisconnect = if (botConnected) onDisconnectBot else null,
        )

        Separator()

        TwitchAppCredentialsSection(controller = appController, manage = manage)
    }
}
