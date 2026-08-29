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

// Proves the Kick card renders honest, state-driven copy rather than a generic Connected/Not-connected
// badge: a login-only connection (signed into the dashboard with Kick, never granted the platform
// connection) must show its own distinct badge, and a real MISSING_SCOPE backoff persisted by
// KickEventSubscriptionWorker (Status = needs_reauth) must render as the reconnect warning, not "Connected".
// Content is pinned to English via [AppEnvironment] so the assertions don't depend on the host locale.
@OptIn(ExperimentalTestApi::class)
class KickRowTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Test
    fun login_only_connection_renders_the_login_only_badge_not_connected() = runComposeUiTest {
        setContent {
            EnglishContent {
                KickRow(
                    connection =
                        ProviderConnection(
                            provider = "kick",
                            connected = false,
                            accountName = null,
                            needsReauth = false,
                            loginOnly = true,
                        ),
                    busy = false,
                    manage = ManageDecision.Allowed,
                    onConnect = {},
                    onDisconnect = {},
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Signed in only — chat not connected").fetchSemanticsNodes().isNotEmpty(),
            "expected the login-only badge, not a generic Connected/Not-connected status",
        )
    }

    @Test
    fun missing_scope_backoff_renders_the_real_reconnect_warning_not_connected() = runComposeUiTest {
        setContent {
            EnglishContent {
                KickRow(
                    connection =
                        ProviderConnection(
                            provider = "kick",
                            connected = false,
                            accountName = "streamer_kick",
                            needsReauth = true,
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
            onAllNodesWithText("Reconnect needed — Kick chat is paused").fetchSemanticsNodes().isNotEmpty(),
            "expected the real backoff warning to render, not a generic Connected/Not-connected status",
        )
    }

    @Test
    fun fully_connected_kick_renders_connected_not_a_backoff_or_login_only_badge() = runComposeUiTest {
        setContent {
            EnglishContent {
                KickRow(
                    connection =
                        ProviderConnection(
                            provider = "kick",
                            connected = true,
                            accountName = "streamer_kick",
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
            onAllNodesWithText("Connected as streamer_kick").fetchSemanticsNodes().isNotEmpty(),
            "expected the normal connected copy for a genuine platform connection",
        )
    }
}
