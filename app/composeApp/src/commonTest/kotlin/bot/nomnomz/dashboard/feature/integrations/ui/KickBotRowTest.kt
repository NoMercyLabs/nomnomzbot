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

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.integrations.state.ProviderConnection
import kotlin.test.Test
import kotlin.test.assertTrue

// Proves the Kick bot account row shows the truthful connect affordance when no kick_bot connection
// exists yet, and a real connected state (the bot's OWN account name) once one is registered — never a
// faked success. Content is pinned to English via [AppEnvironment] so assertions don't depend on locale.
@OptIn(ExperimentalTestApi::class)
class KickBotRowTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Test
    fun no_bot_connection_shows_the_connect_affordance_not_connected() = runComposeUiTest {
        setContent {
            EnglishContent {
                KickBotRow(
                    connection = null,
                    busy = false,
                    manage = ManageDecision.Allowed,
                    onConnect = {},
                    onDisconnect = {},
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Kick bot account").fetchSemanticsNodes().isNotEmpty(),
            "expected the Kick bot account row title",
        )
        assertTrue(
            onAllNodesWithText("Not connected").fetchSemanticsNodes().isNotEmpty(),
            "no kick_bot connection registered — must never claim connected",
        )
        assertTrue(
            onAllNodesWithText("Connect").fetchSemanticsNodes().isNotEmpty(),
            "expected the connect affordance while no bot account is registered",
        )
    }

    @Test
    fun registered_bot_connection_shows_connected_with_its_own_account_name() = runComposeUiTest {
        setContent {
            EnglishContent {
                KickBotRow(
                    connection =
                        ProviderConnection(
                            provider = "kick_bot",
                            connected = true,
                            accountName = "MyStreamBot",
                            needsReauth = false,
                            loginOnly = false,
                        ),
                    busy = false,
                    manage = ManageDecision.Allowed,
                    onConnect = {},
                    onDisconnect = {},
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Connected as MyStreamBot").fetchSemanticsNodes().isNotEmpty(),
            "a registered kick_bot connection must render its OWN account name as connected",
        )
        assertTrue(
            onAllNodesWithText("Disconnect").fetchSemanticsNodes().isNotEmpty(),
            "a connected bot account must offer disconnect",
        )
    }
}
