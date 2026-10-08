// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsOff
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.FollowBotBlockEntry
import bot.nomnomz.dashboard.core.network.SpamCampaign
import bot.nomnomz.dashboard.core.network.SpamDefenseApi
import bot.nomnomz.dashboard.core.network.SpamDefensePolicy
import bot.nomnomz.dashboard.core.network.SpamDefenseSettings
import bot.nomnomz.dashboard.core.network.SpamDetection
import bot.nomnomz.dashboard.core.network.SpamSettingDescriptor
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * SH-1 (writes that do not wait for the server): the admin spam defaults card used to fire the save and
 * forget it. A refusal reached only the banner above the tabs, far from the Save button, and the button
 * stayed live while the call ran so a second click sent a second write. The card now disables Save while
 * the call runs, keeps the edit on a refusal and shows the server's reason beside the button.
 */
@OptIn(ExperimentalTestApi::class)
class AdminSpamDefaultsSaveWaitsTest {

    private val refusal: ApiResult.Failure =
        ApiResult.Failure(ApiError(422, "OUT_OF_RANGE", "The server said no."))
    private val policy: SpamDefensePolicy =
        SpamDefensePolicy(
            settings = SpamDefenseSettings(dryRun = true),
            catalogue =
                listOf(
                    SpamSettingDescriptor(
                        key = "DryRun",
                        group = "master",
                        labelKey = "spam_setting_dry_run_label",
                        isToggle = true,
                    )
                ),
        )

    private fun controllerFor(api: FakeSpamDefenseApi): AdminController {
        val controller =
            AdminController(
                api = FakeAdminApiForNetworkBlockTest(),
                spamDefenseApi = api,
                iamApi = FakeIamApiForNetworkBlockTest(),
                platformAdminApi = FakePlatformAdminApiForNetworkBlockTest(),
            )
        runTest { controller.loadSpamDefaults() }
        return controller
    }

    private fun ComposeUiTest.show(controller: AdminController) {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    val state by controller.state.collectAsState()
                    SpamDefaultsTab(state = state, controller = controller)
                }
            }
        }
        waitForIdle()
    }

    private fun ComposeUiTest.flipDryRunAndSave() {
        onNode(isToggleable()).performClick()
        waitForIdle()
        clickSave()
    }

    // Found by role, not label: while a save runs the button shows a spinner in place of its label.
    private fun ComposeUiTest.clickSave() {
        onNode(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Button)).performClick()
        waitForIdle()
    }

    @Test
    fun save_refused_shows_the_reason_beside_the_button_and_keeps_the_edit() {
        val api = FakeSpamDefenseApi(policy, saveResult = refusal)
        val controller = controllerFor(api)
        runComposeUiTest {
            show(controller)
            flipDryRunAndSave()

            assertEquals(1, api.saves.size)
            assertFalse(api.saves.single().dryRun)
            assertEquals(1, onAllNodesWithText("The server said no.").fetchSemanticsNodes().size)
            // The edit is still on screen: the toggle still reads off, not reset to the saved value.
            onNode(isToggleable()).assertIsOff()
            // Save is usable again so the operator can fix the value and retry.
            onNode(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Button)).assertIsEnabled()
        }
    }

    @Test
    fun save_in_flight_disables_the_button_so_a_second_click_sends_nothing() {
        val gate = CompletableDeferred<ApiResult<SpamDefenseSettings>>()
        val api = FakeSpamDefenseApi(policy, saveGate = gate)
        val controller = controllerFor(api)
        runComposeUiTest {
            show(controller)
            flipDryRunAndSave()
            clickSave()

            assertEquals(1, api.saves.size)
            gate.complete(ApiResult.Ok(SpamDefenseSettings(dryRun = false)))
            waitForIdle()
            assertEquals(1, api.saves.size)
        }
    }

    @Test
    fun save_accepted_sends_the_edit_and_shows_no_error() {
        val api = FakeSpamDefenseApi(policy, saveResult = ApiResult.Ok(SpamDefenseSettings(dryRun = false)))
        val controller = controllerFor(api)
        runComposeUiTest {
            show(controller)
            flipDryRunAndSave()

            assertFalse(api.saves.single().dryRun)
            assertEquals(0, onAllNodesWithText("The server said no.").fetchSemanticsNodes().size)
            assertEquals(false, controller.state.value.spamDefaults?.settings?.dryRun)
            assertTrue(controller.state.value.error == null)
        }
    }
}

private class FakeSpamDefenseApi(
    private val policy: SpamDefensePolicy,
    private val saveResult: ApiResult<SpamDefenseSettings> = ApiResult.Ok(SpamDefenseSettings()),
    private val saveGate: CompletableDeferred<ApiResult<SpamDefenseSettings>>? = null,
) : SpamDefenseApi {
    val saves: MutableList<SpamDefenseSettings> = mutableListOf()

    override suspend fun platformDefaults(): ApiResult<SpamDefensePolicy> = ApiResult.Ok(policy)

    override suspend fun savePlatformDefaults(body: SpamDefenseSettings): ApiResult<SpamDefenseSettings> {
        saves.add(body)
        return saveGate?.await() ?: saveResult
    }

    override suspend fun policy(channelId: String): ApiResult<SpamDefensePolicy> = unused()

    override suspend fun saveSettings(channelId: String, body: SpamDefenseSettings): ApiResult<SpamDefenseSettings> =
        unused()

    override suspend fun detections(channelId: String, page: Int, pageSize: Int): ApiResult<List<SpamDetection>> =
        unused()

    override suspend fun overturn(channelId: String, detectionId: String): ApiResult<Unit> = unused()

    override suspend fun campaigns(channelId: String): ApiResult<List<SpamCampaign>> = unused()

    override suspend fun followBotBlocks(channelId: String): ApiResult<List<FollowBotBlockEntry>> = unused()

    override suspend fun restoreFollowBotBatch(channelId: String, batchId: String): ApiResult<Unit> = unused()

    private fun <T> unused(): ApiResult<T> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}
