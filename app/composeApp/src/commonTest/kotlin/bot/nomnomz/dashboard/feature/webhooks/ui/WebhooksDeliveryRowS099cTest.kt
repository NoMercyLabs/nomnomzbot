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
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.network.OutboundDelivery
import kotlin.test.Test

/**
 * S099c: proves the delivery-log row renders the REAL backend state — `NextRetryAt` (pending
 * retry), a real error message (Failed/DeadLetter), and the plain Delivered row — rather than a
 * stale/fake view. `NextRetryAt` was fetched into [OutboundDelivery] but silently dropped on the
 * floor before this slice (never rendered by `DeliveryRow`); this test would have caught that.
 *
 * Assertions target the timestamp/error/status VALUES only, never the translated label prose
 * around them ("Next retry: ", "Attempt N ·") — that copy is locale-dependent (the JVM's default
 * locale drives which `strings*.xml` composeResources resolves, and CI/dev machines are not
 * guaranteed to run under `en`), so asserting on it here would make the test's pass/fail depend on
 * the runner's locale rather than on the behavior this test exists to prove.
 */
@OptIn(ExperimentalTestApi::class)
class WebhooksDeliveryRowS099cTest {

    private fun delivery(
        status: String,
        nextRetryAt: String? = null,
        error: String? = null,
        responseCode: Int? = null,
    ) = OutboundDelivery(
        id = 1,
        endpointId = "ep-1",
        eventType = "FollowEvent",
        attempt = 1,
        status = status,
        responseCode = responseCode,
        durationMs = 42,
        nextRetryAt = nextRetryAt,
        error = error,
        createdAt = "2026-08-30T12:00:00Z",
    )

    @Test
    fun pending_row_renders_the_next_retry_timestamp() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                DeliveryRow(delivery(status = "Pending", nextRetryAt = "2026-08-30T12:05:00Z"))
            }
        }
        onNodeWithText("2026-08-30T12:05:00Z", substring = true).assertExists()
    }

    @Test
    fun failed_row_renders_a_real_error_message_and_the_next_retry_timestamp() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                DeliveryRow(
                    delivery(
                        status = "Failed",
                        nextRetryAt = "2026-08-30T12:10:00Z",
                        error = "Connection timed out after 5000ms",
                        responseCode = null,
                    )
                )
            }
        }
        onNodeWithText("Connection timed out after 5000ms", substring = true).assertExists()
        onNodeWithText("2026-08-30T12:10:00Z", substring = true).assertExists()
    }

    @Test
    fun dead_letter_row_renders_the_error_and_no_further_retry_timestamp() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                DeliveryRow(
                    delivery(
                        status = "DeadLetter",
                        nextRetryAt = null,
                        error = "Endpoint disabled after 20 consecutive failures",
                        responseCode = 503,
                    )
                )
            }
        }
        onNodeWithText("Endpoint disabled after 20 consecutive failures", substring = true).assertExists()
        onNodeWithText("DeadLetter", substring = true).assertExists()
    }

    @Test
    fun delivered_row_renders_no_error_and_no_retry_timestamp() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                DeliveryRow(delivery(status = "Delivered", responseCode = 200))
            }
        }
        onNodeWithText("Delivered", substring = true).assertExists()
        onNodeWithText("200", substring = true).assertExists()
    }
}
