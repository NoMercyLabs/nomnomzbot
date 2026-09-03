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

import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChannelSearchResult
import bot.nomnomz.dashboard.core.network.ChatFilter
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.CreateChatFilterBody
import bot.nomnomz.dashboard.core.network.UpdateChatFilterBody
import bot.nomnomz.dashboard.core.network.CreateModerationRuleBody
import bot.nomnomz.dashboard.core.network.ModerationActionResult
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.EscalationPolicy
import bot.nomnomz.dashboard.core.network.Moderator
import bot.nomnomz.dashboard.core.network.ResolvedAutomodQueueItem
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.ModerationStanding
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.NetworkBanResult
import bot.nomnomz.dashboard.core.network.NetworkNukeBatch
import bot.nomnomz.dashboard.core.network.NetworkNukeBody
import bot.nomnomz.dashboard.core.network.SaveSharedBanSettingsBody
import bot.nomnomz.dashboard.core.network.SetModerationStandingBody
import bot.nomnomz.dashboard.core.network.SharedBanSettings
import bot.nomnomz.dashboard.core.network.SharedBanTrustedChannel
import bot.nomnomz.dashboard.core.network.ShoutoutOverride
import bot.nomnomz.dashboard.core.network.UnbanRequest
import bot.nomnomz.dashboard.core.network.UpsertEscalationPolicyBody
import bot.nomnomz.dashboard.core.network.ShieldStatus
import bot.nomnomz.dashboard.core.network.TrustApi
import bot.nomnomz.dashboard.core.network.SpamDefenseApi
import bot.nomnomz.dashboard.core.network.SpamDefensePolicy
import bot.nomnomz.dashboard.core.network.SpamDefenseSettings
import bot.nomnomz.dashboard.core.network.FollowBotBlockEntry
import bot.nomnomz.dashboard.core.network.SpamCampaign
import bot.nomnomz.dashboard.core.network.SpamDetection
import bot.nomnomz.dashboard.core.network.TrustPolicy
import bot.nomnomz.dashboard.core.network.TwitchAutoModSettings
import bot.nomnomz.dashboard.core.network.UpdateTrustPolicyBody
import bot.nomnomz.dashboard.core.network.UpdateTwitchAutoModSettingsBody
import bot.nomnomz.dashboard.core.network.UserModerationContext
import bot.nomnomz.dashboard.core.network.UserNote
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.ViewerReport
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_action_applied
import nomnomzbot.composeapp.generated.resources.feedback_action_failed
import nomnomzbot.composeapp.generated.resources.feedback_unban_failed
import nomnomzbot.composeapp.generated.resources.feedback_unbanned

// The Moderation page's state-holder: resolve the active channel, load its real list of currently-banned
// viewers from the backend (no fabricated entries), and lift a ban on request. The screen renders [state];
// a retry calls [load] again. [unban] is the one destructive action here — the screen must confirm it first.
class ModerationController(
    private val channelsApi: ChannelsApi,
    private val moderationApi: ModerationApi,
    private val communityApi: CommunityApi,
    private val feedback: Feedback = NoOpFeedback,
    // Optional broadcaster-search source for the shared-ban trusted-channel picker. Nullable so the state-holder
    // tests construct the controller without it.
    private val streamApi: StreamApi? = null,
    // The per-channel trust policy (S-OWN23) that drives the moderation trust score and heat decay. Nullable for
    // the same reason as [streamApi]: a state-holder test that does not exercise the Trust & Automation section
    // omits it, and the section then stays hidden rather than rendering a policy nobody read.
    private val trustApi: TrustApi? = null,
    // The per-channel spam-defence policy. Nullable for the same reason as [trustApi]: a state-holder test
    // that does not exercise the section omits it, and the section then stays hidden rather than rendering
    // settings nobody read.
    private val spamDefenseApi: SpamDefenseApi? = null,
) {
    private val _state: MutableStateFlow<ModerationState> = MutableStateFlow(ModerationState.Loading)

    /** The page render state: loading / ready (with the bans) / empty / error. */
    val state: StateFlow<ModerationState> = _state.asStateFlow()

    // The per-user moderation panel opened from a banned-user row: null = closed, else its load state.
    private val _userContext: MutableStateFlow<UserContextState?> = MutableStateFlow(null)

    /** The open per-user moderation panel (null when closed). */
    val userContext: StateFlow<UserContextState?> = _userContext.asStateFlow()

    // The channel the loaded bans belong to, kept so [unban] targets the same channel without re-resolving.
    private var channelId: String? = null

    /**
     * Autocomplete over the channel's known viewers for the shared "moderate a viewer" picker. Each match's
     * [PickerOption.id] is the viewer's Twitch user id — the id the ban / timeout / warn / standing writes key on.
     * Best-effort: no resolved channel or a failed search yields an empty list so the picker shows "no matches"
     * rather than sinking the dialog. The write that follows re-checks authorization.
     */
    suspend fun searchViewers(query: String): List<PickerOption> {
        val channel: String = channelId ?: return emptyList()
        return when (
            val result: ApiResult<List<ViewerOption>> = communityApi.searchViewers(channel, query)
        ) {
            is ApiResult.Ok ->
                result.value.map { PickerOption(id = it.id, label = it.label, sublabel = it.subLabel) }
            is ApiResult.Failure -> emptyList()
        }
    }

    /**
     * Autocomplete over Twitch broadcasters (for the shared-ban trusted-channel picker — an ARBITRARY channel,
     * not one of this channel's viewers). Resolves against this page's channel; empty until [load] resolves it,
     * on failure, or when the stream API is absent (tests).
     */
    suspend fun searchChannels(query: String): List<PickerOption> {
        val channel: String = channelId ?: return emptyList()
        val api: StreamApi = streamApi ?: return emptyList()
        return when (
            val result: ApiResult<List<ChannelSearchResult>> = api.searchChannels(channel, query)
        ) {
            is ApiResult.Ok ->
                result.value.map {
                    PickerOption(
                        id = it.id,
                        label = resolveRowLabel(it.displayName, secondary = it.login, typeLabel = "Channel", discriminatorSource = it.id),
                        sublabel = it.login,
                    )
                }
            is ApiResult.Failure -> emptyList()
        }
    }

    /** Resolve the active channel, then load its banned-viewer list. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is ModerationState.Ready) _state.value = ModerationState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ModerationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        // Bans / blocked-terms / shield read LIVE Twitch state, so they can legitimately fail where a local mirror
        // never did — a missing scope, or a channel you moderate but the bot isn't installed on (no broadcaster
        // token). A failure is NOT an empty list (that would be the old phantom lie: "no bans"); it means the
        // section is unavailable here. Track availability per section so the UI shows a needs-permission notice
        // instead of a blank state. A bans failure no longer errors the whole page — the rest still renders.
        var bansAvailable: Boolean = true
        val bans: List<BannedUser> =
            when (val result: ApiResult<List<BannedUser>> = moderationApi.bans(channel.id)) {
                is ApiResult.Failure -> {
                    bansAvailable = false
                    emptyList()
                }
                is ApiResult.Ok -> result.value
            }

        // The mod action log (recent moderator actions). A failure must NOT blank the page — the bans loaded —
        // so it degrades to an empty list rather than erroring the whole screen.
        val modLog: List<ModLogEntry> =
            when (val result: ApiResult<List<ModLogEntry>> = moderationApi.modLog(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Emergency Shield Mode (live Twitch state). A failure means unavailable here — NOT "off" (a phantom lie);
        // the UI shows a needs-permission notice instead of an off toggle.
        var shieldAvailable: Boolean = true
        val shieldEnabled: Boolean =
            when (val result: ApiResult<ShieldStatus> = moderationApi.shieldMode(channel.id)) {
                is ApiResult.Failure -> {
                    shieldAvailable = false
                    false
                }
                is ApiResult.Ok -> result.value.enabled
            }

        // Blocked terms (live Twitch state). A failure means unavailable here — NOT "no terms"; the UI shows a
        // needs-permission notice instead of the empty state.
        var blockedTermsAvailable: Boolean = true
        val blockedTerms: List<String> =
            when (val result: ApiResult<List<String>> = moderationApi.blockedTerms(channel.id)) {
                is ApiResult.Failure -> {
                    blockedTermsAvailable = false
                    emptyList()
                }
                is ApiResult.Ok -> result.value
            }

        // The AutoMod filter config (resilient — a failure leaves the filters reported off/default).
        val automod: AutomodConfig =
            when (val result: ApiResult<AutomodConfig> = moderationApi.automod(channel.id)) {
                is ApiResult.Failure -> AutomodConfig()
                is ApiResult.Ok -> result.value
            }
        val anyAutomodEnabled: Boolean =
            automod.linkFilter.enabled ||
                automod.capsFilter.enabled ||
                automod.bannedPhrases.enabled ||
                automod.emoteSpam.enabled

        // Filter rules (custom moderation rules). Resilient — a failure degrades to an empty list.
        val rules: List<ModerationRule> =
            when (val result: ApiResult<List<ModerationRule>> = moderationApi.rules(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Custom chat filters (regex / blocklist / link-policy, J.6, S066). Resilient — a failure degrades to
        // an empty list rather than failing the page.
        val chatFilters: List<ChatFilter> =
            when (val result: ApiResult<List<ChatFilter>> = moderationApi.chatFilters(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Today's moderation counters for the stats banner. Resilient — a failure leaves all counters at zero.
        val stats: ModerationStats =
            when (val result: ApiResult<ModerationStats> = moderationApi.stats(channel.id)) {
                is ApiResult.Failure -> ModerationStats()
                is ApiResult.Ok -> result.value
            }

        // Pending unban-request appeals (viewers appeal a ban on Twitch). Resilient — a missing scope / no
        // broadcaster token degrades to an empty queue rather than failing the page.
        val unbanRequests: List<UnbanRequest> =
            when (val result: ApiResult<List<UnbanRequest>> = moderationApi.unbanRequests(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Open viewer reports awaiting triage. Resilient — a failure degrades to an empty queue.
        val reports: List<ViewerReport> =
            when (val result: ApiResult<List<ViewerReport>> = moderationApi.reports(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // The channel's current Twitch moderators (S066-mod-actions). Resilient — a failure degrades to an
        // empty roster rather than failing the page (a channel with no broadcaster token / missing scope
        // still gets the rest of the page).
        val moderators: List<Moderator> =
            when (val result: ApiResult<List<Moderator>> = moderationApi.moderators(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // The AutoMod held-message review queue (J.1, S066). Resilient — a failure degrades to an empty queue.
        val automodQueue: List<ModerationQueueItem> =
            when (
                val result: ApiResult<List<ModerationQueueItem>> = moderationApi.automodQueue(channel.id)
            ) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // The repeat-offender escalation ladder (J.10). Resilient — a failure (below the read floor) leaves the
        // card hidden rather than failing the page; when unset the backend still returns the disabled default.
        val escalationPolicy: EscalationPolicy? =
            when (val result: ApiResult<EscalationPolicy> = moderationApi.escalationPolicy(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // The shared-ban trust web (J.9). Resilient — a failure (SuperMod-gated read) leaves the card hidden.
        val sharedBanSettings: SharedBanSettings? =
            when (val result: ApiResult<SharedBanSettings> = moderationApi.sharedBanSettings(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // The network-nuke batch history (J.2a). Resilient — a failure degrades to an empty table.
        val nukeBatches: List<NetworkNukeBatch> =
            when (val result: ApiResult<List<NetworkNukeBatch>> = moderationApi.nukeBatches(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // This channel's own custom shoutout announcement template — also what OTHER streamers see when THEY
        // shout this channel out. Resilient — a failure just means "use the built-in default" for display.
        val shoutoutTemplate: String? =
            when (val result: ApiResult<String?> = moderationApi.shoutoutTemplate(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // This channel's own personal shoutout lines for specific people (old-bot parity). Resilient — a
        // failure degrades to an empty list rather than failing the page.
        val shoutoutOverrides: List<ShoutoutOverride> =
            when (val result: ApiResult<List<ShoutoutOverride>> = moderationApi.shoutoutOverrides(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Twitch's OWN AutoMod levels (live Helix state). A failure means unreadable here — NOT "all levels 0";
        // the automation panel then says the state is unknown rather than claiming AutoMod filters nothing.
        val twitchAutoMod: TwitchAutoModSettings? =
            when (val result: ApiResult<TwitchAutoModSettings> = moderationApi.twitchAutoMod(channel.id)) {
                is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // The channel's trust policy (S-OWN23). Resilient — a failure (below the read floor) leaves the Trust &
        // Automation editor hidden rather than failing the page; the backend returns the DEFAULTS (isPinned =
        // false) for a channel that never saved one.
        val trustPolicy: TrustPolicy? =
            when (val result: ApiResult<TrustPolicy>? = trustApi?.policy(channel.id)) {
                null, is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // The channel's spam-defence policy. Resilient in exactly the same way, and for the same reason:
        // a caller below the read floor sees the section hidden rather than the whole page failing.
        val spamDefense: SpamDefensePolicy? =
            when (val result: ApiResult<SpamDefensePolicy>? = spamDefenseApi?.policy(channel.id)) {
                null, is ApiResult.Failure -> null
                is ApiResult.Ok -> result.value
            }

        // The verdict log. Resilient like the policy: a caller below the read floor sees the section
        // hidden, never a failed page.
        val spamDetections: List<SpamDetection> =
            when (
                val result: ApiResult<List<SpamDetection>>? = spamDefenseApi?.detections(channel.id)
            ) {
                null, is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        val spamCampaigns: List<SpamCampaign> =
            when (val result: ApiResult<List<SpamCampaign>>? = spamDefenseApi?.campaigns(channel.id)) {
                null, is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        val followBotBlocks: List<FollowBotBlockEntry> =
            when (
                val result: ApiResult<List<FollowBotBlockEntry>>? =
                    spamDefenseApi?.followBotBlocks(channel.id)
            ) {
                null, is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Empty only when there is genuinely nothing to show AND every always-on control (shield, automod) is off
        // AND every live-Twitch section is available (an unavailable section must render Ready so its needs-permission
        // notice shows — never Empty, which would read as "nothing here" rather than "you can't see this here").
        _state.value =
            if (
                bans.isEmpty() &&
                    modLog.isEmpty() &&
                    blockedTerms.isEmpty() &&
                    rules.isEmpty() &&
                    moderators.isEmpty() &&
                    chatFilters.isEmpty() &&
                    unbanRequests.isEmpty() &&
                    reports.isEmpty() &&
                    automodQueue.isEmpty() &&
                    // The Trust & Automation section always has something to show when it is readable —
                    // the automation summary states what fires automatically even when the answer is
                    // "nothing", and that answer is exactly what an operator came to check. So a
                    // readable trust policy alone keeps the page Ready; only a caller who cannot read it
                    // at all can see Empty.
                    trustPolicy == null &&
                    !shieldEnabled &&
                    !anyAutomodEnabled &&
                    shoutoutTemplate.isNullOrBlank() &&
                    bansAvailable &&
                    blockedTermsAvailable &&
                    shieldAvailable
            ) {
                ModerationState.Empty
            } else {
                ModerationState.Ready(
                    bans,
                    modLog,
                    shieldEnabled,
                    blockedTerms,
                    automod,
                    rules,
                    moderators = moderators,
                    chatFilters = chatFilters,
                    stats = stats,
                    unbanRequests = unbanRequests,
                    reports = reports,
                    automodQueue = automodQueue,
                    bansAvailable = bansAvailable,
                    blockedTermsAvailable = blockedTermsAvailable,
                    shieldAvailable = shieldAvailable,
                    escalationPolicy = escalationPolicy,
                    sharedBanSettings = sharedBanSettings,
                    nukeBatches = nukeBatches,
                    shoutoutTemplate = shoutoutTemplate,
                    shoutoutOverrides = shoutoutOverrides,
                    twitchAutoMod = twitchAutoMod,
                    trustPolicy = trustPolicy,
                    spamDefense = spamDefense,
                    spamDetections = spamDetections,
                    spamCampaigns = spamCampaigns,
                    followBotBlocks = followBotBlocks,
                )
            }
    }

    /**
     * Lift the ban on [userId] (a [BannedUser.id]). On success the list is reloaded so the unbanned viewer
     * drops off; on failure the current list stays put and the error surfaces on the [ModerationState.Ready]
     * state. The screen gates this behind a confirmation, so it only runs on an explicit, confirmed click.
     */
    suspend fun unban(userId: String) {
        val channel: String = channelId ?: return

        when (val result: ApiResult<Unit> = moderationApi.unban(channel, userId)) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_unbanned)
                load()
            }
            is ApiResult.Failure -> {
                // Announce the failure on the frame (persistent) AND keep the in-page banner over the list.
                feedback.error(Res.string.feedback_unban_failed, result.error.message)
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    /**
     * Open the per-user moderation panel for [userId] (a Twitch id) and load their recorded history — the bot's
     * OWN ban / timeout / warn / unban record (not the full Twitch history). No-ops when no channel is loaded.
     */
    suspend fun openUserContext(userId: String) {
        val channel: String = channelId ?: return
        _userContext.value = UserContextState.Loading
        _userContext.value =
            when (val result: ApiResult<UserModerationContext> = moderationApi.userContext(channel, userId)) {
                is ApiResult.Ok -> {
                    // The mod-team notes load alongside the rap sheet; a notes failure degrades to an empty list
                    // rather than failing the whole panel (the history is still worth showing).
                    val notes: List<UserNote> =
                        when (val n: ApiResult<List<UserNote>> = moderationApi.notesFor(channel, userId)) {
                            is ApiResult.Ok -> n.value
                            is ApiResult.Failure -> emptyList()
                        }
                    UserContextState.Ready(result.value, notes)
                }
                is ApiResult.Failure -> UserContextState.Error(result.error.message)
            }
    }

    /**
     * Add a note on [userId] with [content] ([pinned] floats it to the top), then reload the panel so it appears.
     * On failure surfaces the message on the page (the panel stays open). No-ops when no channel is loaded.
     */
    suspend fun addNote(userId: String, content: String, pinned: Boolean) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.createNote(channel, userId, content, pinned)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Edit note [noteId] on [userId]'s panel — new [content] and/or [pinned] — then reload so the change shows.
     * Surfaces the error on failure. No-ops when no channel is loaded.
     */
    suspend fun editNote(userId: String, noteId: String, content: String?, pinned: Boolean?) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.updateNote(channel, noteId, content, pinned)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Delete note [noteId] from [userId]'s panel, then reload. Surfaces the error on failure. */
    suspend fun deleteNote(userId: String, noteId: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.deleteNote(channel, noteId)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Close the per-user moderation panel. */
    fun closeUserContext() {
        _userContext.value = null
    }

    /**
     * Issue a Twitch warning to [userId] with [reason], then reload their rap sheet so the warn count + recent
     * actions reflect it. A backend success=false (the channel's grant can't warn) surfaces its message on the
     * page. No-ops when no channel is loaded.
     */
    suspend fun warn(userId: String, reason: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<ModerationActionResult> = moderationApi.warn(channel, userId, reason)) {
            is ApiResult.Ok -> {
                if (!result.value.success) {
                    setActionError(result.value.message ?: "The warning could not be issued.")
                }
                openUserContext(userId)
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Flag [userId] as suspicious ([status] = `active_monitoring` or `restricted`), then reload their rap sheet.
     * Surfaces the error on failure; no-ops when no channel is loaded.
     */
    suspend fun setSuspicious(userId: String, status: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.setSuspicious(channel, userId, status)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Clear the suspicious flag on [userId], then reload their rap sheet. Surfaces the error on failure. */
    suspend fun clearSuspicious(userId: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.clearSuspicious(channel, userId)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Resolve a pending unban-request appeal ([requestId]): [approve] lifts the ban (and drops it from the
     * queue), else it is denied with an optional [note]. Reloads the page on success so the queue + bans
     * reflect it; surfaces the error on the current list on failure. No-ops when no channel is loaded.
     */
    suspend fun resolveUnbanRequest(requestId: String, approve: Boolean, note: String?) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.resolveUnbanRequest(channel, requestId, approve, note))
    }

    /**
     * Triage a viewer report [reportId]: [action] is `dismiss` (close, no action) or `escalate` (flag for a
     * moderator to punish separately — escalation does NOT auto-punish). Reloads on success so the report drops
     * off the open queue; surfaces the error on the current list on failure. No-ops when no channel is loaded.
     */
    suspend fun resolveReport(reportId: String, action: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.resolveReport(channel, reportId, action))
    }

    /**
     * Resolve a held AutoMod message [queueItemId]: [action] is `approve` (release it to chat) or `deny` (drop
     * it) — this is the S066 done-when action. Reloads on success so the item drops off the pending queue;
     * surfaces the error on the current list on failure. No-ops when no channel is loaded.
     */
    suspend fun resolveAutomodQueueItem(queueItemId: String, action: String) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<ResolvedAutomodQueueItem> =
                moderationApi.resolveAutomodQueueItem(channel, queueItemId, action)
        ) {
            is ApiResult.Ok -> {
                load()
                // A partial outcome (deny stood, follow-up timeout/ban failed) is still a reload-worthy
                // success — but its failure text must reach the moderator, never be swallowed.
                result.value.followUpError?.let { setActionError(it) }
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Un-nuke [userId] (a [BannedUser.id] = Twitch id): lift the ban in this channel ([scope] = "this_channel")
     * or across every channel the operator moderates ([scope] = "all_moderated"). Reloads on success so the
     * unbanned viewer drops off; surfaces the error on the current list on failure. No-ops with no channel.
     */
    suspend fun networkUnban(userId: String, scope: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<NetworkBanResult> = moderationApi.networkUnban(channel, userId, scope)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Replace the whole escalation policy ([policy]) — enable flag, ladder, window, AutoMod-counting — then reload
     * so the card reflects it. Surfaces the error on failure. No-ops when no channel is loaded.
     */
    suspend fun saveEscalationPolicy(policy: UpsertEscalationPolicyBody) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<EscalationPolicy> = moderationApi.saveEscalationPolicy(channel, policy)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Forgive [userId] — reset their offense tally against the escalation ladder — then reload their rap sheet.
     * Surfaces the error on failure. No-ops when no channel is loaded.
     */
    suspend fun forgiveUser(userId: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.resetEscalation(channel, userId)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Set the heat score [threshold] (0–100) at which the ladder auto-times-out a viewer, re-sending the whole
     * AutoMod config (the backend POST takes the full config). Reloads on success; no-ops off a Ready state.
     */
    suspend fun setHeatTimeoutThreshold(threshold: Int) {
        val channel: String = channelId ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return
        val updated: AutomodConfig = current.automod.copy(heatTimeoutThreshold = threshold)
        afterWrite(moderationApi.saveAutomod(channel, updated))
    }

    /**
     * Fan-out ban [targetTwitchUserId] across every channel the operator holds SuperMod+ on (the network nuke,
     * J.2a) — [requireConfirmation] is forced true, so the screen MUST confirm the blast radius first. Reloads on
     * success so the new batch appears in the history. Surfaces the error on failure; no-ops with no channel.
     */
    suspend fun networkNuke(targetTwitchUserId: String, reason: String?, matchTerm: String?) {
        val channel: String = channelId ?: return
        val body: NetworkNukeBody =
            NetworkNukeBody(
                targetTwitchUserId = targetTwitchUserId,
                reason = reason,
                matchTerm = matchTerm,
                requireConfirmation = true,
            )
        when (val result: ApiResult<NetworkNukeBatch> = moderationApi.networkNuke(channel, body)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Revert nuke batch [batchId] — lift every leg — then reload so its status flips to reverted. */
    suspend fun revertNuke(batchId: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<NetworkNukeBatch> = moderationApi.revertNuke(channel, batchId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Save the shared-ban policy — [accept] applies trusted partners' bans here, [share] offers ours to them
     * (both explicit on every save). Reloads on success; surfaces the error on failure. No-ops with no channel.
     */
    suspend fun saveSharedBanSettings(accept: Boolean, share: Boolean) {
        val channel: String = channelId ?: return
        val body: SaveSharedBanSettingsBody =
            SaveSharedBanSettingsBody(acceptSharedChatBans = accept, shareOutgoingBans = share)
        when (val result: ApiResult<SharedBanSettings> = moderationApi.saveSharedBanSettings(channel, body)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Add [trustedChannelId] to the shared-ban trust list, then reload so it appears. Surfaces the error. */
    suspend fun addTrustedChannel(trustedChannelId: String) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<SharedBanTrustedChannel> =
                moderationApi.addTrustedChannel(channel, trustedChannelId)
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Remove [trustedChannelId] from the trust list, then reload so it drops off. Surfaces the error. */
    suspend fun removeTrustedChannel(trustedChannelId: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.removeTrustedChannel(channel, trustedChannelId))
    }

    /**
     * Set [userId]'s bot-side [standing] (`muted` | `shadowbanned` | `blacklisted`) on [provider] with optional
     * [reason], then reload their rap sheet so the badge + standings list reflect it. Surfaces the error on
     * failure (e.g. 409 assigning the broadcaster one). No-ops when no channel is loaded.
     */
    suspend fun setStanding(userId: String, provider: String, standing: String, reason: String?) {
        val channel: String = channelId ?: return
        val body: SetModerationStandingBody =
            SetModerationStandingBody(provider = provider, standing = standing, reason = reason)
        when (
            val result: ApiResult<ModerationStanding> =
                moderationApi.setStanding(channel, userId, body)
        ) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Restore [userId] to normal standing on [provider], then reload their rap sheet. Surfaces the error. */
    suspend fun clearStanding(userId: String, provider: String) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.clearStanding(channel, userId, provider)) {
            is ApiResult.Ok -> openUserContext(userId)
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    // Surface a write error on the current Ready list without disturbing it (same shape as the other writes).
    private fun setActionError(message: String) {
        val current: ModerationState = _state.value
        if (current is ModerationState.Ready) {
            _state.value = current.copy(actionError = message)
        }
    }

    /**
     * Turn emergency Shield Mode on or off ([enabled]), then reload so the page reflects it. Surfaces the error
     * on the current Ready state on failure; no-ops when no channel is loaded.
     */
    suspend fun setShieldMode(enabled: Boolean) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.setShieldMode(channel, enabled)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    /** Add [term] to the blocked-terms list, then reload so it appears. Surfaces the error on failure. */
    suspend fun addBlockedTerm(term: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.addBlockedTerm(channel, term))
    }

    /** Remove [term] from the blocked-terms list, then reload so it drops off. Surfaces the error on failure. */
    suspend fun removeBlockedTerm(term: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.removeBlockedTerm(channel, term))
    }

    /**
     * Set (or clear, with a blank [template]) this channel's own shoutout announcement template — the text
     * OTHER streamers' `!so` speaks/posts when THEY shout this channel out, then reload so it shows saved.
     */
    suspend fun setShoutoutTemplate(template: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.setShoutoutTemplate(channel, template.ifBlank { null }))
    }

    /** Create or update this channel's own shoutout line for [targetTwitchUserId], then reload. */
    suspend fun setShoutoutOverride(targetTwitchUserId: String, targetDisplayName: String, messageTemplate: String) {
        val channel: String = channelId ?: return
        afterWrite(
            moderationApi.setShoutoutOverride(channel, targetTwitchUserId, targetDisplayName, messageTemplate)
        )
    }

    /** Remove this channel's own shoutout line for [targetTwitchUserId], then reload. */
    suspend fun deleteShoutoutOverride(targetTwitchUserId: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.deleteShoutoutOverride(channel, targetTwitchUserId))
    }

    /**
     * Flip one AutoMod [filter]'s enabled flag and persist the whole config (the backend POST takes the full
     * config; the other filters' settings ride along unchanged), then reload. No-ops off a Ready state.
     */
    suspend fun toggleAutomodFilter(filter: AutomodFilter) {
        val channel: String = channelId ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return
        val c: AutomodConfig = current.automod
        val updated: AutomodConfig =
            when (filter) {
                AutomodFilter.Link ->
                    c.copy(linkFilter = c.linkFilter.copy(enabled = !c.linkFilter.enabled))
                AutomodFilter.Caps ->
                    c.copy(capsFilter = c.capsFilter.copy(enabled = !c.capsFilter.enabled))
                AutomodFilter.Phrases ->
                    c.copy(bannedPhrases = c.bannedPhrases.copy(enabled = !c.bannedPhrases.enabled))
                AutomodFilter.Emotes ->
                    c.copy(emoteSpam = c.emoteSpam.copy(enabled = !c.emoteSpam.enabled))
            }
        afterWrite(moderationApi.saveAutomod(channel, updated))
    }

    /**
     * Set the caps filter's [threshold] (percent of a message that may be caps before it is flagged), re-sending
     * the whole AutoMod config. Reloads on success; no-ops off a Ready state.
     */
    suspend fun setCapsThreshold(threshold: Int) =
        saveAutomod { it.copy(capsFilter = it.capsFilter.copy(threshold = threshold)) }

    /**
     * Set the emote-spam filter's [maxEmotes] (how many emotes a message may carry before it is flagged),
     * re-sending the whole AutoMod config. Reloads on success; no-ops off a Ready state.
     */
    suspend fun setEmoteMaxEmotes(maxEmotes: Int) =
        saveAutomod { it.copy(emoteSpam = it.emoteSpam.copy(maxEmotes = maxEmotes)) }

    /** Add [phrase] to the banned-phrases list (dedup, case-insensitive), then persist + reload. */
    suspend fun addBannedPhrase(phrase: String) {
        val trimmed: String = phrase.trim()
        if (trimmed.isEmpty()) return
        saveAutomod { c ->
            if (c.bannedPhrases.phrases.any { it.equals(trimmed, ignoreCase = true) }) c
            else c.copy(bannedPhrases = c.bannedPhrases.copy(phrases = c.bannedPhrases.phrases + trimmed))
        }
    }

    /** Remove [phrase] from the banned-phrases list, then persist + reload. */
    suspend fun removeBannedPhrase(phrase: String) =
        saveAutomod { c ->
            c.copy(bannedPhrases = c.bannedPhrases.copy(phrases = c.bannedPhrases.phrases.filterNot { it == phrase }))
        }

    /** Add [domain] to the link filter's allow-list (dedup, case-insensitive), then persist + reload. */
    suspend fun addLinkWhitelist(domain: String) {
        val trimmed: String = domain.trim()
        if (trimmed.isEmpty()) return
        saveAutomod { c ->
            if (c.linkFilter.whitelist.any { it.equals(trimmed, ignoreCase = true) }) c
            else c.copy(linkFilter = c.linkFilter.copy(whitelist = c.linkFilter.whitelist + trimmed))
        }
    }

    /** Remove [domain] from the link filter's allow-list, then persist + reload. */
    suspend fun removeLinkWhitelist(domain: String) =
        saveAutomod { c ->
            c.copy(linkFilter = c.linkFilter.copy(whitelist = c.linkFilter.whitelist.filterNot { it == domain }))
        }

    // Apply [transform] to the current AutoMod config and persist the whole thing (the backend POST takes the
    // full config; unchanged filters ride along). No-ops off a Ready state. Shared by every automod sub-edit.
    private suspend fun saveAutomod(transform: (AutomodConfig) -> AutomodConfig) {
        val channel: String = channelId ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return
        afterWrite(moderationApi.saveAutomod(channel, transform(current.automod)))
    }

    /**
     * Create a new filter rule with the given [name], [type], [action], optional [durationSeconds] (for
     * `"timeout"` action), and optional [reason]. Reloads on success so the new rule appears in the list.
     */
    suspend fun createRule(
        name: String,
        type: String,
        action: String,
        durationSeconds: Int? = null,
        reason: String? = null,
    ) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<ModerationRule> =
                moderationApi.createRule(
                    channel,
                    CreateModerationRuleBody(name, type, action, durationSeconds, reason),
                )
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    /** Enable or disable a filter rule ([enabled]), then reload. Surfaces the error on failure. */
    suspend fun toggleRule(ruleId: Int, enabled: Boolean) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.setRuleEnabled(channel, ruleId, enabled))
    }

    /** Delete a filter rule, then reload so it drops off the list. Surfaces the error on failure. */
    suspend fun deleteRule(ruleId: Int) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.deleteRule(channel, ruleId))
    }

    /**
     * Create a chat filter (J.6, S066) — a regex [pattern] or a literal-word [terms] blocklist, with the
     * given [action]. [timeoutSeconds] applies only when [action] is `"Timeout"`. Reloads on success so the
     * new filter appears in the list. Surfaces the error on failure. No-ops when no channel is loaded.
     */
    suspend fun createChatFilter(
        filterType: String,
        name: String,
        action: String,
        pattern: String?,
        terms: List<String>?,
        timeoutSeconds: Int?,
    ) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<ChatFilter> =
                moderationApi.createChatFilter(
                    channel,
                    CreateChatFilterBody(
                        filterType = filterType,
                        name = name,
                        action = action,
                        pattern = pattern,
                        terms = terms,
                        timeoutSeconds = timeoutSeconds,
                    ),
                )
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Enable or disable a chat filter ([enabled]), then reload. Surfaces the error on failure. */
    suspend fun toggleChatFilter(filterId: String, enabled: Boolean) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<ChatFilter> =
                moderationApi.updateChatFilter(channel, filterId, UpdateChatFilterBody(isEnabled = enabled))
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /** Delete a chat filter, then reload so it drops off the list. Surfaces the error on failure. */
    suspend fun deleteChatFilter(filterId: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.deleteChatFilter(channel, filterId))
    }

    /**
     * Apply a moderation [action] (`"ban"` or `"timeout"`) to [targetUserId]. On success the page reloads so the new
     * ban appears. On failure the error surfaces on the Ready state without losing the lists.
     * [durationSeconds] is only required for `"timeout"` (ignored for ban). [reason] is optional.
     */
    suspend fun performAction(
        action: String,
        targetUserId: String,
        durationSeconds: Int?,
        reason: String?,
    ) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<Unit> =
                moderationApi.performAction(channel, action, targetUserId, durationSeconds, reason)
        ) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_action_applied)
                load()
            }
            is ApiResult.Failure -> {
                feedback.error(Res.string.feedback_action_failed, result.error.message)
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    /**
     * Grant [targetTwitchUserId] moderator privileges, then reload so the roster reflects it. Surfaces the
     * error on the current Ready state on failure. No-ops when no channel is loaded.
     */
    suspend fun addModerator(targetTwitchUserId: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.addModerator(channel, targetTwitchUserId))
    }

    /**
     * Revoke [userId]'s moderator privileges, then reload so they drop off the roster. Surfaces the error
     * on the current Ready state on failure. No-ops when no channel is loaded. The screen gates this
     * behind a confirmation before calling.
     */
    suspend fun removeModerator(userId: String) {
        val channel: String = channelId ?: return
        afterWrite(moderationApi.removeModerator(channel, userId))
    }

    /**
     * Clear every message from the channel's chat room (Twitch Clear Chat — irreversible, no message
     * survives). Does not reload the page — the chat itself isn't part of this screen's state. Surfaces
     * the error on the current Ready state on failure. The screen gates this behind a confirmation before
     * calling. No-ops when no channel is loaded.
     */
    suspend fun clearChat() {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.clearChat(channel)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Send a chat announcement with [message] and optional Twitch [color] (`"blue"`, `"green"`, `"orange"`,
     * `"purple"`, `"primary"`). Does not reload the page — the banner is transient. Surfaces any error.
     */
    suspend fun sendAnnouncement(message: String, color: String?) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.announce(channel, message, color)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> {
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    /**
     * Subscribe to [hubEvents] so the mod log updates in real-time:
     * - [HubEvent.ModAction]: prepends a new [ModLogEntry] to the log (cap 50) so ban/timeout/unban actions
     *   issued by any moderator appear instantly without a page refresh.
     * - [HubEvent.AutoModQueueChanged]: re-fetches the pending AutoMod queue so a newly held message (or a
     *   resolution made anywhere) shows without a reload.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            if (evt is HubEvent.AutoModQueueChanged) {
                refreshAutomodQueue()
                return@collect
            }
            if (evt !is HubEvent.ModAction) return@collect
            val current: ModerationState = _state.value
            if (current !is ModerationState.Ready) return@collect
            val entry: ModLogEntry = ModLogEntry(
                id = "${evt.action.action}_${evt.action.moderatorId}_${evt.action.targetUserId}",
                action = evt.action.action,
                moderator = evt.action.moderatorId,
                target = evt.action.targetUserId,
                reason = evt.action.reason,
                duration = evt.action.durationSeconds,
                timestamp = "",
            )
            _state.value = current.copy(modLog = (listOf(entry) + current.modLog).take(50))
        }
    }

    /**
     * Re-fetch the pending AutoMod queue alone (hub-pushed change). On a Ready state only that list is
     * swapped; any other state falls back to a full [load] so a hold arriving on an empty page still surfaces.
     */
    private suspend fun refreshAutomodQueue() {
        val channel: String = channelId ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) {
            load()
            return
        }
        val result: ApiResult<List<ModerationQueueItem>> = moderationApi.automodQueue(channel)
        if (result is ApiResult.Ok) {
            val latest: ModerationState = _state.value
            if (latest is ModerationState.Ready) {
                _state.value = latest.copy(automodQueue = result.value)
            }
        }
    }

    // Reload on success; on failure surface the message on the current Ready state without losing the lists.
    /**
     * Switch the automatic heat timeout on or off ([enabled]) — opt-in, so OFF means a heat crossing only flags
     * the viewer for a human. Re-sends the whole AutoMod config; reloads on success.
     */
    suspend fun setAutoTimeoutOnHeat(enabled: Boolean) = saveAutomod { it.copy(autoTimeoutOnHeat = enabled) }

    /** Set how long the automatic heat timeout lasts ([seconds]), re-sending the whole AutoMod config. */
    suspend fun setHeatTimeoutSeconds(seconds: Int) = saveAutomod { it.copy(heatTimeoutSeconds = seconds) }

    /**
     * Replace the channel's trust policy with [body].
     *
     * The four score weights must sum to 1.0 — the backend rejects anything else, so an invalid sum is refused
     * HERE and never sent: the state carries [ModerationState.Ready.trustWeightSumInvalid] and the editor renders
     * its inline error. A valid save stores the returned policy straight onto the state (the backend echoes the
     * saved row, isPinned now true), so the editor shows what actually persisted.
     */
    suspend fun saveTrustPolicy(body: UpdateTrustPolicyBody) {
        val channel: String = channelId ?: return
        val api: TrustApi = trustApi ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return
        if (!trustWeightsAreValid(body)) {
            _state.value = current.copy(trustWeightSumInvalid = true)
            return
        }
        when (val result: ApiResult<TrustPolicy> = api.savePolicy(channel, body)) {
            is ApiResult.Ok -> {
                val ready: ModerationState = _state.value
                if (ready is ModerationState.Ready) {
                    _state.value =
                        ready.copy(
                            trustPolicy = result.value,
                            trustWeightSumInvalid = false,
                            actionError = null,
                        )
                }
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Saves the channel's spam-defence settings. The backend validates every range against the same
     * catalogue the editor rendered from, so an out-of-range value comes back as a failure naming the
     * control by resource key rather than being silently clamped.
     */
    suspend fun saveSpamDefense(settings: SpamDefenseSettings) {
        val channel: String = channelId ?: return
        val api: SpamDefenseApi = spamDefenseApi ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return

        when (val result: ApiResult<SpamDefenseSettings> = api.saveSettings(channel, settings)) {
            is ApiResult.Ok -> {
                val ready: ModerationState = _state.value
                if (ready is ModerationState.Ready) {
                    // Keep the catalogue and invariants that came with the original load; only the
                    // values changed, and re-fetching them to learn nothing new would cost a round trip
                    // on every save.
                    _state.value =
                        ready.copy(
                            spamDefense =
                                ready.spamDefense?.copy(settings = result.value, isPinned = true),
                            actionError = null,
                        )
                }
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Marks one verdict wrong. The row is updated in place rather than the whole page reloaded — a
     * moderator working through a queue should not lose their scroll position for every correction.
     */
    suspend fun overturnSpamDetection(detectionId: String) {
        val channel: String = channelId ?: return
        val api: SpamDefenseApi = spamDefenseApi ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return

        when (val result: ApiResult<Unit> = api.overturn(channel, detectionId)) {
            is ApiResult.Ok -> {
                val ready: ModerationState = _state.value
                if (ready is ModerationState.Ready) {
                    _state.value =
                        ready.copy(
                            spamDetections =
                                ready.spamDetections.map { detection ->
                                    if (detection.id == detectionId) {
                                        detection.copy(overturnedAt = "overturned")
                                    } else {
                                        detection
                                    }
                                },
                            actionError = null,
                        )
                }
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Restores an entire follow-bot sweep. Bulk on purpose: a misread viral moment can be hundreds of
     * accounts, and undoing them one at a time is not a recovery path anybody would use.
     */
    suspend fun restoreFollowBotBatch(batchId: String) {
        val channel: String = channelId ?: return
        val api: SpamDefenseApi = spamDefenseApi ?: return
        val current: ModerationState = _state.value
        if (current !is ModerationState.Ready) return

        when (val result: ApiResult<Unit> = api.restoreFollowBotBatch(channel, batchId)) {
            is ApiResult.Ok -> {
                val ready: ModerationState = _state.value
                if (ready is ModerationState.Ready) {
                    _state.value =
                        ready.copy(
                            followBotBlocks =
                                ready.followBotBlocks.map { block ->
                                    if (block.batchId == batchId) {
                                        block.copy(restoredAt = "restored")
                                    } else {
                                        block
                                    }
                                },
                            actionError = null,
                        )
                }
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    /**
     * Replace Twitch's own AutoMod levels with [body]. The body type only exists in an overall-dial OR a
     * per-category shape, so the combination Twitch rejects cannot be sent. Stores the echoed settings on success.
     */
    suspend fun saveTwitchAutoMod(body: UpdateTwitchAutoModSettingsBody) {
        val channel: String = channelId ?: return
        when (
            val result: ApiResult<TwitchAutoModSettings> = moderationApi.saveTwitchAutoMod(channel, body)
        ) {
            is ApiResult.Ok -> {
                val ready: ModerationState = _state.value
                if (ready is ModerationState.Ready) {
                    _state.value = ready.copy(twitchAutoMod = result.value, actionError = null)
                }
            }
            is ApiResult.Failure -> setActionError(result.error.message)
        }
    }

    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }
}

/** The Moderation page render state. */
sealed interface ModerationState {
    data object Loading : ModerationState

    /**
     * The active bans + the recent mod action log, plus an optional message when the last unban attempt failed
     * (the lists stay intact).
     */
    data class Ready(
        val bans: List<BannedUser>,
        val modLog: List<ModLogEntry> = emptyList(),
        val shieldEnabled: Boolean = false,
        val blockedTerms: List<String> = emptyList(),
        val automod: AutomodConfig = AutomodConfig(),
        val rules: List<ModerationRule> = emptyList(),
        // The channel's current Twitch moderators (S066-mod-actions) — see load().
        val moderators: List<Moderator> = emptyList(),
        // Custom chat filters (regex / blocklist, J.6, S066) — see load().
        val chatFilters: List<ChatFilter> = emptyList(),
        val stats: ModerationStats = ModerationStats(),
        val actionError: String? = null,
        val unbanRequests: List<UnbanRequest> = emptyList(),
        val reports: List<ViewerReport> = emptyList(),
        // The AutoMod held-message review queue (J.1, S066) — pending items awaiting approve/deny. See load().
        val automodQueue: List<ModerationQueueItem> = emptyList(),
        // Live-Twitch sections: false when the section's read failed (missing scope / bot not installed here), so
        // the UI shows a needs-permission notice instead of an empty/off state. See load().
        val bansAvailable: Boolean = true,
        val blockedTermsAvailable: Boolean = true,
        val shieldAvailable: Boolean = true,
        // The escalation ladder (J.10) + shared-ban trust web (J.9), null when the read failed (below the floor);
        // the network-nuke batch history (J.2a). See load().
        val escalationPolicy: EscalationPolicy? = null,
        val sharedBanSettings: SharedBanSettings? = null,
        val nukeBatches: List<NetworkNukeBatch> = emptyList(),
        // This channel's own custom shoutout announcement template (null/blank = built-in default) — also
        // what OTHER streamers see when THEY shout this channel out. See load().
        val shoutoutTemplate: String? = null,
        // This channel's own personal shoutout lines for specific people (old-bot parity). See load().
        val shoutoutOverrides: List<ShoutoutOverride> = emptyList(),
        // Twitch's own AutoMod levels, null when the live read failed (unknown — never reported as "off").
        val twitchAutoMod: TwitchAutoModSettings? = null,
        // The channel's trust policy (S-OWN23), null when the read failed or no trust API is wired.
        val trustPolicy: TrustPolicy? = null,
        val spamDefense: SpamDefensePolicy? = null,
        val spamDetections: List<SpamDetection> = emptyList(),
        val spamCampaigns: List<SpamCampaign> = emptyList(),
        val followBotBlocks: List<FollowBotBlockEntry> = emptyList(),
        // True when the last trust-policy save was refused locally because the four weights do not sum to 1.0 —
        // the backend rejects that body, so the editor blocks it and shows the inline error instead.
        val trustWeightSumInvalid: Boolean = false,
    ) : ModerationState

    data object Empty : ModerationState

    data class Error(val detail: String) : ModerationState
}

/** How far the four trust weights may drift from 1.0 before the backend refuses the policy. */
private const val TRUST_WEIGHT_SUM_TOLERANCE: Double = 0.001

/**
 * True when the four score weights of [body] sum to 1.0 (within [TRUST_WEIGHT_SUM_TOLERANCE]) — the same rule the
 * backend validates. The editor reads this live to show the running sum, and the save path refuses a body that
 * fails it, so the user never has to guess at a server-side rejection.
 */
fun trustWeightsAreValid(body: UpdateTrustPolicyBody): Boolean {
    val sum: Double =
        body.requestCountWeight + body.accountAgeWeight + body.contentAgeWeight + body.contentPopularityWeight
    val drift: Double = sum - 1.0
    return (if (drift < 0.0) -drift else drift) <= TRUST_WEIGHT_SUM_TOLERANCE
}

/** The four independent AutoMod filters, used to address a per-filter toggle. */
enum class AutomodFilter {
    Link,
    Caps,
    Phrases,
    Emotes,
}

/** The per-user moderation panel's load state (opened on demand from a banned-user row). */
sealed interface UserContextState {
    data object Loading : UserContextState

    data class Ready(
        val context: UserModerationContext,
        val notes: List<UserNote> = emptyList(),
    ) : UserContextState

    data class Error(val detail: String) : UserContextState
}
