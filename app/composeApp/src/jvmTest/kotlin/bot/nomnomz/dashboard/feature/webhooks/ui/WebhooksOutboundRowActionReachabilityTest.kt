// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.webhooks.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * S-WEBHOOK-ACCENT: `OutboundRow` used to tint Edit, Test AND Reenable with `tokens.primary` in the same
 * action strip. Taking the accent off Test and Reenable must not have quietly taken anything else with it —
 * de-tinting is a color-only change, so every action stays a real, enabled, clickable control regardless of
 * which one keeps the accent. `WebhooksRowAccentScarcityGuardTest` proves the color shape structurally; this
 * proves the controls themselves still work once that color is gone.
 */
@OptIn(ExperimentalTestApi::class)
class WebhooksOutboundRowActionReachabilityTest {

    private fun disabledOutbound() =
        OutboundWebhook(
            id = "ep-1",
            name = "Alerts relay",
            fqdn = "hooks.example.com",
            isEnabled = false,
            disabledAt = "2026-09-01T00:00:00Z",
            disabledReason = "too many consecutive failures",
        )

    @Test
    fun test_and_reenable_stay_enabled_and_clickable_once_de_tinted() =
        runComposeUiTest {
            var testClicked = false
            var reenableClicked = false
            setContent {
                AppEnvironment(tag = "en") { NomNomzTheme {
                    OutboundRow(
                        ep = disabledOutbound(),
                        manage = ManageDecision.Allowed,
                        onToggle = {},
                        onEdit = {},
                        onReenable = { reenableClicked = true },
                        onRotateSecret = {},
                        onDeliveries = {},
                        onTest = { testClicked = true },
                        onDelete = {},
                    )
                } }
            }
            waitForIdle()

            onNodeWithContentDescription("Test").performClick()
            assertTrue(testClicked, "Test must still reach its handler after losing its accent tint")

            onNodeWithContentDescription("Re-enable").performClick()
            assertTrue(reenableClicked, "Reenable must still reach its handler after losing its accent tint")
        }
}
