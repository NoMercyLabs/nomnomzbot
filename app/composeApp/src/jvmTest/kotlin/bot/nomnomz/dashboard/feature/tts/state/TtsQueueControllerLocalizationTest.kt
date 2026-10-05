// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TtsApi
import java.lang.reflect.Proxy
import java.util.Locale
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// S-I18N-NOCHANNELERROR: a write with no active channel used to surface the hardcoded English literal
// `TtsQueueController.NoChannelError`. It now resolves a string resource, so with the locale forced to Dutch the
// controller's error state must carry the real Dutch translation, never the English sentence.
class TtsQueueControllerLocalizationTest {

    @Test
    fun a_write_with_no_active_channel_surfaces_the_localized_dutch_error() = runTest {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val channelsApi: ChannelsApi =
                stub { ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded")) }
            val ttsApi: TtsApi = stub { error("unreachable: the no-channel guard must stop the call") }
            val controller = TtsQueueController(channelsApi, ttsApi)
            controller.load()

            controller.approve("entry-1")

            val state: TtsQueueState = controller.state.value
            assertTrue(state is TtsQueueState.Error)
            assertEquals(
                "Geen actief kanaal — maak opnieuw verbinding en probeer het nogmaals.",
                (state as TtsQueueState.Error).detail,
            )
        } finally {
            Locale.setDefault(original)
        }
    }

    // Every suspend member returns the same value; the proxy never suspends, so the result is the return value.
    private inline fun <reified T : Any> stub(crossinline answer: () -> Any): T =
        Proxy.newProxyInstance(T::class.java.classLoader, arrayOf(T::class.java)) { _, _, _ -> answer() } as T
}
