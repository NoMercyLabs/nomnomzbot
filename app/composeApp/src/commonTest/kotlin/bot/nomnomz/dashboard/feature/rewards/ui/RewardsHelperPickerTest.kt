// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateRewardBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.RedemptionSummary
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.core.network.RewardsApi
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.UpdateRewardBody
import bot.nomnomz.dashboard.feature.rewards.state.RewardsController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * S043's last unwired free-text field.
 *
 * Every other template field in the product — commands, event responses, timers, chat triggers, Discord,
 * pipelines — carries the "All helpers…" picker. A reward's redemption announcement did not, so a streamer
 * writing one had to already know the token vocabulary by heart, with no way to discover `{user}`.
 *
 * The assertion is on the CONTEXT the picker requests, not merely that a button appeared: asking for the
 * wrong context would open a dialog listing helpers that do not resolve in a reward response, which reads as
 * a working feature right up until the message posts with a literal token in it.
 */
class RewardsHelperPickerTest {

    private class RecordingHelpersApi : TemplateHelpersApi {
        var lastContext: TemplateHelperContext? = null
        var calls: Int = 0

        override suspend fun helpers(
            context: TemplateHelperContext,
            eventType: String?,
        ): ApiResult<List<TemplateHelperDto>> {
            lastContext = context
            calls++
            return ApiResult.Ok(emptyList())
        }
    }

    private class StubChannelsApi : ChannelsApi {
        override suspend fun primaryChannel(): ApiResult<ChannelSummary> =
            ApiResult.Ok(ChannelSummary(id = "ch1"))

        override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
        override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
        override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
        override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
        override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
        override suspend fun channelScopes(channelId: String) = error("stub")
        override suspend fun startChannelBotConnect(channelId: String) = error("stub")
        override suspend fun channelBotStatus(channelId: String) = error("stub")
        override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> =
            ApiResult.Ok(emptyList())
    }

    /** The narrowest RewardsApi that satisfies a load: everything empty, every write a no-op. */
    private class StubRewardsApi : RewardsApi {
        override suspend fun list(channelId: String): ApiResult<List<RewardSummary>> =
            ApiResult.Ok(emptyList())

        override suspend fun create(channelId: String, body: CreateRewardBody): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun update(
            channelId: String,
            rewardId: String,
            body: UpdateRewardBody,
        ): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun delete(channelId: String, rewardId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun blastRadius(
            channelId: String,
            rewardId: String,
        ): ApiResult<bot.nomnomz.dashboard.core.network.BlastRadiusSummary> =
            ApiResult.Ok(bot.nomnomz.dashboard.core.network.BlastRadiusSummary())

        override suspend fun redemptions(
            channelId: String,
            status: String?,
        ): ApiResult<List<RedemptionSummary>> = ApiResult.Ok(emptyList())

        override suspend fun fulfillRedemption(
            channelId: String,
            redemptionId: String,
        ): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun refundRedemption(
            channelId: String,
            redemptionId: String,
        ): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun sync(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun import(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun recreate(channelId: String, rewardId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun redemptionTimers(
            channelId: String
        ): ApiResult<List<bot.nomnomz.dashboard.core.network.RedemptionTimer>> =
            ApiResult.Ok(emptyList())

        override suspend fun pauseTimer(channelId: String, timerId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun resumeTimer(channelId: String, timerId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun completeTimer(channelId: String, timerId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun cancelTimer(channelId: String, timerId: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)
    }

    private object StubPipelinesApi : PipelinesApi {
        override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
            ApiResult.Ok(emptyList())

        override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
            ApiResult.Ok(PipelineCatalogueRemote())

        override suspend fun get(
            channelId: String,
            id: String,
        ): ApiResult<bot.nomnomz.dashboard.core.network.PipelineDetail> = error("stub")

        override suspend fun create(
            channelId: String,
            body: bot.nomnomz.dashboard.core.network.CreatePipelineBody,
        ): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun createReturning(
            channelId: String,
            body: bot.nomnomz.dashboard.core.network.CreatePipelineBody,
        ): ApiResult<bot.nomnomz.dashboard.core.network.PipelineDetail> = error("stub")

        override suspend fun update(
            channelId: String,
            id: String,
            body: bot.nomnomz.dashboard.core.network.UpdatePipelineBody,
        ): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun delete(channelId: String, id: String): ApiResult<Unit> =
            ApiResult.Ok(Unit)

        override suspend fun blastRadius(
            channelId: String,
            id: String,
        ): ApiResult<bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary> = error("stub")

        override suspend fun testRun(
            channelId: String,
            id: String,
            body: bot.nomnomz.dashboard.core.network.PipelineTestRunBody,
        ): ApiResult<bot.nomnomz.dashboard.core.network.TestRunResult> = error("stub")
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun the_reward_response_field_offers_the_helper_picker_in_the_event_response_context() = runTest {
        val helpers = RecordingHelpersApi()
        val controller = RewardsController(StubChannelsApi(), StubRewardsApi(), StubPipelinesApi)
        controller.load()

        runComposeUiTest {
            setContent {
                WithLifecycle {
                    AppEnvironment("en") {
                        NomNomzTheme {
                            RewardsScreen(
                                controller = controller,
                                role = ManagementRole.Broadcaster,
                                templateHelpersApi = helpers,
                            )
                        }
                    }
                }
            }

            // Open the create dialog, then the picker beside the response field.
            onAllNodesWithText("New reward")[0].performClick()
            waitForIdle()

            assertTrue(
                onAllNodesWithText("All helpers…").fetchSemanticsNodes().isNotEmpty(),
                "the reward response field must offer the same picker every other template field has",
            )

            onAllNodesWithText("All helpers…")[0].performClick()
            waitForIdle()
        }

        assertEquals(
            TemplateHelperContext.EventResponse,
            helpers.lastContext,
            "a reward response resolves event-response helpers — asking for another context would list " +
                "tokens that never resolve here",
        )
        assertEquals(1, helpers.calls, "the picker must actually fetch, not render an empty shell")
    }
}

/**
 * The screen reads state through `collectAsStateWithLifecycle`, which needs a LifecycleOwner that the plain
 * compose test host does not provide. Mirrors the wrapper EventResponsesScreenTest already uses.
 */
@androidx.compose.runtime.Composable
private fun WithLifecycle(content: @androidx.compose.runtime.Composable () -> Unit) {
    val owner: androidx.lifecycle.LifecycleOwner =
        object : androidx.lifecycle.LifecycleOwner {
            override val lifecycle: androidx.lifecycle.Lifecycle =
                androidx.lifecycle.LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as androidx.lifecycle.LifecycleRegistry).apply {
        currentState = androidx.lifecycle.Lifecycle.State.CREATED
        currentState = androidx.lifecycle.Lifecycle.State.STARTED
        currentState = androidx.lifecycle.Lifecycle.State.RESUMED
    }
    androidx.compose.runtime.CompositionLocalProvider(
        androidx.lifecycle.compose.LocalLifecycleOwner provides owner
    ) {
        content()
    }
}
