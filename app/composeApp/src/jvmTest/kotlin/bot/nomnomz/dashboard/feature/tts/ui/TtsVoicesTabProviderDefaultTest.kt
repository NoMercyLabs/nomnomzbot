// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsVoicePage
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// The Voices tab opens on the channel's effective provider (the API's TTS config), never on "no provider", so the
// first voice search is already filtered to the provider the channel really speaks with.
@OptIn(ExperimentalTestApi::class)
class TtsVoicesTabProviderDefaultTest {
    @Test
    fun the_first_voice_search_uses_the_providers_the_config_returns() = runComposeUiTest {
        val api: RecordingVoicesApi = RecordingVoicesApi(TtsConfig(defaultProvider = "azure"))
        open(api)

        assertEquals("azure", api.providers.first())
    }

    @Test
    fun a_channel_with_the_default_config_searches_edge_first() = runComposeUiTest {
        val api: RecordingVoicesApi = RecordingVoicesApi(TtsConfig())
        open(api)

        assertEquals("edge", api.providers.first())
    }

    private fun ComposeUiTest.open(api: TtsApi) {
        val controller: TtsController = TtsController(StubChannelsApi(), api, feedback = CountingFeedback())
        val queueController: TtsQueueController = TtsQueueController(StubChannelsApi(), api)
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withVoicesLifecycle {
                NomNomzTheme {
                    AppEnvironment("en") {
                        TtsScreen(
                            controller = controller,
                            queueController = queueController,
                            role = ManagementRole.Broadcaster,
                            section = TtsSection.Voices,
                        )
                    }
                }
            }
        }
        mainClock.advanceTimeBy(1_000)
        waitForIdle()
    }
}

@Composable
private fun withVoicesLifecycle(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

private class RecordingVoicesApi(
    private val loaded: TtsConfig,
    base: TtsApi = StubTtsApi(),
) : TtsApi by base {
    val providers: MutableList<String> = mutableListOf()

    override suspend fun config(channelId: String): ApiResult<TtsConfig> = ApiResult.Ok(loaded)

    override suspend fun voicesPage(
        channelId: String,
        query: String,
        locale: String,
        gender: String,
        provider: String,
        accent: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<TtsVoicePage> {
        providers.add(provider)
        return ApiResult.Ok(TtsVoicePage())
    }
}
