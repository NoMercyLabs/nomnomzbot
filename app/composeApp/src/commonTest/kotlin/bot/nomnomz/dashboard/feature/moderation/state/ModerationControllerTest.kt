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
import bot.nomnomz.dashboard.core.network.ChatFilter
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.CreateChatFilterBody
import bot.nomnomz.dashboard.core.network.UpdateChatFilterBody
import bot.nomnomz.dashboard.core.network.CreateModerationRuleBody
import bot.nomnomz.dashboard.core.network.EscalationLadderStep
import bot.nomnomz.dashboard.core.network.EscalationPolicy
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.ResolvedAutomodQueueItem
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationStanding
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.NetworkNukeBatch
import bot.nomnomz.dashboard.core.network.NetworkNukeBody
import bot.nomnomz.dashboard.core.network.SaveSharedBanSettingsBody
import bot.nomnomz.dashboard.core.network.SetModerationStandingBody
import bot.nomnomz.dashboard.core.network.SharedBanSettings
import bot.nomnomz.dashboard.core.network.SharedBanTrustedChannel
import bot.nomnomz.dashboard.core.network.UpsertEscalationPolicyBody
import bot.nomnomz.dashboard.core.network.ShieldStatus
import bot.nomnomz.dashboard.core.network.TRUST_POLICY_DEFAULTS
import bot.nomnomz.dashboard.core.network.TrustApi
import bot.nomnomz.dashboard.core.network.TrustPolicy
import bot.nomnomz.dashboard.core.network.TwitchAutoModSettings
import bot.nomnomz.dashboard.core.network.UpdateTrustPolicyBody
import bot.nomnomz.dashboard.core.network.UpdateTwitchAutoModSettingsBody
import bot.nomnomz.dashboard.core.network.asUpdateBody
import bot.nomnomz.dashboard.feature.moderation.ui.formatTrustValue
import kotlin.test.assertNotNull
import bot.nomnomz.dashboard.core.network.ShoutoutOverride
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ModerationActionLog
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ModerationActionResult
import bot.nomnomz.dashboard.core.network.Moderator
import bot.nomnomz.dashboard.core.network.NetworkBanResult
import bot.nomnomz.dashboard.core.network.UnbanRequest
import bot.nomnomz.dashboard.core.network.ViewerReport
import bot.nomnomz.dashboard.core.network.UserModerationContext
import bot.nomnomz.dashboard.core.network.UserNote
import bot.nomnomz.dashboard.core.network.ViewerOption
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_unban_failed
import nomnomzbot.composeapp.generated.resources.feedback_unbanned
import bot.nomnomz.dashboard.core.realtime.HubAutoModQueueChange
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher

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
                FakeCommunityApi(),
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
        val api = FakeModerationApi(bansResults = listOf(ApiResult.Ok(emptyList())))
        api.userContextResult = ApiResult.Ok(context)
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Error)
    }

    @Test
    fun bans_read_failure_renders_unavailable_not_an_empty_or_errored_page() = runTest {
        // Bans read LIVE Twitch state, so a failure (missing scope / bot not installed here) is NOT a page error
        // and NOT "no bans" — it's an unavailable section. The page still renders (Ready) so the rest is usable and
        // the needs-permission notice shows in place of the ban list.
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope."))),
                FakeCommunityApi(),
            )

        controller.load()

        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        assertFalse((state as ModerationState.Ready).bansAvailable)
    }

    @Test
    fun shield_and_blocked_terms_read_failures_render_unavailable_not_off_or_empty() = runTest {
        // A shield/terms read failure must not phantom-lie "off"/"no terms" — it flags the section unavailable.
        val api =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                shieldResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
                blockedTermsResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

        controller.load()

        // Even though every list is empty and shield reads off, the page renders Ready (not Empty) with the two
        // sections marked unavailable so their notices show.
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        assertFalse((state as ModerationState.Ready).shieldAvailable)
        assertFalse(state.blockedTermsAvailable)
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
                FakeCommunityApi(),
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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.resolveUnbanRequest("r1", approve = true, note = null)

        // The resolve hit the API with exactly the decision (approve + note).
        assertEquals(listOf(Triple("r1", true, null as String?)), api.resolvedUnban)
    }

    @Test
    fun network_unban_records_the_target_and_scope_then_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(listOf(BannedUser(id = "u1", username = "troll"))))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

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
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.resolveReport("rep1", "escalate")

        // The triage hit the API with exactly the report id + action, and the queue reloaded.
        assertEquals(listOf("rep1" to "escalate"), api.resolvedReports)
        assertEquals(2, api.reportsCalls)
    }

    @Test
    fun an_automod_queue_push_refetches_the_pending_queue_live() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        // Another mod resolved q1 and a NEW message q9 was held — the push must swap in the fresh queue,
        // and the surviving item must be exactly q9 (not q1 kept by a stale count-only refresh).
        api.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q9", status = "pending")))
        events.emit(HubEvent.AutoModQueueChanged(HubAutoModQueueChange(messageId = "amsg-9", change = "held")))

        assertEquals(
            listOf("q9"),
            (controller.state.value as? ModerationState.Ready)?.automodQueue?.map { it.id },
        )
    }

    @Test
    fun an_automod_queue_push_on_an_empty_page_falls_back_to_a_full_load() = runTest {
        // The page can sit in its empty state (nothing to moderate) when the FIRST hold arrives — the
        // targeted Ready-refresh cannot apply, so the push must trigger a full load and surface the item.
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        assertTrue(controller.state.value !is ModerationState.Ready)

        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        api.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        events.emit(HubEvent.AutoModQueueChanged(HubAutoModQueueChange(messageId = "amsg-1", change = "held")))

        assertEquals(
            listOf("q1"),
            (controller.state.value as? ModerationState.Ready)?.automodQueue?.map { it.id },
        )
    }

    @Test
    fun load_surfaces_the_pending_automod_queue() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.automodQueueResult =
            ApiResult.Ok(
                listOf(
                    ModerationQueueItem(
                        id = "q1",
                        source = "automod",
                        status = "pending",
                        targetUsernameSnapshot = "chatter",
                        messageContentSnapshot = "you are all idiots",
                        autoModCategory = "aggression",
                    )
                )
            )
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

        controller.load()

        // The page renders (not Empty) because there's a pending held message, and it carries the item.
        assertEquals(
            listOf("q1"),
            (controller.state.value as? ModerationState.Ready)?.automodQueue?.map { it.id },
        )
    }

    @Test
    fun resolve_automod_queue_item_records_the_action_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.resolveAutomodQueueItem("q1", "approve")

        // The resolve hit the API with exactly the item id + action, and the queue reloaded so an approved
        // item drops off the pending list — the S066 done-when: a mod approved a held message from the dashboard.
        assertEquals(listOf("q1" to "approve"), api.resolvedAutomodQueueItems)
    }

    @Test
    fun open_user_context_loads_notes_alongside_the_rap_sheet() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        api.notesResult =
            ApiResult.Ok(listOf(UserNote(id = 1, subjectUserId = "u1", content = "watch this one", pinned = true)))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.openUserContext("u1")

        val ctx: UserContextState? = controller.userContext.value
        assertTrue(ctx is UserContextState.Ready)
        assertEquals(listOf(1), (ctx as UserContextState.Ready).notes.map { it.id })
    }

    @Test
    fun add_note_posts_the_content_and_reloads_the_panel() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        controller.openUserContext("u1")

        controller.addNote("u1", "first warning noted", pinned = true)

        // The note carried the subject + content + pinned, and the panel reloaded (open once + reload once).
        assertEquals(listOf(Triple("u1", "first warning noted", true)), api.createdNotes)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun pin_note_edits_only_the_pinned_flag_and_leaves_content_untouched() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.editNote("u1", "n1", content = null, pinned = true)

        // Pinning sends the flag with a null content (leave-unchanged), matching the backend's partial update.
        assertEquals(listOf(Triple("n1", null as String?, true as Boolean?)), api.updatedNotes)
    }

    @Test
    fun delete_note_removes_it_and_reloads_the_panel() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        controller.openUserContext("u1")

        controller.deleteNote("u1", "n1")

        assertEquals(listOf("n1"), api.deletedNotes)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun load_surfaces_the_escalation_policy_and_shared_ban_settings_on_ready() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(listOf(BannedUser(id = "u1", username = "troll"))))
        api.escalationResult =
            ApiResult.Ok(EscalationPolicy(isEnabled = true, offenseWindowHours = 168))
        api.sharedBanResult = ApiResult.Ok(SharedBanSettings(acceptSharedChatBans = true, shareOutgoingBans = false))
        api.nukeBatchesResult = ApiResult.Ok(listOf(NetworkNukeBatch(id = "b1", channelCount = 3, status = "active")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

        controller.load()

        val ready = controller.state.value as ModerationState.Ready
        assertEquals(true, ready.escalationPolicy?.isEnabled)
        assertEquals(168, ready.escalationPolicy?.offenseWindowHours)
        assertEquals(true, ready.sharedBanSettings?.acceptSharedChatBans)
        assertEquals(1, ready.nukeBatches.size)
        assertEquals("active", ready.nukeBatches.first().status)
    }

    @Test
    fun save_escalation_policy_sends_the_whole_ladder_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.saveEscalationPolicy(
            UpsertEscalationPolicyBody(
                isEnabled = true,
                ladder =
                    listOf(
                        EscalationLadderStep(atOffense = 1, action = "warn"),
                        EscalationLadderStep(atOffense = 2, action = "timeout", timeoutSeconds = 600),
                    ),
                offenseWindowHours = 24,
                countAutoModViolations = true,
            )
        )

        // The whole ladder + settings reached the API, and the page reloaded (initial load + post-save reload).
        assertEquals(2, api.savedEscalation?.ladder?.size)
        assertEquals("timeout", api.savedEscalation?.ladder?.get(1)?.action)
        assertEquals(24, api.savedEscalation?.offenseWindowHours)
        assertEquals(2, api.bansCalls)
    }

    @Test
    fun forgive_user_resets_the_ladder_and_reloads_the_panel() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        controller.openUserContext("u1")

        controller.forgiveUser("u1")

        assertEquals(listOf("u1"), api.resetEscalations)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun set_heat_threshold_resends_the_full_automod_config() = runTest {
        val api =
            FakeModerationApi(
                // A banned viewer makes load() resolve to Ready (setHeatTimeoutThreshold reads the current config).
                bansResults = listOf(ApiResult.Ok(listOf(BannedUser(id = "u1")))),
                automodResult = ApiResult.Ok(AutomodConfig(heatTimeoutThreshold = 80)),
            )
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.setHeatTimeoutThreshold(50)

        // The whole config was re-sent with only the threshold changed.
        assertEquals(50, api.lastSavedAutomod?.heatTimeoutThreshold)
    }

    @Test
    fun network_nuke_forces_confirmation_and_records_the_target() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.networkNuke("victim42", reason = "raid", matchTerm = null)

        assertEquals(1, api.nuked.size)
        assertEquals("victim42", api.nuked.first().targetTwitchUserId)
        // The single-confirmation guardrail is always asserted true on the wire.
        assertTrue(api.nuked.first().requireConfirmation)
        assertEquals(2, api.bansCalls)
    }

    @Test
    fun save_shared_ban_settings_sends_both_switches_explicitly() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.saveSharedBanSettings(accept = true, share = false)

        assertEquals(true to false, api.savedSharedBans)
        assertEquals(2, api.bansCalls)
    }

    @Test
    fun add_trusted_channel_records_the_id_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.addTrustedChannel("partner-ulid")

        assertEquals(listOf("partner-ulid"), api.addedTrusted)
        assertEquals(2, api.bansCalls)
    }

    @Test
    fun set_standing_sends_provider_and_tier_then_reloads_the_panel() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        controller.openUserContext("u1")

        controller.setStanding("u1", provider = "twitch", standing = "muted", reason = "spam")

        assertEquals(listOf(Triple("u1", "twitch", "muted")), api.standingsSet)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun clear_standing_restores_normal_and_reloads_the_panel() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.userContextResult = ApiResult.Ok(UserModerationContext(userId = "u1"))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        controller.openUserContext("u1")

        controller.clearStanding("u1", provider = "twitch")

        assertEquals(listOf("u1" to "twitch"), api.standingsCleared)
        assertEquals(2, api.userContextCalls.size)
    }

    @Test
    fun load_surfaces_the_channels_chat_filters_on_success() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        api.chatFiltersResult =
            ApiResult.Ok(
                listOf(
                    ChatFilter(id = "f1", filterType = "Regex", name = "No links", action = "Delete", isEnabled = true)
                )
            )
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

        controller.load()

        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        val filters: List<ChatFilter> = (state as ModerationState.Ready).chatFilters
        assertEquals(1, filters.size)
        assertEquals("No links", filters.first().name)
        assertTrue(filters.first().isEnabled)
    }

    @Test
    fun create_chat_filter_sends_the_pattern_and_reloads_the_list() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()
        api.chatFiltersResult =
            ApiResult.Ok(listOf(ChatFilter(id = "f1", filterType = "Regex", name = "No links", action = "Delete")))

        controller.createChatFilter(
            filterType = "Regex",
            name = "No links",
            action = "Delete",
            pattern = "https?://",
            terms = null,
            timeoutSeconds = null,
        )

        assertEquals(1, api.createdChatFilters.size)
        assertEquals("https?://", api.createdChatFilters.first().pattern)
        assertEquals("Regex", api.createdChatFilters.first().filterType)
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        assertEquals(1, (state as ModerationState.Ready).chatFilters.size)
    }

    @Test
    fun toggle_chat_filter_flips_enabled_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.toggleChatFilter("f1", enabled = false)

        assertEquals(1, api.updatedChatFilters.size)
        assertEquals("f1", api.updatedChatFilters.first().first)
        assertEquals(false, api.updatedChatFilters.first().second.isEnabled)
    }

    @Test
    fun delete_chat_filter_removes_it_and_reloads() = runTest {
        val api = FakeModerationApi(ApiResult.Ok(emptyList()))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.deleteChatFilter("f1")

        assertEquals(listOf("f1"), api.deletedChatFilters)
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

private class FakeCommunityApi(
    private val searchResult: ApiResult<List<ViewerOption>> = ApiResult.Ok(emptyList()),
) : CommunityApi {
    override suspend fun stats(channelId: String): ApiResult<CommunityStats> =
        ApiResult.Ok(CommunityStats())

    override suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember> =
        ApiResult.Ok(CommunityMember(id = userId))

    override suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int,
    ): ApiResult<List<ViewerOption>> = searchResult

    override suspend fun members(channelId: String) = error("stub")
    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ) = error("stub")
    override suspend fun topChatters(channelId: String) = error("stub")
    override suspend fun setTrust(channelId: String, userId: String, level: String) = error("stub")
    override suspend fun ban(channelId: String, userId: String, reason: String) = error("stub")
    override suspend fun unban(channelId: String, userId: String) = error("stub")
    override suspend fun addVip(channelId: String, userId: String) = error("stub")
    override suspend fun removeVip(channelId: String, userId: String) = error("stub")
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String) = error("stub")
}

// S066-mod-actions: the moderator roster loads into state, and add/remove/clear-chat each call the
// corresponding client method with the right arguments and reload (add/remove) or leave state be (clear
// chat, which is not part of this screen's own state).
class ModerationControllerModeratorTests {

    @Test
    fun load_surfaces_the_channels_current_moderators() = runTest {
        val api =
            FakeModerationApi(bansResults = listOf(ApiResult.Ok(emptyList())))
        api.moderatorsResult = ApiResult.Ok(listOf(Moderator(userId = "u9", username = "modperson")))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())

        controller.load()

        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        val moderators: List<Moderator> = (state as ModerationState.Ready).moderators
        assertEquals(1, moderators.size)
        assertEquals("modperson", moderators.first().username)
    }

    @Test
    fun addModerator_callsTheApiWithTheTargetId_thenReloads() = runTest {
        val api = FakeModerationApi(bansResults = listOf(ApiResult.Ok(emptyList())))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        api.moderatorsResult = ApiResult.Ok(listOf(Moderator(userId = "u9", username = "newmod")))
        controller.addModerator("u9")

        assertEquals(listOf("u9"), api.addedModerators)
        // load() ran again after the write (afterWrite reloads on success) — proven by the reloaded
        // roster reflecting the newly-granted moderator rather than the pre-write (empty) snapshot.
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        assertEquals(listOf("newmod"), (state as ModerationState.Ready).moderators.map { it.username })
    }

    @Test
    fun removeModerator_callsTheApiWithTheUserId_thenReloads() = runTest {
        val api = FakeModerationApi(bansResults = listOf(ApiResult.Ok(emptyList())))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.removeModerator("u9")

        assertEquals(listOf("u9"), api.removedModerators)
    }

    @Test
    fun clearChat_callsTheApi() = runTest {
        val api = FakeModerationApi(bansResults = listOf(ApiResult.Ok(emptyList())))
        val controller = ModerationController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeCommunityApi())
        controller.load()

        controller.clearChat()

        assertEquals(1, api.clearChatCalls)
    }
}

private class FakeModerationApi(
    private val bansResults: List<ApiResult<List<BannedUser>>>,
    private val unbanResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val modLogResult: ApiResult<List<ModLogEntry>> = ApiResult.Ok(emptyList()),
    private val shieldResult: ApiResult<ShieldStatus> = ApiResult.Ok(ShieldStatus(false)),
    private val blockedTermsResult: ApiResult<List<String>> = ApiResult.Ok(emptyList()),
    private val automodResult: ApiResult<AutomodConfig> = ApiResult.Ok(AutomodConfig()),
    private val rulesResult: ApiResult<List<ModerationRule>> = ApiResult.Ok(emptyList()),
) : ModerationApi {
    var chatFiltersResult: ApiResult<List<ChatFilter>> = ApiResult.Ok(emptyList())

    override suspend fun chatFilters(channelId: String): ApiResult<List<ChatFilter>> = chatFiltersResult

    val createdChatFilters: MutableList<CreateChatFilterBody> = mutableListOf()
    var createChatFilterResult: ApiResult<ChatFilter> = ApiResult.Ok(ChatFilter())

    override suspend fun createChatFilter(
        channelId: String,
        body: CreateChatFilterBody,
    ): ApiResult<ChatFilter> {
        createdChatFilters.add(body)
        return createChatFilterResult
    }

    val updatedChatFilters: MutableList<Pair<String, UpdateChatFilterBody>> = mutableListOf()
    var updateChatFilterResult: ApiResult<ChatFilter> = ApiResult.Ok(ChatFilter())

    override suspend fun updateChatFilter(
        channelId: String,
        filterId: String,
        body: UpdateChatFilterBody,
    ): ApiResult<ChatFilter> {
        updatedChatFilters.add(filterId to body)
        return updateChatFilterResult
    }

    val deletedChatFilters: MutableList<String> = mutableListOf()

    override suspend fun deleteChatFilter(channelId: String, filterId: String): ApiResult<Unit> {
        deletedChatFilters.add(filterId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun shoutoutTemplate(channelId: String): ApiResult<String?> = ApiResult.Ok(null)

    override suspend fun setShoutoutTemplate(channelId: String, template: String?): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun shoutoutOverrides(channelId: String): ApiResult<List<ShoutoutOverride>> =
        ApiResult.Ok(emptyList())

    override suspend fun setShoutoutOverride(
        channelId: String,
        targetTwitchUserId: String,
        targetDisplayName: String,
        messageTemplate: String,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteShoutoutOverride(channelId: String, targetTwitchUserId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

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
    var userContextResult: ApiResult<UserModerationContext> = ApiResult.Ok(UserModerationContext())

    override suspend fun userContext(channelId: String, userId: String): ApiResult<UserModerationContext> {
        userContextCalls.add(channelId to userId)
        return userContextResult
    }

    var notesResult: ApiResult<List<UserNote>> = ApiResult.Ok(emptyList<UserNote>())
    val createdNotes: MutableList<Triple<String, String, Boolean>> = mutableListOf()
    val updatedNotes: MutableList<Triple<String, String?, Boolean?>> = mutableListOf()
    val deletedNotes: MutableList<String> = mutableListOf()

    override suspend fun notesFor(channelId: String, userId: String): ApiResult<List<UserNote>> = notesResult

    override suspend fun createNote(
        channelId: String,
        userId: String,
        content: String,
        pinned: Boolean,
    ): ApiResult<Unit> {
        createdNotes.add(Triple(userId, content, pinned))
        return ApiResult.Ok(Unit)
    }

    override suspend fun updateNote(
        channelId: String,
        noteId: String,
        content: String?,
        pinned: Boolean?,
    ): ApiResult<Unit> {
        updatedNotes.add(Triple(noteId, content, pinned))
        return ApiResult.Ok(Unit)
    }

    override suspend fun deleteNote(channelId: String, noteId: String): ApiResult<Unit> {
        deletedNotes.add(noteId)
        return ApiResult.Ok(Unit)
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

    var twitchAutoModResult: ApiResult<TwitchAutoModSettings> = ApiResult.Ok(TwitchAutoModSettings())
    var savedTwitchAutoMod: UpdateTwitchAutoModSettingsBody? = null
        private set

    override suspend fun twitchAutoMod(channelId: String): ApiResult<TwitchAutoModSettings> =
        twitchAutoModResult

    override suspend fun saveTwitchAutoMod(
        channelId: String,
        body: UpdateTwitchAutoModSettingsBody,
    ): ApiResult<TwitchAutoModSettings> {
        savedTwitchAutoMod = body
        return ApiResult.Ok(
            TwitchAutoModSettings(
                overallLevel = body.overallLevel,
                aggression = body.aggression ?: 0,
                bullying = body.bullying ?: 0,
                disability = body.disability ?: 0,
                misogyny = body.misogyny ?: 0,
                raceEthnicityOrReligion = body.raceEthnicityOrReligion ?: 0,
                sexBasedTerms = body.sexBasedTerms ?: 0,
                sexualitySexOrGender = body.sexualitySexOrGender ?: 0,
                swearing = body.swearing ?: 0,
            )
        )
    }

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

    var automodQueueResult: ApiResult<List<ModerationQueueItem>> =
        ApiResult.Ok(emptyList<ModerationQueueItem>())
    var resolveAutomodQueueItemResult: ApiResult<ResolvedAutomodQueueItem> =
        ApiResult.Ok(ResolvedAutomodQueueItem(item = ModerationQueueItem()))
    val resolvedAutomodQueueItems: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun automodQueue(
        channelId: String,
        status: String?,
    ): ApiResult<List<ModerationQueueItem>> = automodQueueResult

    override suspend fun resolveAutomodQueueItem(
        channelId: String,
        queueItemId: String,
        action: String,
        followUp: String?,
        timeoutSeconds: Int?,
        reason: String?,
    ): ApiResult<ResolvedAutomodQueueItem> {
        resolvedAutomodQueueItems.add(queueItemId to action)
        return resolveAutomodQueueItemResult
    }

    var escalationResult: ApiResult<EscalationPolicy> = ApiResult.Ok(EscalationPolicy())
    var savedEscalation: UpsertEscalationPolicyBody? = null
    val resetEscalations: MutableList<String> = mutableListOf()

    override suspend fun escalationPolicy(channelId: String): ApiResult<EscalationPolicy> = escalationResult

    override suspend fun saveEscalationPolicy(
        channelId: String,
        body: UpsertEscalationPolicyBody,
    ): ApiResult<EscalationPolicy> {
        savedEscalation = body
        return ApiResult.Ok(
            EscalationPolicy(
                isEnabled = body.isEnabled,
                ladder = body.ladder,
                offenseWindowHours = body.offenseWindowHours,
                countAutoModViolations = body.countAutoModViolations,
            )
        )
    }

    override suspend fun resetEscalation(channelId: String, userId: String): ApiResult<Unit> {
        resetEscalations.add(userId)
        return ApiResult.Ok(Unit)
    }

    var nukeBatchesResult: ApiResult<List<NetworkNukeBatch>> = ApiResult.Ok(emptyList())
    val nuked: MutableList<NetworkNukeBody> = mutableListOf()
    val revertedNukes: MutableList<String> = mutableListOf()

    override suspend fun nukeBatches(channelId: String): ApiResult<List<NetworkNukeBatch>> = nukeBatchesResult

    override suspend fun networkNuke(channelId: String, body: NetworkNukeBody): ApiResult<NetworkNukeBatch> {
        nuked.add(body)
        return ApiResult.Ok(
            NetworkNukeBatch(id = "batch", targetTwitchUserId = body.targetTwitchUserId, status = "active")
        )
    }

    override suspend fun revertNuke(channelId: String, batchId: String): ApiResult<NetworkNukeBatch> {
        revertedNukes.add(batchId)
        return ApiResult.Ok(NetworkNukeBatch(id = batchId, status = "reverted"))
    }

    var sharedBanResult: ApiResult<SharedBanSettings> = ApiResult.Ok(SharedBanSettings())
    var savedSharedBans: Pair<Boolean, Boolean>? = null
    val addedTrusted: MutableList<String> = mutableListOf()
    val removedTrusted: MutableList<String> = mutableListOf()

    override suspend fun sharedBanSettings(channelId: String): ApiResult<SharedBanSettings> = sharedBanResult

    override suspend fun saveSharedBanSettings(
        channelId: String,
        body: SaveSharedBanSettingsBody,
    ): ApiResult<SharedBanSettings> {
        savedSharedBans = body.acceptSharedChatBans to body.shareOutgoingBans
        return ApiResult.Ok(SharedBanSettings(body.acceptSharedChatBans, body.shareOutgoingBans))
    }

    override suspend fun addTrustedChannel(
        channelId: String,
        trustedChannelId: String,
    ): ApiResult<SharedBanTrustedChannel> {
        addedTrusted.add(trustedChannelId)
        return ApiResult.Ok(SharedBanTrustedChannel(trustedChannelId = trustedChannelId))
    }

    override suspend fun removeTrustedChannel(
        channelId: String,
        trustedChannelId: String,
    ): ApiResult<Unit> {
        removedTrusted.add(trustedChannelId)
        return ApiResult.Ok(Unit)
    }

    val standingsSet: MutableList<Triple<String, String, String>> = mutableListOf()
    val standingsCleared: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun setStanding(
        channelId: String,
        userId: String,
        body: SetModerationStandingBody,
    ): ApiResult<ModerationStanding> {
        standingsSet.add(Triple(userId, body.provider, body.standing))
        return ApiResult.Ok(
            ModerationStanding(userId = userId, provider = body.provider, standing = body.standing)
        )
    }

    override suspend fun clearStanding(
        channelId: String,
        userId: String,
        provider: String,
    ): ApiResult<Unit> {
        standingsCleared.add(userId to provider)
        return ApiResult.Ok(Unit)
    }

    var moderatorsResult: ApiResult<List<Moderator>> = ApiResult.Ok(emptyList<Moderator>())
    val addedModerators: MutableList<String> = mutableListOf()
    val removedModerators: MutableList<String> = mutableListOf()
    var clearChatCalls: Int = 0

    override suspend fun moderators(channelId: String): ApiResult<List<Moderator>> = moderatorsResult

    override suspend fun addModerator(channelId: String, targetTwitchUserId: String): ApiResult<Unit> {
        addedModerators.add(targetTwitchUserId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun removeModerator(channelId: String, userId: String): ApiResult<Unit> {
        removedModerators.add(userId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun clearChat(channelId: String): ApiResult<Unit> {
        clearChatCalls++
        return ApiResult.Ok(Unit)
    }
}

// ── Trust & Automation (S-OWN23 T4) ─────────────────────────────────────────────────────────────────

/**
 * The trust-policy backend, faked: it hands back a policy on read and RECORDS the body it is asked to save, so a
 * test can assert the exact payload that leaves the client (and that an invalid one never does).
 */
private class FakeTrustApi(
    var policyResult: ApiResult<TrustPolicy> = ApiResult.Ok(TrustPolicy()),
) : TrustApi {
    var savedPolicy: UpdateTrustPolicyBody? = null
        private set
    var saveCalls: Int = 0
        private set

    override suspend fun policy(channelId: String): ApiResult<TrustPolicy> = policyResult

    override suspend fun savePolicy(
        channelId: String,
        body: UpdateTrustPolicyBody,
    ): ApiResult<TrustPolicy> {
        savedPolicy = body
        saveCalls += 1
        return ApiResult.Ok(
            TrustPolicy(
                requestCountWeight = body.requestCountWeight,
                accountAgeWeight = body.accountAgeWeight,
                contentAgeWeight = body.contentAgeWeight,
                contentPopularityWeight = body.contentPopularityWeight,
                requestCountDecay = body.requestCountDecay,
                accountAgeDecay = body.accountAgeDecay,
                contentAgeDecay = body.contentAgeDecay,
                contentPopularityDecay = body.contentPopularityDecay,
                notFollowingFactor = body.notFollowingFactor,
                reputationBoostEnabled = body.reputationBoostEnabled,
                youTubeQualityPenaltyFactor = body.youTubeQualityPenaltyFactor,
                skipPenalty = body.skipPenalty,
                timeoutPenalty = body.timeoutPenalty,
                banPenalty = body.banPenalty,
                untrustedMax = body.untrustedMax,
                lowMax = body.lowMax,
                standardMax = body.standardMax,
                heatHalfLifeHours = body.heatHalfLifeHours,
                heatDeltaBan = body.heatDeltaBan,
                heatDeltaTimeout = body.heatDeltaTimeout,
                heatDeltaReportValidated = body.heatDeltaReportValidated,
                heatDeltaAutoModDenied = body.heatDeltaAutoModDenied,
                heatDeltaFilterHit = body.heatDeltaFilterHit,
                // The backend pins the channel the moment it saves its own values.
                isPinned = true,
            )
        )
    }
}

/** Proves the Trust & Automation section reads, edits, validates and persists real values. */
class TrustAutomationControllerTest {

    private fun controller(
        trust: FakeTrustApi,
        moderation: FakeModerationApi = FakeModerationApi(ApiResult.Ok(emptyList())),
    ): Pair<ModerationController, FakeModerationApi> =
        ModerationController(
            FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            moderation,
            FakeCommunityApi(),
            trustApi = trust,
        ) to moderation

    @Test
    fun load_maps_every_trust_policy_field_and_the_twitch_automod_levels() = runTest {
        val edited =
            TrustPolicy(
                requestCountWeight = 0.10,
                accountAgeWeight = 0.20,
                contentAgeWeight = 0.30,
                contentPopularityWeight = 0.40,
                requestCountDecay = 0.111,
                accountAgeDecay = 0.222,
                contentAgeDecay = 0.333,
                contentPopularityDecay = 0.0004,
                notFollowingFactor = 0.55,
                reputationBoostEnabled = false,
                youTubeQualityPenaltyFactor = 0.66,
                skipPenalty = 6.0,
                timeoutPenalty = 11.0,
                banPenalty = 31.0,
                untrustedMax = 26.0,
                lowMax = 51.0,
                standardMax = 76.0,
                heatHalfLifeHours = 12.0,
                heatDeltaBan = 41.0,
                heatDeltaTimeout = 16.0,
                heatDeltaReportValidated = 11.0,
                heatDeltaAutoModDenied = 6.0,
                heatDeltaFilterHit = 7.0,
                isPinned = true,
            )
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList()))
        moderation.twitchAutoModResult =
            ApiResult.Ok(TwitchAutoModSettings(overallLevel = null, aggression = 3, swearing = 1))
        val (subject, _) = controller(FakeTrustApi(ApiResult.Ok(edited)), moderation)

        subject.load()

        val ready = subject.state.value as ModerationState.Ready
        assertEquals(edited, ready.trustPolicy)
        assertEquals(3, ready.twitchAutoMod?.aggression)
        assertEquals(1, ready.twitchAutoMod?.swearing)
        assertNull(ready.twitchAutoMod?.overallLevel)
    }

    @Test
    fun load_leaves_the_twitch_levels_unknown_rather_than_reporting_them_off() = runTest {
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList()))
        moderation.twitchAutoModResult =
            ApiResult.Failure(
                ApiError(status = 403, code = "403", message = "Missing AutoMod settings scope.")
            )
        val (subject, _) = controller(FakeTrustApi(), moderation)

        subject.load()

        assertNull((subject.state.value as ModerationState.Ready).twitchAutoMod)
    }

    @Test
    fun saveTrustPolicy_sends_the_edited_values_and_stores_the_saved_policy() = runTest {
        val trust = FakeTrustApi()
        val (subject, _) = controller(trust)
        subject.load()

        val body =
            TRUST_POLICY_DEFAULTS.copy(banPenalty = 45.0, heatHalfLifeHours = 6.0).asUpdateBody()
        subject.saveTrustPolicy(body)

        val sent = assertNotNull(trust.savedPolicy)
        assertEquals(45.0, sent.banPenalty)
        assertEquals(6.0, sent.heatHalfLifeHours)
        // The rest of the body rides along unchanged — the PUT replaces the whole policy.
        assertEquals(TRUST_POLICY_DEFAULTS.requestCountWeight, sent.requestCountWeight)
        assertEquals(TRUST_POLICY_DEFAULTS.contentPopularityDecay, sent.contentPopularityDecay)

        val ready = subject.state.value as ModerationState.Ready
        assertEquals(45.0, ready.trustPolicy?.banPenalty)
        assertEquals(true, ready.trustPolicy?.isPinned)
        assertFalse(ready.trustWeightSumInvalid)
    }

    @Test
    fun saveTrustPolicy_blocks_a_weight_sum_that_is_not_one_and_never_reaches_the_backend() = runTest {
        val trust = FakeTrustApi()
        val (subject, _) = controller(trust)
        subject.load()

        val body = TRUST_POLICY_DEFAULTS.copy(requestCountWeight = 0.50).asUpdateBody()
        subject.saveTrustPolicy(body)

        assertNull(trust.savedPolicy)
        assertEquals(0, trust.saveCalls)
        assertTrue((subject.state.value as ModerationState.Ready).trustWeightSumInvalid)
    }

    @Test
    fun a_valid_save_after_a_blocked_one_clears_the_weight_sum_error() = runTest {
        val trust = FakeTrustApi()
        val (subject, _) = controller(trust)
        subject.load()
        subject.saveTrustPolicy(TRUST_POLICY_DEFAULTS.copy(accountAgeWeight = 0.9).asUpdateBody())
        assertTrue((subject.state.value as ModerationState.Ready).trustWeightSumInvalid)

        subject.saveTrustPolicy(TRUST_POLICY_DEFAULTS.asUpdateBody())

        val ready = subject.state.value as ModerationState.Ready
        assertFalse(ready.trustWeightSumInvalid)
        assertEquals(1, trust.saveCalls)
    }

    @Test
    fun resetting_a_field_to_its_default_restores_the_shipped_value_and_saves_it() = runTest {
        val pinned = TrustPolicy(contentPopularityDecay = 0.5, banPenalty = 99.0, isPinned = true)
        val trust = FakeTrustApi(ApiResult.Ok(pinned))
        val (subject, _) = controller(trust)
        subject.load()

        // What the per-field reset control does: put the shipped default back on that one field.
        val afterReset =
            pinned
                .copy(
                    contentPopularityDecay = TRUST_POLICY_DEFAULTS.contentPopularityDecay,
                    banPenalty = TRUST_POLICY_DEFAULTS.banPenalty,
                )
                .asUpdateBody()
        subject.saveTrustPolicy(afterReset)

        val sent = assertNotNull(trust.savedPolicy)
        assertEquals(0.0003, sent.contentPopularityDecay)
        assertEquals(30.0, sent.banPenalty)
        assertEquals(0.0003, (subject.state.value as ModerationState.Ready).trustPolicy?.contentPopularityDecay)
        // The reset control writes this text into the field — a plain decimal, never 3.0E-4.
        assertEquals("0.0003", formatTrustValue(TRUST_POLICY_DEFAULTS.contentPopularityDecay))
        assertEquals("30.0", formatTrustValue(TRUST_POLICY_DEFAULTS.banPenalty))
    }

    @Test
    fun saveTwitchAutoMod_with_categories_never_sends_an_overall_level() = runTest {
        val moderation = FakeModerationApi(ApiResult.Ok(emptyList()))
        moderation.twitchAutoModResult = ApiResult.Ok(TwitchAutoModSettings(overallLevel = 2))
        val (subject, api) = controller(FakeTrustApi(), moderation)
        subject.load()

        subject.saveTwitchAutoMod(
            UpdateTwitchAutoModSettingsBody.categories(
                aggression = 4,
                bullying = 3,
                disability = 2,
                misogyny = 1,
                raceEthnicityOrReligion = 0,
                sexBasedTerms = 1,
                sexualitySexOrGender = 2,
                swearing = 3,
            )
        )

        val sent = assertNotNull(api.savedTwitchAutoMod)
        assertNull(sent.overallLevel)
        assertEquals(4, sent.aggression)
        assertEquals(3, sent.swearing)
        assertNull((subject.state.value as ModerationState.Ready).twitchAutoMod?.overallLevel)
    }

    @Test
    fun saveTwitchAutoMod_with_an_overall_level_never_sends_categories() = runTest {
        val (subject, api) = controller(FakeTrustApi())
        subject.load()

        subject.saveTwitchAutoMod(UpdateTwitchAutoModSettingsBody.overall(3))

        val sent = assertNotNull(api.savedTwitchAutoMod)
        assertEquals(3, sent.overallLevel)
        assertNull(sent.aggression)
        assertNull(sent.bullying)
        assertNull(sent.disability)
        assertNull(sent.misogyny)
        assertNull(sent.raceEthnicityOrReligion)
        assertNull(sent.sexBasedTerms)
        assertNull(sent.sexualitySexOrGender)
        assertNull(sent.swearing)
        assertEquals(3, (subject.state.value as ModerationState.Ready).twitchAutoMod?.overallLevel)
    }

    @Test
    fun setAutoTimeoutOnHeat_persists_the_opt_in_switch_with_the_rest_of_the_config() = runTest {
        val moderation =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(emptyList())),
                automodResult = ApiResult.Ok(AutomodConfig(heatTimeoutThreshold = 70)),
            )
        val (subject, api) = controller(FakeTrustApi(), moderation)
        subject.load()

        subject.setAutoTimeoutOnHeat(true)

        val saved = assertNotNull(api.lastSavedAutomod)
        assertTrue(saved.autoTimeoutOnHeat)
        assertEquals(70, saved.heatTimeoutThreshold)
    }

    @Test
    fun setHeatTimeoutSeconds_persists_the_automatic_timeout_length() = runTest {
        val (subject, api) = controller(FakeTrustApi())
        subject.load()

        subject.setHeatTimeoutSeconds(120)

        assertEquals(120, assertNotNull(api.lastSavedAutomod).heatTimeoutSeconds)
    }
}
