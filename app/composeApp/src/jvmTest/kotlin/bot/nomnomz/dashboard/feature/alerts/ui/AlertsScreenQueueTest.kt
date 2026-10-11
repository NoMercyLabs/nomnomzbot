// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.alerts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.AlertQueueDto
import bot.nomnomz.dashboard.core.network.AlertQueueEntryDto
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.feature.alerts.state.AlertsController
import kotlin.test.Test

/**
 * S059 (widgets-overlays.md §1.2, Done-when 4): the dashboard's Alerts page must show the REAL cross-platform
 * queue, with provider attribution, and must show the honest "not connected" state rather than implying an
 * alert was ever shown on stream. A test that passed with the overlay disconnected AND no "not connected" copy
 * on screen would be exactly the false-positive this project has already paid for twice (TTS's "spoke",
 * the overlay test button) — so this asserts the not-connected copy is on screen, not merely that the page
 * did not crash.
 */
@OptIn(ExperimentalTestApi::class)
class AlertsScreenQueueTest {
    // AlertsScreen collects the controller flow with collectAsStateWithLifecycle(), which requires a
    // LocalLifecycleOwner — mirrors AdminTabGroupingTest's EnglishContent helper (the first full-screen
    // admin test to hit the same gap).
    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            AppEnvironment(tag = "en") { NomNomzTheme { content() } }
        }
    }

    @Test
    fun queue_entries_from_two_providers_render_with_their_own_provider_attribution() = runComposeUiTest {
        val queue =
            AlertQueueDto(
                overlayConnected = true,
                entries =
                    listOf(
                        AlertQueueEntryDto(
                            id = "1",
                            provider = "patreon",
                            kind = "supporter.tip",
                            status = "delivered",
                        ),
                        AlertQueueEntryDto(
                            id = "2",
                            provider = "shopify",
                            kind = "supporter.merch",
                            status = "delivered",
                        ),
                    ),
            )
        val controller = newController(queue = ApiResult.Ok(queue))

        setContent { EnglishContent { AlertsScreen(controller = controller) } }
        waitForIdle()

        onNodeWithText("patreon").assertExists()
        onNodeWithText("shopify").assertExists()
        // Every entry keeps ITS OWN provider — never merged into one generic "alert" label alongside the kind.
        onNodeWithText("supporter.tip").assertExists()
        onNodeWithText("supporter.merch").assertExists()
    }

    @Test
    fun a_disconnected_overlay_shows_not_connected_copy_never_implying_an_alert_was_shown() =
        runComposeUiTest {
            val queue =
                AlertQueueDto(
                    overlayConnected = false,
                    entries =
                        listOf(
                            AlertQueueEntryDto(
                                id = "1",
                                provider = "treatstream",
                                kind = "supporter.tip",
                                status = "queued",
                            )
                        ),
                )
            val controller = newController(queue = ApiResult.Ok(queue))

            setContent { EnglishContent { AlertsScreen(controller = controller) } }
            waitForIdle()

            // The honest state is on screen — not a generic "connected" claim, and the entry reads "queued",
            // never "shown on stream".
            onNodeWithText("Overlay not connected — add the alert surface to OBS").assertExists()
            onNodeWithText("Queued — not yet shown").assertExists()
        }

    @Test
    fun the_tab_offers_no_create_control_because_event_responses_are_managed_on_their_own_page() =
        runComposeUiTest {
            val controller = newController(queue = ApiResult.Ok(AlertQueueDto(overlayConnected = true)))

            setContent { EnglishContent { AlertsScreen(controller = controller) } }
            waitForIdle()

            onNodeWithText("Live alert queue").assertExists()
            onNodeWithText("New alert").assertDoesNotExist()
        }

    private fun newController(queue: ApiResult<AlertQueueDto>): AlertsController =
        AlertsController(
            channelsApi = FakeChannelsApi(),
            alertsApi = FakeAlertsApi(queue),
        )
}

private class FakeChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> =
        ApiResult.Ok(ChannelSummary(id = "chan-1"))

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

/** Only [queue] is reachable from the page render. */
private class FakeAlertsApi(private val queueResult: ApiResult<AlertQueueDto>) : AlertsApi {
    override suspend fun queue(channelId: String): ApiResult<AlertQueueDto> = queueResult
}
