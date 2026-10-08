// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.FeedbackMessage
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.UpsertVtsConnectionBody
import bot.nomnomz.dashboard.core.network.VtsApi
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsControlBody
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.core.network.VtsRequestResult
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking
import org.jetbrains.compose.resources.StringResource

// Shared defect SH-1 on the VTube Studio page: rotating the bridge token cuts the running bridge off, so it asks
// first, waits for the server, and shows a failure inside the dialog (no toast on top).
@OptIn(ExperimentalTestApi::class)
class VtsRotateBridgeTokenConfirmTest {
    private val reason: String = "The bridge token could not be rotated."

    @Test
    fun clicking_rotate_opens_a_confirm_and_writes_nothing_yet() = runComposeUiTest {
        val api: RotateStubVtsApi = RotateStubVtsApi(ApiResult.Ok(VtsConnection(mode = "bridge")))
        setContent { Screen(api, RotateRecordingFeedback()) }
        waitForIdle()
        onNodeWithText("Rotate bridge token").performClick()
        waitForIdle()

        onNodeWithText("Rotate the bridge token?").assertExists()
        assertEquals(0, api.rotations)
    }

    @Test
    fun a_failed_rotate_keeps_the_confirm_open_with_the_reason_and_raises_no_toast() = runComposeUiTest {
        val api: RotateStubVtsApi = RotateStubVtsApi(ApiResult.Failure(ApiError(500, "FAILED", reason)))
        val feedback: RotateRecordingFeedback = RotateRecordingFeedback()
        setContent { Screen(api, feedback) }
        waitForIdle()
        onNodeWithText("Rotate bridge token").performClick()
        waitForIdle()
        onNodeWithText("Rotate token").performClick()
        waitForIdle()

        assertEquals(1, api.rotations)
        onNodeWithText("Rotate the bridge token?").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_rotate_closes_the_confirm() = runComposeUiTest {
        val api: RotateStubVtsApi = RotateStubVtsApi(ApiResult.Ok(VtsConnection(mode = "bridge", hasBridgeToken = true)))
        setContent { Screen(api, RotateRecordingFeedback()) }
        waitForIdle()
        onNodeWithText("Rotate bridge token").performClick()
        waitForIdle()
        onNodeWithText("Rotate token").performClick()
        waitForIdle()

        assertEquals(1, api.rotations)
        onNodeWithText("Rotate the bridge token?").assertDoesNotExist()
    }

    @Composable
    private fun Screen(api: RotateStubVtsApi, feedback: Feedback) {
        val controller: VtsController = VtsController(RotateStubChannelsApi(), api, feedback)
        runBlocking { controller.load() }
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
            NomNomzTheme {
                AppEnvironment("en") { VtsScreen(controller = controller, role = ManagementRole.Broadcaster) }
            }
        }
    }
}

private class RotateRecordingFeedback : Feedback {
    var errors: Int = 0

    override fun success(label: StringResource, vararg formatArgs: Any) = Unit

    override fun error(label: StringResource, vararg formatArgs: Any) {
        errors += 1
    }

    override fun info(label: StringResource, vararg formatArgs: Any) = Unit

    override fun emit(message: FeedbackMessage) = Unit
}

private class RotateStubChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "c1"))

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

// The connection read answers in bridge mode (so the rotate button shows); only rotate has a scripted answer.
private class RotateStubVtsApi(private val rotate: ApiResult<VtsConnection>) : VtsApi {
    var rotations: Int = 0

    override suspend fun connection(channelId: String): ApiResult<VtsConnection> =
        ApiResult.Ok(VtsConnection(mode = "bridge"))

    override suspend fun upsertConnection(channelId: String, body: UpsertVtsConnectionBody): ApiResult<VtsConnection> =
        ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun authorize(channelId: String): ApiResult<Unit> = ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun rotateBridgeToken(channelId: String): ApiResult<VtsConnection> {
        rotations += 1
        return rotate
    }

    override suspend fun inventory(channelId: String): ApiResult<VtsModelInventory> =
        ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun control(channelId: String, body: VtsControlBody): ApiResult<VtsRequestResult> =
        ApiResult.Failure(ApiError(404, "STUB", "stub"))
}
