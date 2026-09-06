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
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.width
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * S-UI-TRUNCATION: [OutboundRow]'s primary label (the webhook's own name, via
 * [bot.nomnomz.dashboard.core.designsystem.resolveRowLabel]) is the thing a user reads to tell one
 * webhook row from another before pressing Edit/Test/Rotate secret/Delete. Before this slice it was
 * `maxLines = 1` + `TextOverflow.Ellipsis`, the exact defect class the owner reported on the pipelines
 * screen ("Raid…" identifying nothing) — now `maxLines = 2`, letting a long name wrap instead of
 * clipping to one line.
 *
 * A plain `onNodeWithText(name).assertExists()` would pass EVEN on the broken `maxLines = 1` version —
 * Compose's Text semantics carry the full original string regardless of visual truncation; only the
 * painted layout is clipped. So this test also compares the RENDERED HEIGHT of a short, one-line name
 * against a long name at the same Compact (580 dp, below the 600 dp Medium breakpoint) width: a
 * `maxLines = 1` label always renders at exactly one line's height no matter how long the string is, so
 * an unchanged height would mean the row is still silently truncating. A taller render for the long
 * name proves it actually wrapped onto a second line, not that it was clipped.
 */
@OptIn(ExperimentalTestApi::class)
class WebhooksOutboundRowCompactWidthTest {

    private val shortName = "Alerts"
    private val longName = "Channel Point Redemption Alert Relay For The Overlay Bot Feed"

    private fun endpoint(name: String) =
        OutboundWebhook(
            id = "ep-1",
            name = name,
            fqdn = "hooks.example.com",
            isEnabled = true,
        )

    @Test
    fun long_webhook_name_wraps_instead_of_clipping_to_one_line_at_compact_width() =
        runComposeUiTest {
            setContent {
                NomNomzTheme {
                    // 580 dp is Compact (below WindowSizeClass's 600 dp Medium breakpoint); OutboundRow's own
                    // action-button row (Edit/Deliveries/Test/Rotate/Delete/Switch) still eats a large share of
                    // it, leaving the name column genuinely squeezed — the real-world shape of the bug. Each
                    // row gets its own identically-sized Box so neither name's wrap affects the other's.
                    Column {
                        Box(modifier = Modifier.width(580.dp)) {
                            OutboundRow(
                                ep = endpoint(shortName),
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
                        Box(modifier = Modifier.width(580.dp)) {
                            OutboundRow(
                                ep = endpoint(longName),
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
                    }
                }
            }
            waitForIdle()

            // The FULL name is present in the rendered tree — not swapped for a truncated run.
            onNodeWithText(longName, substring = true, useUnmergedTree = true).assertExists()

            val shortNameHeight: Int =
                onNodeWithText(shortName, substring = true, useUnmergedTree = true).fetchSemanticsNode().size.height
            val longNameHeight: Int =
                onNodeWithText(longName, substring = true, useUnmergedTree = true).fetchSemanticsNode().size.height

            assertTrue(
                longNameHeight > shortNameHeight,
                "expected the long webhook name to wrap onto a second line at Compact width " +
                    "(short-name height=$shortNameHeight, long-name height=$longNameHeight) — an " +
                    "unchanged height means it rendered on one line only, i.e. it is still being clipped",
            )
        }
}
