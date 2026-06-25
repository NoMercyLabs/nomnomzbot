// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AutomodCapsFilter
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.ShieldStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModerationApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_unban_failed
import nomnomzbot.composeapp.generated.resources.feedback_unbanned

// Proves the Moderation page state machine the read-only screen renders: resolve the active channel, then
// surface the real banned-viewer list — Empty when there are none, or Error if either step fails. The screen
// is a pure projection of this, so testing it proves the page shows real data and degrades cleanly.
class ModerationControllerTest {

    @Test
    fun load_surfaces_the_channels_banned_viewers_on_success() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(
                    ApiResult.Ok(
                        listOf(
                            BannedUser(
                                id = "u1",
                                username = "trolly",
                                displayName = "Trolly",
                                reason = "Spamming links",
                                bannedBy = "ModBot",
                                bannedAt = "2026-06-24T18:05:00Z",
                            )
                        )
                    )
                ),
            )

        controller.load()

        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        val bans: List<BannedUser> = (state as ModerationState.Ready).bans
        assertEquals(1, bans.size)
        assertEquals("Trolly", bans.first().displayName)
        assertEquals("Spamming links", bans.first().reason)
    }

    @Test
    fun load_surfaces_the_mod_action_log_even_with_no_bans() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(
                    bansResults = listOf(ApiResult.Ok(emptyList())),
                    modLogResult =
                        ApiResult.Ok(
                            listOf(
                                ModLogEntry(
                                    id = "e1",
                                    action = "timeout",
                                    moderator = "Stoney_Eagle",
                                    target = "Baduser",
                                    timestamp = "2025-08-01T00:00:00Z",
                                    duration = 600,
                                )
                            )
                        ),
                ),
            )

        controller.load()

        // The log keeps the page non-empty even though there are no active bans.
        val ready: ModerationState.Ready = controller.state.value as ModerationState.Ready
        assertTrue(ready.bans.isEmpty())
        assertEquals(1, ready.modLog.size)
        assertEquals("timeout", ready.modLog.first().action)
        assertEquals("Baduser", ready.modLog.first().target)
    }

    @Test
    fun shield_mode_on_keeps_the_page_ready_and_toggling_calls_the_api() = runTest {
        val moderationApi =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                shieldResult = ApiResult.Ok(ShieldStatus(enabled = true)),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()

        // No bans and no log, but shield is on → the page is Ready (so the active shield shows), not Empty.
        val ready: ModerationState.Ready = controller.state.value as ModerationState.Ready
        assertTrue(ready.shieldEnabled)

        controller.setShieldMode(false)
        assertEquals(false, moderationApi.lastShieldToggle) // the toggle is sent to the api
    }

    @Test
    fun load_surfaces_blocked_terms_and_removing_one_calls_the_api() = runTest {
        val moderationApi =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                blockedTermsResult = ApiResult.Ok(listOf("badword", "slur")),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()

        // No bans/log, but blocked terms keep the page Ready so they're visible + removable.
        val ready: ModerationState.Ready = controller.state.value as ModerationState.Ready
        assertEquals(listOf("badword", "slur"), ready.blockedTerms)

        controller.removeBlockedTerm("badword")
        assertEquals(listOf("badword"), moderationApi.removedTerms) // the term is sent to the api
    }

    @Test
    fun adding_a_blocked_term_calls_the_api_with_the_term() = runTest {
        val moderationApi = FakeModerationApi(bansResults = listOf(ApiResult.Ok(emptyList())))
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )
        controller.load()

        controller.addBlockedTerm("badword")
        assertEquals(listOf("badword"), moderationApi.addedTerms)
    }

    @Test
    fun load_surfaces_the_automod_config_and_an_enabled_filter_keeps_the_page_ready() = runTest {
        val moderationApi =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                automodResult =
                    ApiResult.Ok(
                        AutomodConfig(capsFilter = AutomodCapsFilter(enabled = true, threshold = 75))
                    ),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()

        // No bans/log/terms, but an enabled AutoMod filter keeps the page Ready so the config shows.
        val ready: ModerationState.Ready = controller.state.value as ModerationState.Ready
        assertTrue(ready.automod.capsFilter.enabled)
        assertEquals(75, ready.automod.capsFilter.threshold)
    }

    @Test
    fun toggling_an_automod_filter_saves_the_whole_config_with_that_filter_flipped() = runTest {
        val moderationApi =
            FakeModerationApi(
                // A ban keeps the page Ready; the caps filter starts disabled so the toggle enables it.
                bansResults = listOf(ApiResult.Ok(listOf(BannedUser(id = "u1", username = "troll")))),
                automodResult =
                    ApiResult.Ok(
                        AutomodConfig(
                            capsFilter = AutomodCapsFilter(enabled = false, threshold = 80)
                        )
                    ),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )
        controller.load()

        controller.toggleAutomodFilter(AutomodFilter.Caps)

        // The whole config is saved with caps flipped on; its threshold rides along unchanged.
        val saved: AutomodConfig = moderationApi.lastSavedAutomod!!
        assertTrue(saved.capsFilter.enabled)
        assertEquals(80, saved.capsFilter.threshold)
    }

    @Test
    fun load_is_empty_when_no_one_is_banned() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeModerationApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Error)
    }

    @Test
    fun load_errors_when_the_bans_call_fails() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Error)
    }

    @Test
    fun unban_lifts_the_ban_then_reloads_the_remaining_list() = runTest {
        val trolly =
            BannedUser(id = "u1", username = "trolly", displayName = "Trolly", reason = "Spam")
        val griefer =
            BannedUser(id = "u2", username = "griefer", displayName = "Griefer", reason = "Raid")
        val moderationApi =
            FakeModerationApi(
                // First load returns both; after the unban succeeds the reload returns only the other one.
                bansResults =
                    listOf(ApiResult.Ok(listOf(trolly, griefer)), ApiResult.Ok(listOf(griefer))),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()
        controller.unban("u1")

        // The unban hit the real route with the resolved channel + the user's id.
        assertEquals(listOf("ch1" to "u1"), moderationApi.unbanCalls)
        // The list reloaded and now reflects the post-unban state — the unbanned viewer is gone.
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        val bans: List<BannedUser> = (state as ModerationState.Ready).bans
        assertEquals(listOf("u2"), bans.map { it.id })
        assertNull(state.actionError)
    }

    @Test
    fun unban_surfaces_the_error_and_keeps_the_list_when_it_fails() = runTest {
        val trolly =
            BannedUser(id = "u1", username = "trolly", displayName = "Trolly", reason = "Spam")
        val moderationApi =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(listOf(trolly))),
                unbanResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()
        controller.unban("u1")

        assertEquals(listOf("ch1" to "u1"), moderationApi.unbanCalls)
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        // The list is untouched and the failure is surfaced on the Ready state.
        assertEquals(listOf("u1"), (state as ModerationState.Ready).bans.map { it.id })
        assertEquals("Missing scope.", state.actionError)
        // Only the initial load fetched bans; the failed unban did not trigger a reload.
        assertEquals(1, moderationApi.bansCalls)
    }

    @Test
    fun a_successful_unban_announces_success_on_the_frame() = runTest {
        val trolly = BannedUser(id = "u1", username = "trolly", displayName = "Trolly", reason = "Spam")
        val feedback = RecordingFeedback()
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(bansResults = listOf(ApiResult.Ok(listOf(trolly)), ApiResult.Ok(emptyList()))),
                feedback,
            )

        controller.load()
        controller.unban("u1")

        // Exactly one frame message, a success, with the "ban lifted" label.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_unbanned, feedback.only.label)
    }

    @Test
    fun a_failed_unban_announces_an_error_carrying_the_backend_detail() = runTest {
        val trolly = BannedUser(id = "u1", username = "trolly", displayName = "Trolly", reason = "Spam")
        val feedback = RecordingFeedback()
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(
                    bansResults = listOf(ApiResult.Ok(listOf(trolly))),
                    unbanResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
                ),
                feedback,
            )

        controller.load()
        controller.unban("u1")

        // It announced an ERROR (not a success), carrying the backend's message as the detail arg.
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_unban_failed, feedback.only.label)
        assertEquals(listOf<Any>("Missing scope."), feedback.only.formatArgs)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeModerationApi(
    private val bansResults: List<ApiResult<List<BannedUser>>>,
    private val unbanResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val modLogResult: ApiResult<List<ModLogEntry>> = ApiResult.Ok(emptyList()),
    private val shieldResult: ApiResult<ShieldStatus> = ApiResult.Ok(ShieldStatus(false)),
    private val blockedTermsResult: ApiResult<List<String>> = ApiResult.Ok(emptyList()),
    private val automodResult: ApiResult<AutomodConfig> = ApiResult.Ok(AutomodConfig()),
) : ModerationApi {
    // Single-result convenience for the read-only tests (one bans() result, default-OK unban).
    constructor(
        result: ApiResult<List<BannedUser>>
    ) : this(bansResults = listOf(result))

    var bansCalls: Int = 0
        private set

    val unbanCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun bans(channelId: String): ApiResult<List<BannedUser>> {
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(bansCalls, bansResults.lastIndex)
        bansCalls += 1
        return bansResults[index]
    }

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> {
        unbanCalls.add(channelId to userId)
        return unbanResult
    }

    override suspend fun modLog(channelId: String): ApiResult<List<ModLogEntry>> = modLogResult

    var lastShieldToggle: Boolean? = null
        private set

    override suspend fun shieldMode(channelId: String): ApiResult<ShieldStatus> = shieldResult

    override suspend fun setShieldMode(channelId: String, enabled: Boolean): ApiResult<Unit> {
        lastShieldToggle = enabled
        return ApiResult.Ok(Unit)
    }

    override suspend fun blockedTerms(channelId: String): ApiResult<List<String>> =
        blockedTermsResult

    val addedTerms: MutableList<String> = mutableListOf()

    override suspend fun addBlockedTerm(channelId: String, term: String): ApiResult<Unit> {
        addedTerms.add(term)
        return ApiResult.Ok(Unit)
    }

    val removedTerms: MutableList<String> = mutableListOf()

    override suspend fun removeBlockedTerm(channelId: String, term: String): ApiResult<Unit> {
        removedTerms.add(term)
        return ApiResult.Ok(Unit)
    }

    override suspend fun automod(channelId: String): ApiResult<AutomodConfig> = automodResult

    var lastSavedAutomod: AutomodConfig? = null
        private set

    override suspend fun saveAutomod(channelId: String, config: AutomodConfig): ApiResult<Unit> {
        lastSavedAutomod = config
        return ApiResult.Ok(Unit)
    }
}
