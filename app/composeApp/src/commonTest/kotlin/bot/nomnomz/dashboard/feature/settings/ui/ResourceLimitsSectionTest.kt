// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ResourceUsage
import kotlin.test.Test
import kotlin.test.assertTrue

// S-BUDGETS-b2 done-when coverage: every assertion below mounts the real composable and reads what actually
// rendered on screen, not the state layer that fed it — the same class of regression the setup-wizard credential
// field test guards against (state can be correct while the composable silently fails to reflect it).
//
// Content is pinned to English via [AppEnvironment] rather than relying on the JVM's default locale: this repo
// resolves `stringResource` against whichever locale the running machine happens to report, so a Dutch-default
// dev box renders the values-nl/ copy and the English literal assertions below would fail for a reason that
// has nothing to do with the composable under test.
@OptIn(ExperimentalTestApi::class)
class ResourceLimitsSectionTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    private fun nearFree(currentCount: Long, limit: Long): ResourceUsage =
        ResourceUsage(
            limitKey = "custom_commands",
            classWire = 1, // NearFree
            displayName = "Custom commands",
            currentCount = currentCount,
            limit = limit,
            safetyBaseline = limit,
        )

    private fun costDriving(currentCount: Long, limit: Long): ResourceUsage =
        ResourceUsage(
            limitKey = "tts_characters",
            classWire = 0, // CostDriving
            displayName = "TTS characters",
            currentCount = currentCount,
            limit = limit,
            safetyBaseline = 0,
        )

    @Test
    fun renders_the_endpoints_real_used_and_limit_numbers_as_x_of_y() = runComposeUiTest {
        setContent {
            EnglishContent {
                ResourceLimitsSection(
                    items = listOf(nearFree(currentCount = 7, limit = 200)),
                    loadFailed = false,
                    isSelfHost = false,
                    tierDisplayName = "Pro",
                )
            }
        }

        // The exact numbers the fake endpoint returned must appear verbatim — never a client-recomputed value.
        assertTrue(
            onAllNodesWithText("7 of 200").fetchSemanticsNodes().isNotEmpty(),
            "expected the rendered row to read '7 of 200' straight from the endpoint's real numbers",
        )
    }

    @Test
    fun a_near_free_resource_never_renders_upgrade_or_upsell_copy() = runComposeUiTest {
        setContent {
            EnglishContent {
                ResourceLimitsSection(
                    items = listOf(nearFree(currentCount = 3, limit = 100)),
                    loadFailed = false,
                    isSelfHost = false,
                    tierDisplayName = "Pro",
                )
            }
        }

        val forbidden: List<String> = listOf("Upgrade", "upgrade", "Pro tier", "Choose plan", "Manage subscription")
        forbidden.forEach { phrase ->
            assertTrue(
                onAllNodesWithText(phrase, substring = true).fetchSemanticsNodes().isEmpty(),
                "a NEAR_FREE-only report must never render upsell copy, but found '$phrase'",
            )
        }
    }

    @Test
    fun a_cost_driving_resource_may_render_its_tier_name() = runComposeUiTest {
        setContent {
            EnglishContent {
                ResourceLimitsSection(
                    items = listOf(costDriving(currentCount = 500, limit = 10_000)),
                    loadFailed = false,
                    isSelfHost = false,
                    tierDisplayName = "Pro",
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Pro tier").fetchSemanticsNodes().isNotEmpty(),
            "a COST_DRIVING group is allowed to name the active tier",
        )
        assertTrue(
            onAllNodesWithText("500 of 10000").fetchSemanticsNodes().isNotEmpty(),
            "expected the cost-driving row's real used/limit numbers to render",
        )
    }

    @Test
    fun self_host_renders_no_commercial_ceiling_for_a_cost_driving_resource() = runComposeUiTest {
        setContent {
            EnglishContent {
                ResourceLimitsSection(
                    // Self-host resolves a COST_DRIVING limit to unlimited (-1) on the backend.
                    items = listOf(costDriving(currentCount = 500, limit = -1)),
                    loadFailed = false,
                    isSelfHost = true,
                    tierDisplayName = "",
                )
            }
        }

        assertTrue(
            onAllNodesWithText("500 of unlimited").fetchSemanticsNodes().isNotEmpty(),
            "self-host must render its real unlimited limit, never a numeric ceiling",
        )
        assertTrue(
            onAllNodesWithText("tier", substring = true).fetchSemanticsNodes().isEmpty(),
            "self-host must show no tier / commercial-ceiling affordance at all",
        )
    }

    @Test
    fun a_failed_load_renders_differently_from_a_legitimately_empty_report() = runComposeUiTest {
        setContent {
            EnglishContent {
                ResourceLimitsSection(
                    items = emptyList(),
                    loadFailed = true,
                    isSelfHost = false,
                    tierDisplayName = "Pro",
                )
            }
        }
        val errorNodes = onAllNodesWithText("Couldn't load resource limits").fetchSemanticsNodes()
        assertTrue(errorNodes.isNotEmpty(), "a failed fetch must render the 'could not load' message")
        assertTrue(
            onAllNodesWithText("Nothing is limited on this channel right now.").fetchSemanticsNodes().isEmpty(),
            "a failed fetch must NOT render the same copy as a legitimately empty report",
        )
    }

    @Test
    fun a_legitimately_empty_report_renders_the_empty_message_not_the_error_message() = runComposeUiTest {
        setContent {
            EnglishContent {
                ResourceLimitsSection(
                    items = emptyList(),
                    loadFailed = false,
                    isSelfHost = false,
                    tierDisplayName = "Pro",
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Nothing is limited on this channel right now.").fetchSemanticsNodes().isNotEmpty(),
            "an empty (not failed) report must render the honest empty state",
        )
        assertTrue(
            onAllNodesWithText("Couldn't load resource limits").fetchSemanticsNodes().isEmpty(),
            "an empty (not failed) report must NOT render the error message",
        )
    }
}
