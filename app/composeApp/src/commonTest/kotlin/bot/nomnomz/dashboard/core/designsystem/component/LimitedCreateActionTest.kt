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

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ResourceUsage
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// S-BUDGETS-b3 done-when coverage: [LimitedCreateAction] is the ONE shared warn-before-refuse wrapper every
// limited-resource create surface (commands, timers, event responses) uses — proven here at the shared-component
// level rather than duplicated per screen. Every assertion mounts the real composable and reads the rendered
// semantics tree, never the state layer that fed it.
@OptIn(ExperimentalTestApi::class)
class LimitedCreateActionTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    private fun nearFree(currentCount: Long, limit: Long, displayName: String = "custom commands"): ResourceUsage =
        ResourceUsage(
            limitKey = "custom_commands",
            classWire = 1, // NearFree
            displayName = displayName,
            currentCount = currentCount,
            limit = limit,
            safetyBaseline = limit,
        )

    @Test
    fun at_the_limit_the_create_control_renders_disabled_with_its_reason() = runComposeUiTest {
        var lastEnabled: Boolean? = null
        setContent {
            EnglishContent {
                LimitedCreateAction(usage = nearFree(currentCount = 100, limit = 100)) { enabled ->
                    lastEnabled = enabled
                    Text(if (enabled) "New command (enabled)" else "New command (disabled)")
                }
            }
        }

        // The control itself is disabled — never enabled-then-failing.
        assertEquals(false, lastEnabled, "the create affordance must be disabled at the limit")
        assertTrue(
            onAllNodesWithText("New command (disabled)").fetchSemanticsNodes().isNotEmpty(),
            "the disabled control must still render (never silently missing)",
        )
        // The reason names the limit in plain language, using the endpoint's real displayName.
        assertTrue(
            onAllNodesWithText("custom commands", substring = true).fetchSemanticsNodes().isNotEmpty(),
            "the disabled reason must name the resource that's at its limit",
        )
    }

    @Test
    fun a_near_free_at_limit_message_carries_no_upgrade_or_upsell_copy() = runComposeUiTest {
        setContent {
            EnglishContent {
                LimitedCreateAction(usage = nearFree(currentCount = 100, limit = 100)) { enabled ->
                    Text(if (enabled) "New" else "New")
                }
            }
        }

        val forbidden: List<String> = listOf("Upgrade", "upgrade", "Pro tier", "Choose plan", "Manage subscription")
        forbidden.forEach { phrase ->
            assertTrue(
                onAllNodesWithText(phrase, substring = true).fetchSemanticsNodes().isEmpty(),
                "a NEAR_FREE at-limit message must never carry upsell copy, but found '$phrase'",
            )
        }
    }

    @Test
    fun approaching_the_limit_renders_the_real_remaining_and_total_counts() = runComposeUiTest {
        var lastEnabled: Boolean? = null
        setContent {
            EnglishContent {
                // 97 of 100 used → 3 remaining, at the approaching floor.
                LimitedCreateAction(usage = nearFree(currentCount = 97, limit = 100)) { enabled ->
                    lastEnabled = enabled
                    Text("New")
                }
            }
        }

        // Approaching never blocks the control itself — only warns ahead of the refusal.
        assertEquals(true, lastEnabled, "approaching the limit must not disable the create affordance")
        onNodeWithText("3 of 100 left").assertExists("the notice must render the endpoint's real remaining/limit numbers")
    }

    @Test
    fun comfortably_below_the_limit_renders_no_notice_and_a_fully_enabled_control() = runComposeUiTest {
        var lastEnabled: Boolean? = null
        setContent {
            EnglishContent {
                LimitedCreateAction(usage = nearFree(currentCount = 5, limit = 100)) { enabled ->
                    lastEnabled = enabled
                    Text("New")
                }
            }
        }

        assertEquals(true, lastEnabled)
        assertTrue(
            onAllNodesWithText("left", substring = true).fetchSemanticsNodes().isEmpty(),
            "a resource nowhere near its limit must render no approaching notice",
        )
    }

    @Test
    fun a_missing_usage_report_never_blocks_the_create_affordance() = runComposeUiTest {
        var lastEnabled: Boolean? = null
        setContent {
            EnglishContent {
                LimitedCreateAction(usage = null) { enabled ->
                    lastEnabled = enabled
                    Text("New")
                }
            }
        }

        assertEquals(true, lastEnabled, "unknown usage (endpoint not yet answered) must never block create")
    }
}
