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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.CreateModerationRuleBody
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.ShieldStatus
import kotlinx.coroutines.flow.MutableStateFlow
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
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<ModerationState> = MutableStateFlow(ModerationState.Loading)

    /** The page render state: loading / ready (with the bans) / empty / error. */
    val state: StateFlow<ModerationState> = _state.asStateFlow()

    // The channel the loaded bans belong to, kept so [unban] targets the same channel without re-resolving.
    private var channelId: String? = null

    /** Resolve the active channel, then load its banned-viewer list. */
    suspend fun load() {
        _state.value = ModerationState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ModerationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val bans: List<BannedUser> =
            when (val result: ApiResult<List<BannedUser>> = moderationApi.bans(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = ModerationState.Error(result.error.message)
                    return
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

        // Emergency Shield Mode (resilient — a failure leaves it reported off rather than blanking the page).
        val shieldEnabled: Boolean =
            when (val result: ApiResult<ShieldStatus> = moderationApi.shieldMode(channel.id)) {
                is ApiResult.Failure -> false
                is ApiResult.Ok -> result.value.enabled
            }

        // Blocked terms (auto-removed words/phrases). Resilient — a failure degrades to an empty list.
        val blockedTerms: List<String> =
            when (val result: ApiResult<List<String>> = moderationApi.blockedTerms(channel.id)) {
                is ApiResult.Failure -> emptyList()
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

        // Today's moderation counters for the stats banner. Resilient — a failure leaves all counters at zero.
        val stats: ModerationStats =
            when (val result: ApiResult<ModerationStats> = moderationApi.stats(channel.id)) {
                is ApiResult.Failure -> ModerationStats()
                is ApiResult.Ok -> result.value
            }

        // Empty only when there is genuinely nothing to show AND every always-on control (shield, automod) is off;
        // if any is active, or there are filter rules, the page renders so its state stays visible.
        _state.value =
            if (
                bans.isEmpty() &&
                    modLog.isEmpty() &&
                    blockedTerms.isEmpty() &&
                    rules.isEmpty() &&
                    !shieldEnabled &&
                    !anyAutomodEnabled
            ) {
                ModerationState.Empty
            } else {
                ModerationState.Ready(bans, modLog, shieldEnabled, blockedTerms, automod, rules, stats = stats)
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

    // Reload on success; on failure surface the message on the current Ready state without losing the lists.
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
        val stats: ModerationStats = ModerationStats(),
        val actionError: String? = null,
    ) : ModerationState

    data object Empty : ModerationState

    data class Error(val detail: String) : ModerationState
}

/** The four independent AutoMod filters, used to address a per-filter toggle. */
enum class AutomodFilter {
    Link,
    Caps,
    Phrases,
    Emotes,
}
