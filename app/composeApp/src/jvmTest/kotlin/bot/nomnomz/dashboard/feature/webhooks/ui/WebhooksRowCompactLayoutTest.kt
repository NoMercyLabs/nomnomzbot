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

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.width
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.InboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * S-UX-EMPTY-AND-COMPACT (part B): [InboundRow] and [OutboundRow] laid their name/target column and their
 * whole action-button strip out in one fixed [androidx.compose.foundation.layout.Row] with no width branch
 * at all — at Compact width the button strip crowded the row (not just the name, which an earlier sweep
 * already fixed). Both rows now measure their own width via `BoxWithConstraints` and, below
 * [bot.nomnomz.dashboard.core.designsystem.theme.Breakpoints.Wide], stack the actions in their own row
 * underneath the details instead of squeezing everything into one line.
 *
 * Proves three things per row, for both rows: (1) at Compact width every action's right edge stays inside
 * the row's own bounds — no horizontal overflow, (2) an action buried in that stacked strip is still a
 * real, clickable target — "reachable" means more than merely present in the tree, and (3) at Wide width
 * the row still renders as a single line (unchanged from before this slice).
 */
@OptIn(ExperimentalTestApi::class)
class WebhooksRowCompactLayoutTest {

    private val compactWidth = 360.dp
    private val wideWidth = 1000.dp
    private val containerTag = "row-container"

    private fun outbound() =
        OutboundWebhook(
            id = "ep-1",
            name = "Alerts relay",
            fqdn = "hooks.example.com",
            isEnabled = true,
        )

    private fun inbound() =
        InboundWebhook(id = "in-1", name = "Ko-fi receipts", adapter = "Kofi", ingestUrl = "https://bot/hooks/in-1")

    // The action's own right edge must not exceed the container's right edge (by more than a hairline
    // rounding tolerance) — an action poking past the row's own measured width is exactly the overflow
    // this slice fixes, not merely a visual guess from reading the source.
    private fun assertWithinContainer(container: SemanticsNode, action: SemanticsNode, label: String) {
        val overflowPx: Float = action.boundsInRoot.right - container.boundsInRoot.right
        assertTrue(overflowPx <= 1f, "'$label' overflows its row by ${overflowPx}px at Compact width")
    }

    @Test
    fun outbound_row_stacks_actions_below_the_details_at_compact_width_with_no_overflow() =
        runComposeUiTest {
            var deleteClicked = false
            setContent {
                AppEnvironment(tag = "en") { NomNomzTheme {
                    Box(modifier = Modifier.width(compactWidth).testTag(containerTag)) {
                        OutboundRow(
                            ep = outbound(),
                            manage = ManageDecision.Allowed,
                            onToggle = {},
                            onEdit = {},
                            onReenable = {},
                            onRotateSecret = {},
                            onDeliveries = {},
                            onTest = {},
                            onDelete = { deleteClicked = true },
                        )
                    }
                } }
            }
            waitForIdle()

            val container: SemanticsNode = onNodeWithTag(containerTag).fetchSemanticsNode()
            val editNode: SemanticsNode = onNodeWithContentDescription("Edit").fetchSemanticsNode()
            val deleteNode: SemanticsNode = onNodeWithContentDescription("Delete").fetchSemanticsNode()

            assertWithinContainer(container, editNode, "Edit")
            assertWithinContainer(container, deleteNode, "Delete")

            // Reachable, not merely present: a genuine click still reaches the handler through the stacked layout.
            onNodeWithContentDescription("Delete").performClick()
            assertTrue(deleteClicked, "Delete action must still be clickable once it moves to its own row at Compact width")
        }

    @Test
    fun outbound_row_stays_a_single_line_at_wide_width() =
        runComposeUiTest {
            setContent {
                AppEnvironment(tag = "en") { NomNomzTheme {
                    Box(modifier = Modifier.width(wideWidth).testTag(containerTag)) {
                        OutboundRow(
                            ep = outbound(),
                            manage = ManageDecision.Allowed,
                            onToggle = {},
                            onEdit = {},
                            onReenable = {},
                            onRotateSecret = {},
                            onDeliveries = {},
                            onTest = {},
                            onDelete = {},
                        )
                    }
                } }
            }
            waitForIdle()

            val editNode: SemanticsNode = onNodeWithContentDescription("Edit").fetchSemanticsNode()
            // At Wide width the action sits within the header band near the container's own top, not pushed
            // down onto a second row underneath a full-width details column.
            val container: SemanticsNode = onNodeWithTag(containerTag).fetchSemanticsNode()
            val verticalOffset: Float = editNode.boundsInRoot.top - container.boundsInRoot.top
            assertTrue(
                verticalOffset < 24f,
                "expected Edit to sit in the same top row as the details at Wide width, offset=$verticalOffset",
            )
        }

    @Test
    fun inbound_row_stacks_actions_below_the_details_at_compact_width_with_no_overflow() =
        runComposeUiTest {
            var deleteClicked = false
            setContent {
                AppEnvironment(tag = "en") { NomNomzTheme {
                    Box(modifier = Modifier.width(compactWidth).testTag(containerTag)) {
                        InboundRow(
                            ep = inbound(),
                            pipelines = emptyList(),
                            manage = ManageDecision.Allowed,
                            onToggle = {},
                            onEdit = {},
                            onRotate = {},
                            onDelete = { deleteClicked = true },
                        )
                    }
                } }
            }
            waitForIdle()

            val container: SemanticsNode = onNodeWithTag(containerTag).fetchSemanticsNode()
            val editNode: SemanticsNode = onNodeWithContentDescription("Edit").fetchSemanticsNode()
            val deleteNode: SemanticsNode = onNodeWithContentDescription("Delete").fetchSemanticsNode()

            assertWithinContainer(container, editNode, "Edit")
            assertWithinContainer(container, deleteNode, "Delete")

            onNodeWithContentDescription("Delete").performClick()
            assertTrue(deleteClicked, "Delete action must still be clickable once it moves to its own row at Compact width")
        }

    @Test
    fun inbound_row_stays_a_single_line_at_wide_width() =
        runComposeUiTest {
            setContent {
                AppEnvironment(tag = "en") { NomNomzTheme {
                    Box(modifier = Modifier.width(wideWidth).testTag(containerTag)) {
                        InboundRow(
                            ep = inbound(),
                            pipelines = emptyList(),
                            manage = ManageDecision.Allowed,
                            onToggle = {},
                            onEdit = {},
                            onRotate = {},
                            onDelete = {},
                        )
                    }
                } }
            }
            waitForIdle()

            val container: SemanticsNode = onNodeWithTag(containerTag).fetchSemanticsNode()
            val editNode: SemanticsNode = onNodeWithContentDescription("Edit").fetchSemanticsNode()
            val verticalOffset: Float = editNode.boundsInRoot.top - container.boundsInRoot.top
            assertTrue(
                verticalOffset < 24f,
                "expected Edit to sit in the same top row as the details at Wide width, offset=$verticalOffset",
            )
        }
}
