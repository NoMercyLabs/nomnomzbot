// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.ui

import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.test.assertCountEquals
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.TtsVoicePage
import bot.nomnomz.dashboard.core.network.UserModerationHistorySummary
import bot.nomnomz.dashboard.core.network.ViewerIdentity
import bot.nomnomz.dashboard.core.network.ViewerOverrides
import bot.nomnomz.dashboard.core.network.ViewerPermits
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileController
import bot.nomnomz.dashboard.feature.moderation.state.FakeModerationApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole as ShellManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.CompletableDeferred

// Defect SH-1 for the viewer's TTS voice on the profile: Assign voice did not wait for the server (the editor
// stayed live while the call ran) and a failure showed above the list, away from the button. The editor now
// shows pending and locks while saving, keeps the chosen voice on failure, and shows the reason next to Assign.
@OptIn(ExperimentalTestApi::class)
class ViewerProfileVoiceSaveTest {
    private val reason: String = "Voice not synthesisable"
    private val voice: TtsVoice = TtsVoice(id = "v-ava", name = "ava", displayName = "Ava", locale = "en-US", provider = "azure")

    @Test
    fun assign_is_locked_while_the_server_call_runs() = runComposeUiTest {
        val tts = FakeTts(ApiResult.Ok(Unit), gate = CompletableDeferred())
        open(controller(tts))

        pickAva()
        onNodeWithText("Assign voice").performClick()
        waitForIdle()

        assertEquals(1, tts.setCalls.size)
        // The button shows its pending spinner instead of the label, so it cannot be clicked a second time.
        onAllNodesWithText("Assign voice").assertCountEquals(0)
        assertEquals(1, tts.setCalls.size)
        tts.gate?.complete(Unit)
        waitForIdle()
        onNode(hasScrollAction()).performScrollToNode(hasText("Assign voice"))
        onNodeWithText("Assign voice").assertExists()
    }

    @Test
    fun a_failed_assign_keeps_the_choice_and_shows_the_reason_next_to_the_button() = runComposeUiTest {
        val tts = FakeTts(ApiResult.Failure(ApiError(400, "BAD_REQUEST", reason)))
        open(controller(tts))

        pickAva()
        onNodeWithText("Assign voice").performClick()
        waitForIdle()

        onNodeWithText(reason).assertExists()
        onNodeWithText("Assign voice").assertIsEnabled()
        // The chosen voice stays marked.
        onNodeWithText("Selected", useUnmergedTree = true).assertExists()
        // The reason sits below the Assign row, next to the button, not above the list.
        onNode(hasScrollAction()).performScrollToNode(hasText(reason))
        val buttonBottom: Float = onNodeWithText("Assign voice").fetchSemanticsNode().boundsInRoot.bottom
        val reasonTop: Float = onNodeWithText(reason).fetchSemanticsNode().boundsInRoot.top
        assertTrue(reasonTop >= buttonBottom - 1f, "reason top $reasonTop should be at or below the button bottom $buttonBottom")
    }

    @Test
    fun a_successful_assign_calls_the_server_once_and_shows_no_error() = runComposeUiTest {
        val tts = FakeTts(ApiResult.Ok(Unit))
        open(controller(tts))

        pickAva()
        onNodeWithText("Assign voice").performClick()
        waitForIdle()

        assertEquals(listOf("v-ava"), tts.setCalls)
        onAllNodesWithText(reason).assertCountEquals(0)
    }

    private fun ComposeUiTest.pickAva() {
        onNode(hasScrollAction()).performScrollToNode(hasText("Search voices", substring = true))
        // The voice search is the last editable field on the page.
        onAllNodes(hasSetTextAction()).onLast().performTextInput("ava")
        mainClock.advanceTimeBy(500)
        waitForIdle()
        onNode(hasScrollAction()).performScrollToNode(hasContentDescription("en-US, azure", substring = true))
        onNode(hasContentDescription("en-US, azure", substring = true)).performClick()
        waitForIdle()
        onNode(hasScrollAction()).performScrollToNode(hasText("Assign voice"))
    }

    private fun ComposeUiTest.open(controller: ViewerProfileController) {
        setContent {
            val owner: LifecycleOwner =
                object : LifecycleOwner {
                    override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
                }
            (owner.lifecycle as LifecycleRegistry).currentState = Lifecycle.State.RESUMED
            androidx.compose.runtime.CompositionLocalProvider(LocalLifecycleOwner provides owner) {
                NomNomzTheme {
                    AppEnvironment("en") {
                        ViewerProfileScreen(controller = controller, userId = "u1", role = ShellManagementRole.Broadcaster, onBack = {})
                    }
                }
            }
        }
        waitForIdle()
    }

    private fun controller(tts: TtsApi): ViewerProfileController =
        ViewerProfileController(
            channelsApi = VPSFakeChannelsApi(),
            communityApi = VPSFakeCommunityApi(profile()),
            moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>())),
            ttsApi = tts,
            rolesApi = VPSStubRolesApi(),
            viewerDataApi = VPSStubViewerDataApi(),
            gdprApi = VPSStubGdprApi(),
            usersApi = VPSStubUsersApi(),
            fileBridge = VPSStubFileBridge(),
        )

    private inner class FakeTts(
        private val setResult: ApiResult<Unit>,
        val gate: CompletableDeferred<Unit>? = null,
    ) : TtsApi by VPSStubTtsApi() {
        val setCalls: MutableList<String> = mutableListOf()

        override suspend fun voicesPage(
            channelId: String,
            query: String,
            locale: String,
            gender: String,
            provider: String,
            accent: String,
            page: Int,
            pageSize: Int,
        ): ApiResult<TtsVoicePage> = ApiResult.Ok(TtsVoicePage(data = listOf(voice), total = 1))

        override suspend fun setUserVoice(channelId: String, userId: String, voiceId: String): ApiResult<Unit> {
            setCalls += voiceId
            gate?.await()
            return setResult
        }
    }

    private companion object {
        fun profile(): ViewerProfileSummary =
            ViewerProfileSummary(
                identity =
                    ViewerIdentity(
                        userId = "u1",
                        twitchUserId = "tw-cathy",
                        username = "chattycathy",
                        displayName = "Chatty Cathy",
                        communityStanding = "subscriber",
                        firstSeenUtc = "2026-01-01T00:00:00Z",
                    ),
                moderationHistory = UserModerationHistorySummary(),
                permits = ViewerPermits(),
                overrides = ViewerOverrides(),
            )
    }
}
