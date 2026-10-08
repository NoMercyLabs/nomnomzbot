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
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.UserModerationHistorySummary
import bot.nomnomz.dashboard.core.network.UserNote
import bot.nomnomz.dashboard.core.network.ViewerIdentity
import bot.nomnomz.dashboard.core.network.ViewerOverrides
import bot.nomnomz.dashboard.core.network.ViewerPermits
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileController
import bot.nomnomz.dashboard.feature.moderation.state.FakeModerationApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole as ShellManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals

// Shared defect SH-1 for the viewer profile's history note and shoutout line: a failed write lost the typed
// text and reached the user only as a toast. Each form now keeps its edits on failure and shows the reason inline.
@OptIn(ExperimentalTestApi::class)
class ViewerProfileInlineFormsTest {
    private val reason: String = "Twitch said no"
    private val failure: ApiError = ApiError(400, "BAD_REQUEST", reason)

    @Test
    fun a_failed_note_keeps_the_typed_text_and_shows_the_reason() = runComposeUiTest {
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        moderation.addHistoryNoteResult = ApiResult.Failure(failure)
        open(controller(moderation = moderation))

        scrollTo("Add a note")
        typeInto("Add a note", "Warned in Discord")
        onNodeWithText("Add note").performClick()
        waitForIdle()

        onNodeWithText("Warned in Discord").assertExists()
        onNodeWithText(reason).assertExists()
    }

    @Test
    fun a_successful_note_clears_the_field_and_lists_the_note() = runComposeUiTest {
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        moderation.addHistoryNoteResult = ApiResult.Ok(UserNote(id = 2, subjectUserId = "u1", content = "Warned in Discord"))
        open(controller(moderation = moderation))

        scrollTo("Add a note")
        typeInto("Add a note", "Warned in Discord")
        onNodeWithText("Add note").performClick()
        waitForIdle()

        // The text now appears once: as the listed note, no longer as the field's draft.
        onAllNodesWithText("Warned in Discord").assertCountEquals(1)
        onAllNodesWithText(reason).assertCountEquals(0)
    }

    @Test
    fun a_failed_shoutout_save_keeps_the_edit_and_shows_the_reason() = runComposeUiTest {
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        moderation.setShoutoutOverrideResult = ApiResult.Failure(failure)
        open(controller(moderation = moderation))

        scrollTo("Shoutout line")
        typeInto("Shoutout line", "Go follow Cathy")
        scrollTo("Save")
        onNodeWithText("Save").performClick()
        waitForIdle()

        onNodeWithText("Go follow Cathy").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(0, moderation.savedOverrides.size)
    }

    @Test
    fun a_successful_shoutout_save_writes_the_line_and_shows_no_error() = runComposeUiTest {
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        open(controller(moderation = moderation))

        scrollTo("Shoutout line")
        typeInto("Shoutout line", "Go follow Cathy")
        scrollTo("Save")
        onNodeWithText("Save").performClick()
        waitForIdle()

        assertEquals(listOf("Go follow Cathy"), moderation.savedOverrides.map { it.messageTemplate })
        onAllNodesWithText(reason).assertCountEquals(0)
    }

    // The label is its own node, not the editable one, so type into the editable field that sits nearest the label.
    private fun ComposeUiTest.typeInto(label: String, text: String) {
        val labelTop: Float = onNode(hasText(label, substring = true)).fetchSemanticsNode().boundsInRoot.top
        val tops: List<Float> = onAllNodes(hasSetTextAction()).fetchSemanticsNodes().map { it.boundsInRoot.top }
        val nearest: Int = tops.indices.minBy { index -> kotlin.math.abs(tops[index] - labelTop) }
        onAllNodes(hasSetTextAction())[nearest].performTextInput(text)
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

    private fun ComposeUiTest.scrollTo(text: String) {
        onNode(hasScrollAction()).performScrollToNode(hasText(text))
    }

    private fun controller(
        moderation: FakeModerationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>())),
    ): ViewerProfileController =
        ViewerProfileController(
            channelsApi = VPSFakeChannelsApi(),
            communityApi = VPSFakeCommunityApi(profile()),
            moderationApi = moderation,
            ttsApi = VPSStubTtsApi(),
            rolesApi = VPSStubRolesApi(),
            viewerDataApi = VPSStubViewerDataApi(),
            gdprApi = VPSStubGdprApi(),
            usersApi = VPSStubUsersApi(),
            fileBridge = VPSStubFileBridge(),
        )

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
