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
import bot.nomnomz.dashboard.core.network.CreateModerationRuleBody
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.ShieldStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ModerationActionLog
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ModerationActionResult
import bot.nomnomz.dashboard.core.network.NetworkBanResult
import bot.nomnomz.dashboard.core.network.UnbanRequest
import bot.nomnomz.dashboard.core.network.ViewerReport
import bot.nomnomz.dashboard.core.network.UserModerationContext
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
    fun openUserContext_loads_the_viewers_recorded_history_then_close_clears_it() = runTest {
        val context =
            UserModerationContext(
                userId = "u1",
                username = "trolly",
                banCount = 2,
                timeoutCount = 3,
                recentActions =
                    listOf(
                        ModerationActionLog(
                            id = "a1",
                            action = "ban",
                            moderatorUsername = "ModBot",
                            reason = "spam",
                        )
                    ),
            )
        val api =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                userContextResult = ApiResult.Ok(context),
            )
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load() // resolves the channel so the per-user lookup targets it

        assertNull(controller.userContext.value)

        controller.openUserContext("u1")

        val state: UserContextState? = controller.userContext.value
        assertTrue(state is UserContextState.Ready)
        assertEquals(context, (state as UserContextState.Ready).context)
        assertEquals(2, state.context.banCount)
        assertEquals(1, state.context.recentActions.size)
        assertEquals(listOf("ch1" to "u1"), api.userContextCalls)

        controller.closeUserContext()
        assertNull(controller.userContext.value)
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
    fun load_surfaces_filter_rules_and_toggle_delete_call_the_api() = runTest {
        val moderationApi =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                rulesResult =
                    ApiResult.Ok(
                        listOf(
                            ModerationRule(id = 7, name = "No links", type = "link", isEnabled = true)
                        )
                    ),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )
        controller.load()

        // A rule keeps the page Ready even with no bans/log; it lists, toggles, and deletes through the api.
        val ready: ModerationState.Ready = controller.state.value as ModerationState.Ready
        assertEquals(1, ready.rules.size)
        assertEquals("No links", ready.rules.first().name)

        controller.toggleRule(7, enabled = false)
        assertEquals(7 to false, moderationApi.lastRuleToggle)

        controller.deleteRule(7)
        assertEquals(listOf(7), moderationApi.deletedRules)
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

    @Test
    fun warn_records_the_reason_and_reloads_the_rap_sheet() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(listOf(BannedUser(id = "u1", username = "troll"))))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()
        controller.openUserContext("123")

        controller.warn("123", "spamming links")

        // The warn hit the API with exactly the reason, and the rap sheet reloaded (the open + the reload).
        assertEquals(listOf("123" to "spamming links"), api.warned)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun warn_surfaces_the_backend_message_when_the_action_is_refused() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(listOf(BannedUser(id = "u1"))))
        api.warnResult = ApiResult.Ok(ModerationActionResult(success = false, message = "Missing scope."))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.warn("123", "spamming links")

        // A success=false result surfaces the backend's message rather than silently pretending it worked.
        assertEquals(
            "Missing scope.",
            (controller.state.value as? ModerationState.Ready)?.actionError,
        )
    }

    @Test
    fun set_suspicious_records_the_status_and_reloads_the_rap_sheet() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(listOf(BannedUser(id = "u1"))))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()
        controller.openUserContext("123")

        controller.setSuspicious("123", "restricted")

        assertEquals(listOf("123" to "restricted"), api.suspiciousSet)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun load_surfaces_pending_unban_requests() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.unbanRequestsResult =
            ApiResult.Ok(listOf(UnbanRequest(id = "r1", userLogin = "troll", text = "sorry")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)

        controller.load()

        // The page renders (not Empty) because there's a pending appeal, and it carries the request.
        assertEquals(
            listOf("r1"),
            (controller.state.value as? ModerationState.Ready)?.unbanRequests?.map { it.id },
        )
    }

    @Test
    fun resolve_unban_request_records_the_decision_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.unbanRequestsResult =
            ApiResult.Ok(listOf(UnbanRequest(id = "r1", userLogin = "troll", text = "sorry")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.resolveUnbanRequest("r1", approve = true, note = null)

        // The resolve hit the API with exactly the decision (approve + note).
        assertEquals(listOf(Triple("r1", true, null as String?)), api.resolvedUnban)
    }

    @Test
    fun network_unban_records_the_target_and_scope_then_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(listOf(BannedUser(id = "u1", username = "troll"))))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.networkUnban("u1", "all_moderated")

        // The un-nuke hit the API with the target + scope, and the list reloaded (initial + post-write).
        assertEquals(listOf("u1" to "all_moderated"), api.networkUnbanned)
        assertEquals(2, api.bansCalls)
    }

    @Test
    fun load_surfaces_open_viewer_reports() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.reportsResult =
            ApiResult.Ok(
                listOf(
                    ViewerReport(
                        id = "rep1",
                        reportedTwitchUserId = "999",
                        reportedUsername = "spammer",
                        reason = "posting scam links",
                        status = "open",
                    )
                )
            )
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)

        controller.load()

        // The page renders (not Empty) because there's an open report, and it carries the report.
        assertEquals(
            listOf("rep1"),
            (controller.state.value as? ModerationState.Ready)?.reports?.map { it.id },
        )
    }

    @Test
    fun resolve_report_records_the_action_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.reportsResult =
            ApiResult.Ok(listOf(ViewerReport(id = "rep1", reportedUsername = "spammer", status = "open")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.resolveReport("rep1", "escalate")

        // The triage hit the API with exactly the report id + action, and the queue reloaded.
        assertEquals(listOf("rep1" to "escalate"), api.resolvedReports)
        assertEquals(2, api.reportsCalls)
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

private class FakeModerationApi(
    private val bansResults: List<ApiResult<List<BannedUser>>>,
    private val unbanResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val modLogResult: ApiResult<List<ModLogEntry>> = ApiResult.Ok(emptyList()),
    private val shieldResult: ApiResult<ShieldStatus> = ApiResult.Ok(ShieldStatus(false)),
    private val blockedTermsResult: ApiResult<List<String>> = ApiResult.Ok(emptyList()),
    private val automodResult: ApiResult<AutomodConfig> = ApiResult.Ok(AutomodConfig()),
    private val rulesResult: ApiResult<List<ModerationRule>> = ApiResult.Ok(emptyList()),
    private val userContextResult: ApiResult<UserModerationContext> = ApiResult.Ok(UserModerationContext()),
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

    val userContextCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun userContext(channelId: String, userId: String): ApiResult<UserModerationContext> {
        userContextCalls.add(channelId to userId)
        return userContextResult
    }

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

    override suspend fun rules(channelId: String): ApiResult<List<ModerationRule>> = rulesResult

    var lastRuleToggle: Pair<Int, Boolean>? = null
        private set

    override suspend fun setRuleEnabled(
        channelId: String,
        ruleId: Int,
        enabled: Boolean,
    ): ApiResult<Unit> {
        lastRuleToggle = ruleId to enabled
        return ApiResult.Ok(Unit)
    }

    val deletedRules: MutableList<Int> = mutableListOf()

    override suspend fun deleteRule(channelId: String, ruleId: Int): ApiResult<Unit> {
        deletedRules.add(ruleId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun createRule(channelId: String, body: CreateModerationRuleBody): ApiResult<ModerationRule> =
        ApiResult.Ok(ModerationRule(id = 999, name = body.name, isEnabled = true))

    override suspend fun performAction(
        channelId: String,
        action: String,
        targetUserId: String,
        durationSeconds: Int?,
        reason: String?,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun stats(channelId: String): ApiResult<ModerationStats> = ApiResult.Ok(ModerationStats())

    override suspend fun announce(channelId: String, message: String, color: String?): ApiResult<Unit> = ApiResult.Ok(Unit)

    val warned: MutableList<Pair<String, String>> = mutableListOf()
    var warnResult: ApiResult<ModerationActionResult> = ApiResult.Ok(ModerationActionResult(success = true))

    override suspend fun warn(
        channelId: String,
        userId: String,
        reason: String,
    ): ApiResult<ModerationActionResult> {
        warned.add(userId to reason)
        return warnResult
    }

    val suspiciousSet: MutableList<Pair<String, String>> = mutableListOf()
    val suspiciousCleared: MutableList<String> = mutableListOf()

    override suspend fun setSuspicious(channelId: String, userId: String, status: String): ApiResult<Unit> {
        suspiciousSet.add(userId to status)
        return ApiResult.Ok(Unit)
    }

    override suspend fun clearSuspicious(channelId: String, userId: String): ApiResult<Unit> {
        suspiciousCleared.add(userId)
        return ApiResult.Ok(Unit)
    }

    var unbanRequestsResult: ApiResult<List<UnbanRequest>> = ApiResult.Ok(emptyList<UnbanRequest>())
    val resolvedUnban: MutableList<Triple<String, Boolean, String?>> = mutableListOf()

    override suspend fun unbanRequests(channelId: String): ApiResult<List<UnbanRequest>> = unbanRequestsResult

    override suspend fun resolveUnbanRequest(
        channelId: String,
        requestId: String,
        approve: Boolean,
        note: String?,
    ): ApiResult<Unit> {
        resolvedUnban.add(Triple(requestId, approve, note))
        return ApiResult.Ok(Unit)
    }

    val networkUnbanned: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun networkUnban(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
    ): ApiResult<NetworkBanResult> {
        networkUnbanned.add(targetTwitchUserId to scope)
        return ApiResult.Ok(NetworkBanResult(attempted = 1, succeeded = 1))
    }

    var reportsResult: ApiResult<List<ViewerReport>> = ApiResult.Ok(emptyList<ViewerReport>())
    val resolvedReports: MutableList<Pair<String, String>> = mutableListOf()
    var reportsCalls: Int = 0

    override suspend fun reports(channelId: String): ApiResult<List<ViewerReport>> {
        reportsCalls++
        return reportsResult
    }

    override suspend fun resolveReport(
        channelId: String,
        reportId: String,
        action: String,
    ): ApiResult<Unit> {
        resolvedReports.add(reportId to action)
        return ApiResult.Ok(Unit)
    }
}
