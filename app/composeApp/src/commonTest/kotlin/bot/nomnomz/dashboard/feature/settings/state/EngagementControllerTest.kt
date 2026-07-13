// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.EngagementApi
import bot.nomnomz.dashboard.core.network.EngagementConfig
import bot.nomnomz.dashboard.core.network.UpdateEngagementConfigBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the "Engagement triggers" card state machine: surface the channel's real config, persist a whole-config
// save by ADOPTING the backend's echoed (validated/clamped) config rather than the requested values, and degrade
// to an error state when a call fails. The screen is a pure projection, so this proves the card shows real config
// and writes exactly what was toggled.
class EngagementControllerTest {

    @Test
    fun load_surfaces_the_channels_config() = runTest {
        val controller =
            EngagementController(
                FakeEngagementApi(
                    ApiResult.Ok(
                        EngagementConfig(
                            firstTimeChatterEnabled = true,
                            returningChatterEnabled = false,
                            watchStreakEnabled = true,
                            streakMilestones = listOf(5, 10, 25),
                            greetCooldownSeconds = 300,
                        )
                    )
                )
            )

        controller.load()

        val state: EngagementState = controller.state.value
        assertTrue(state is EngagementState.Ready)
        val config: EngagementConfig = (state as EngagementState.Ready).config
        assertEquals(true, config.firstTimeChatterEnabled)
        assertEquals(true, config.watchStreakEnabled)
        assertEquals(listOf(5, 10, 25), config.streakMilestones)
        assertEquals(300, config.greetCooldownSeconds)
    }

    @Test
    fun load_errors_when_the_config_call_fails() = runTest {
        val controller =
            EngagementController(FakeEngagementApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))))

        controller.load()

        assertTrue(controller.state.value is EngagementState.Error)
    }

    @Test
    fun save_sends_the_edited_config_and_adopts_the_backend_echo() = runTest {
        // The backend clamps/normalizes (e.g. de-dupes + sorts milestones) and echoes the saved config; the
        // controller adopts THAT, not the request. Here the echo sorts the milestones, proving it trusts the server.
        val api =
            FakeEngagementApi(
                ApiResult.Ok(EngagementConfig()),
                setResult =
                    ApiResult.Ok(
                        EngagementConfig(
                            firstTimeChatterEnabled = true,
                            returningChatterEnabled = true,
                            watchStreakEnabled = false,
                            streakMilestones = listOf(3, 7, 14),
                            greetCooldownSeconds = 120,
                        )
                    ),
            )
        val controller = EngagementController(api)
        controller.load()

        val body =
            UpdateEngagementConfigBody(
                firstTimeChatterEnabled = true,
                returningChatterEnabled = true,
                watchStreakEnabled = false,
                streakMilestones = listOf(14, 3, 7),
                greetCooldownSeconds = 120,
            )
        controller.save(body)

        // Exactly the edited body is sent.
        assertEquals(body, api.lastSet)

        // State holds the backend echo (sorted milestones), flags the save, and carries no error.
        val state: EngagementState = controller.state.value
        assertTrue(state is EngagementState.Ready)
        val ready: EngagementState.Ready = state as EngagementState.Ready
        assertEquals(listOf(3, 7, 14), ready.config.streakMilestones)
        assertEquals(120, ready.config.greetCooldownSeconds)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
    }

    @Test
    fun save_failure_surfaces_the_error_without_losing_the_loaded_config() = runTest {
        val loaded = EngagementConfig(firstTimeChatterEnabled = true, greetCooldownSeconds = 60)
        val api =
            FakeEngagementApi(
                ApiResult.Ok(loaded),
                setResult = ApiResult.Failure(ApiError(400, "VALIDATION_FAILED", "cooldown must be >= 0")),
            )
        val controller = EngagementController(api)
        controller.load()

        controller.save(
            UpdateEngagementConfigBody(
                firstTimeChatterEnabled = true,
                returningChatterEnabled = false,
                watchStreakEnabled = false,
                streakMilestones = null,
                greetCooldownSeconds = -5,
            )
        )

        // Stays Ready on the loaded config (no data loss) and surfaces the error.
        val state: EngagementState = controller.state.value
        assertTrue(state is EngagementState.Ready)
        val ready: EngagementState.Ready = state as EngagementState.Ready
        assertEquals(loaded, ready.config)
        assertEquals("cooldown must be >= 0", ready.saveError)
        assertEquals(false, ready.saving)
    }
}

private class FakeEngagementApi(
    private val getResult: ApiResult<EngagementConfig>,
    private val setResult: ApiResult<EngagementConfig> = ApiResult.Ok(EngagementConfig()),
) : EngagementApi {
    var lastSet: UpdateEngagementConfigBody? = null
        private set

    override suspend fun getConfig(): ApiResult<EngagementConfig> = getResult

    override suspend fun setConfig(body: UpdateEngagementConfigBody): ApiResult<EngagementConfig> {
        lastSet = body
        return setResult
    }
}
