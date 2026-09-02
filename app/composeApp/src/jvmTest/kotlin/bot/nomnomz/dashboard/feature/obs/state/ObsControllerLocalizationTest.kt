// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.obs.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ObsApi
import bot.nomnomz.dashboard.core.network.ObsBridgeSetup
import bot.nomnomz.dashboard.core.network.ObsBridgeStatus
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.core.network.ObsInput
import bot.nomnomz.dashboard.core.network.ObsProbe
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.core.network.ObsState
import bot.nomnomz.dashboard.core.network.UpsertObsConnectionBody
import java.util.Locale
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// S105-I18N-HARDCODED-ERRORS: the OBS page used to build its "no active channel" error from a plain Kotlin
// string literal (`ObsController.NoChannelError`) instead of a translation resource — an English-only message
// baked straight into `ObsUiState.Error`/`actionError` regardless of the dashboard's chosen language. The
// controller now resolves it through `Res.string.obs_no_channel_error` (the same real Compose Resources
// pipeline `stringResource` uses). This proves the fix the way SchemaLocalizationManifestTest proves schema
// strings: force the JVM default locale to Dutch and assert the controller's error state carries the ACTUAL
// Dutch translation — not the hardcoded English sentence, which would fail this assertion if it ever came back.
class ObsControllerLocalizationTest {

    @Test
    fun a_write_with_no_active_channel_surfaces_the_localized_dutch_error() = runTest {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))

            // primaryChannel() fails, so load() never resolves a channelId — every write below hits the
            // "no active channel" branch without needing a working ObsApi.
            val controller =
                ObsController(
                    FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                    UnreachableObsApi(),
                )

            controller.saveConnection(mode = "direct", host = null, port = null, password = null, isEnabled = true)

            val state: ObsUiState = controller.state.value
            assertTrue(state is ObsUiState.Error)
            assertEquals(
                "Geen actief kanaal — maak opnieuw verbinding en probeer het nogmaals.",
                (state as ObsUiState.Error).detail,
            )
        } finally {
            Locale.setDefault(original)
        }
    }

    @Test
    fun a_control_action_with_no_active_channel_surfaces_the_localized_dutch_error() = runTest {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))

            val controller =
                ObsController(
                    FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                    UnreachableObsApi(),
                )

            controller.switchScene("Scene 1")

            val state: ObsUiState = controller.state.value
            assertTrue(state is ObsUiState.Error)
            assertEquals(
                "Geen actief kanaal — maak opnieuw verbinding en probeer het nogmaals.",
                (state as ObsUiState.Error).detail,
            )
        } finally {
            Locale.setDefault(original)
        }
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

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

// The no-channel path never reaches the ObsApi (load() bails before `refresh()` ever calls it), so every
// member errors loudly if the controller's guard clause ever regresses and starts calling through.
private class UnreachableObsApi : ObsApi {
    override suspend fun connection(channelId: String): ApiResult<ObsConnection> = error("unreachable")
    override suspend fun upsertConnection(channelId: String, body: UpsertObsConnectionBody): ApiResult<ObsConnection> =
        error("unreachable")
    override suspend fun bridgeSetup(channelId: String): ApiResult<ObsBridgeSetup> = error("unreachable")
    override suspend fun rotateBridgeToken(channelId: String): ApiResult<ObsBridgeSetup> = error("unreachable")
    override suspend fun bridgeStatus(channelId: String): ApiResult<ObsBridgeStatus> = error("unreachable")
    override suspend fun probe(channelId: String): ApiResult<ObsProbe> = error("unreachable")
    override suspend fun state(channelId: String): ApiResult<ObsState> = error("unreachable")
    override suspend fun scenes(channelId: String): ApiResult<List<ObsScene>> = error("unreachable")
    override suspend fun inputs(channelId: String): ApiResult<List<ObsInput>> = error("unreachable")
    override suspend fun switchScene(channelId: String, scene: String): ApiResult<Unit> = error("unreachable")
    override suspend fun setInputMute(channelId: String, inputName: String, muted: Boolean): ApiResult<Unit> =
        error("unreachable")
    override suspend fun setInputVolume(channelId: String, inputName: String, volumeDb: Double): ApiResult<Unit> =
        error("unreachable")
    override suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit> = error("unreachable")
    override suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit> = error("unreachable")
}
