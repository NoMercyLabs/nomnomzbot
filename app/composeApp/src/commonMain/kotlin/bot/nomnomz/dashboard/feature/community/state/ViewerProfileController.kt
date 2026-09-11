// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.DataExport
import bot.nomnomz.dashboard.core.network.GdprApi
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ModerationHistoryEntry
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.ShoutoutOverrideKind
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.UsersApi
import bot.nomnomz.dashboard.core.network.ViewerDataApi
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.community_action_error

// The Community PROFILE page's state-holder (owner punch list 2026-09-08 §3) — the single-person view opened
// from the Directory. Loads the full [ViewerProfileSummary] in one call and drives every genuinely-editable
// section through its OWN existing endpoint (never a second write path for data another page already owns):
// shoutout/raid overrides + moderation history notes (ModerationApi), TTS voice (TtsApi), management role +
// permits (RolesApi), free-form viewer data (ViewerDataApi), and the broadcaster-only GDPR export/erase
// (GdprApi/UsersApi) that used to live on the old Community stats dialog. Every write reloads the profile so
// the page always reflects the backend's truth, matching [CommunityController]'s reload-on-write discipline.
class ViewerProfileController(
    private val channelsApi: ChannelsApi,
    private val communityApi: CommunityApi,
    private val moderationApi: ModerationApi,
    private val ttsApi: TtsApi,
    private val rolesApi: RolesApi,
    private val viewerDataApi: ViewerDataApi,
    private val gdprApi: GdprApi,
    private val usersApi: UsersApi,
    private val fileBridge: JournalFileIO,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<ViewerProfileState> =
        MutableStateFlow(ViewerProfileState.Loading)

    /** The page render state: loading / ready (with the profile + a page of history) / error. */
    val state: StateFlow<ViewerProfileState> = _state.asStateFlow()

    private var channelId: String? = null
    private var userId: String? = null
    private var historyPage: Int = 1

    // The channel's synthesisable TTS voices — fetched once per [load] (not re-fetched on every write reload)
    // so the overrides section offers a real picker instead of a raw voice-id text field. Best-effort: a failed
    // fetch just leaves the picker empty rather than blocking the rest of the profile.
    private var availableVoices: List<TtsVoice> = emptyList()

    /** Resolve the active channel, then load [personUserId]'s full profile and the first page of their history. */
    suspend fun load(personUserId: String) {
        _state.value = ViewerProfileState.Loading
        userId = personUserId
        historyPage = 1

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ViewerProfileState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        availableVoices =
            when (val voicesResult: ApiResult<List<TtsVoice>> = ttsApi.voices(channel.id)) {
                is ApiResult.Ok -> voicesResult.value
                is ApiResult.Failure -> emptyList()
            }

        refresh(isInitial = true)
    }

    /** Reload the profile and reset the history log to its first page (called after every mutation). */
    private suspend fun refresh(isInitial: Boolean) {
        val channel: String = channelId ?: return
        val person: String = userId ?: return

        val profileResult: ApiResult<ViewerProfileSummary> = communityApi.profile(channel, person)
        val profile: ViewerProfileSummary =
            when (profileResult) {
                is ApiResult.Failure -> {
                    if (isInitial) _state.value = ViewerProfileState.Error(profileResult.error.message)
                    else failWrite(profileResult.error.message)
                    return
                }
                is ApiResult.Ok -> profileResult.value
            }

        historyPage = 1
        val historyResult =
            moderationApi.historyForUser(channel, profile.identity.userId, historyPage, HistoryPageSize)
        val (history, historyHasMore) =
            when (historyResult) {
                is ApiResult.Ok -> historyResult.value.data to historyResult.value.hasMore
                is ApiResult.Failure -> emptyList<ModerationHistoryEntry>() to false
            }

        // The profile summary carries no ban flag (it isn't one of the punch-list §3 groups) — the Twitch-side
        // ban state lives on the same `CommunityUserDto`/`UserDetailDto` the Directory row already reads, keyed
        // on the person's Twitch id. Fetched here (not folded into the profile call) so the Ban/Unban action
        // shows the TRUE current state rather than guessing — a wrong label would offer "Ban" on an already-
        // banned person or vice versa.
        val isBanned: Boolean =
            profile.identity.twitchUserId?.let { twitchId ->
                when (val memberResult = communityApi.member(channel, twitchId)) {
                    is ApiResult.Ok -> memberResult.value.isBanned
                    is ApiResult.Failure -> false
                }
            } ?: false

        _state.value =
            ViewerProfileState.Ready(
                profile = profile,
                history = history,
                historyHasMore = historyHasMore,
                availableVoices = availableVoices,
                isBanned = isBanned,
            )
    }

    /** Page the full moderation history log forward. The screen only calls this while `historyHasMore` is true. */
    suspend fun loadMoreHistory() {
        val channel: String = channelId ?: return
        val current: ViewerProfileState = _state.value
        if (current !is ViewerProfileState.Ready) return

        val nextPage: Int = historyPage + 1
        when (
            val result =
                moderationApi.historyForUser(
                    channel,
                    current.profile.identity.userId,
                    nextPage,
                    HistoryPageSize,
                )
        ) {
            is ApiResult.Ok -> {
                historyPage = nextPage
                _state.value =
                    current.copy(
                        history = current.history + result.value.data,
                        historyHasMore = result.value.hasMore,
                    )
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Add a manual moderation-history note, then reload so the log and summary reflect it. */
    suspend fun addHistoryNote(note: String) {
        val channel: String = channelId ?: return
        val current: ViewerProfileState = _state.value
        if (current !is ViewerProfileState.Ready) return
        when (
            val result = moderationApi.addHistoryNote(channel, current.profile.identity.userId, note)
        ) {
            is ApiResult.Ok -> refresh(isInitial = false)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Save this channel's [kind] line ([ShoutoutOverrideKind.Shoutout] / [ShoutoutOverrideKind.Raid]) for this
     * person, keyed on their Twitch id (the overrides table has no local-user FK — see [ShoutoutOverride]).
     * Returns null on success, or the backend's message on failure.
     */
    // Every write below returns the backend's error message (null on success) AND, on failure, sets it on
    // [ViewerProfileState.Ready.actionError] via [failWrite] — the same signal [ban]/[addVip]/[setTrust] already
    // use. Belt and braces: a caller that captures the return value shows the error inline (the overrides fields
    // do — the SAME pattern the old Community "Messages" section used), and a caller that fires-and-forgets
    // (Profile's quick TTS/role pickers) still surfaces it through the page's top banner rather than swallowing
    // it silently.
    suspend fun saveOverrideMessage(kind: String, messageTemplate: String): String? {
        val channel: String = channelId ?: return NoChannelError
        val target: String = requireTwitchId() ?: return NoTwitchIdError
        val name: String = (_state.value as? ViewerProfileState.Ready)?.profile?.identity?.displayName ?: target
        return when (
            val result =
                moderationApi.setShoutoutOverride(channel, target, name, messageTemplate, kind)
        ) {
            is ApiResult.Ok -> {
                refresh(isInitial = false)
                null
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                result.error.message
            }
        }
    }

    /** Clear this channel's [kind] line for this person. Returns null on success, else the backend's message. */
    suspend fun clearOverrideMessage(kind: String): String? {
        val channel: String = channelId ?: return NoChannelError
        val target: String = requireTwitchId() ?: return NoTwitchIdError
        return when (val result = moderationApi.deleteShoutoutOverride(channel, target, kind)) {
            is ApiResult.Ok -> {
                refresh(isInitial = false)
                null
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                result.error.message
            }
        }
    }

    /** Assign this person's TTS voice. Returns null on success, else the backend's message. */
    suspend fun saveTtsVoice(voiceId: String): String? {
        val channel: String = channelId ?: return NoChannelError
        val target: String = requireTwitchId() ?: return NoTwitchIdError
        return when (val result = ttsApi.setUserVoice(channel, target, voiceId)) {
            is ApiResult.Ok -> {
                refresh(isInitial = false)
                null
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                result.error.message
            }
        }
    }

    /** Clear this person's TTS voice override (they fall back to the channel default). */
    suspend fun clearTtsVoice(): String? {
        val channel: String = channelId ?: return NoChannelError
        val target: String = requireTwitchId() ?: return NoTwitchIdError
        return when (val result = ttsApi.clearUserVoice(channel, target)) {
            is ApiResult.Ok -> {
                refresh(isInitial = false)
                null
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                result.error.message
            }
        }
    }

    /** Assign this person's management role (Moderator/SuperMod/Editor/Broadcaster). Roles:manage floor. */
    suspend fun setManagementRole(role: ManagementRole) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val person: String = userId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.assignRole(channel, person, role))
    }

    /** Remove this person's management role. */
    suspend fun removeManagementRole() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val person: String = userId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.removeRole(channel, person))
    }

    /** Grant this person a whole management role via a delegated permit (optionally expiring). */
    suspend fun grantPermitRole(role: ManagementRole, expiresAt: String?, reason: String?) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val person: String = userId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.grantRole(channel, person, role, expiresAt, reason))
    }

    /** Grant this person a single capability permit (optionally expiring). */
    suspend fun grantPermitCapability(actionKey: String, expiresAt: String?, reason: String?) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val person: String = userId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.grantCapability(channel, person, actionKey, expiresAt, reason))
    }

    /** Revoke one of this person's active permit grants. */
    suspend fun revokePermit(actionKeyOrRole: String?) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val person: String = userId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.revokePermit(channel, person, actionKeyOrRole))
    }

    /** This person's stored free-form key/value data (the "Community Data" editor). */
    suspend fun getViewerData(): Map<String, String>? {
        val person: String = userId ?: return null
        return when (val result = viewerDataApi.getData(person)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }
    }

    /** Upsert one custom-data [key]=[value]. Returns null on success, or the backend's error message. */
    suspend fun setViewerDatum(key: String, value: String): String? {
        val person: String = userId ?: return NoChannelError
        return when (val result = viewerDataApi.setDatum(person, key, value)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> result.error.message
        }
    }

    /** Delete one custom-data [key]. Returns null on success, or the backend's error message. */
    suspend fun deleteViewerDatum(key: String): String? {
        val person: String = userId ?: return NoChannelError
        return when (val result = viewerDataApi.deleteDatum(person, key)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> result.error.message
        }
    }

    /** Set this person's community trust level (the legacy per-channel trust config), then reload. */
    suspend fun setTrust(level: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val target: String = requireTwitchId() ?: return failWrite(NoTwitchIdError)
        afterWrite(communityApi.setTrust(channel, target, level))
    }

    /** Ban this person from the channel via Twitch, then reload. */
    suspend fun ban(reason: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val target: String = requireTwitchId() ?: return failWrite(NoTwitchIdError)
        afterWrite(communityApi.ban(channel, target, reason))
    }

    /** Lift this person's ban, then reload. */
    suspend fun unban() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val target: String = requireTwitchId() ?: return failWrite(NoTwitchIdError)
        afterWrite(communityApi.unban(channel, target))
    }

    /** Grant this person VIP status on Twitch, then reload. */
    suspend fun addVip() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val target: String = requireTwitchId() ?: return failWrite(NoTwitchIdError)
        afterWrite(communityApi.addVip(channel, target))
    }

    /** Revoke this person's VIP status on Twitch, then reload. */
    suspend fun removeVip() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val target: String = requireTwitchId() ?: return failWrite(NoTwitchIdError)
        afterWrite(communityApi.removeVip(channel, target))
    }

    /** Send a `/shoutout` to this person right now (fire-and-forget; no reload needed). */
    suspend fun shoutoutNow() {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val target: String = requireTwitchId() ?: return failWrite(NoTwitchIdError)
        when (val result: ApiResult<Unit> = communityApi.shoutout(channel, target)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Fulfil a right-of-access request for this person and hand the document to the OS (broadcaster-only,
     * mirrors the export the old Community stats dialog offered). Returns null on success, an error on failure.
     */
    suspend fun exportUserData(): String? {
        val person: String = userId ?: return NoChannelError
        return when (val result: ApiResult<DataExport> = gdprApi.exportSubject(person, channelId)) {
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                result.error.message
            }
            is ApiResult.Ok ->
                if (
                    fileBridge.saveFile(
                        suggestedName = "nomnomz-subject-$person.json",
                        bytes = result.value.document.encodeToByteArray(),
                    )
                ) {
                    null
                } else {
                    // The user closed the save dialog — nothing failed, nothing was delivered.
                    null
                }
        }
    }

    /** Permanently erase this person's data (GDPR erasure, broadcaster-only, irreversible). */
    suspend fun eraseUserData(): String? {
        val person: String = userId ?: return NoChannelError
        return when (val result: ApiResult<Unit> = usersApi.erase(person)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                result.error.message
            }
        }
    }

    private fun requireTwitchId(): String? =
        (_state.value as? ViewerProfileState.Ready)?.profile?.identity?.twitchUserId

    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> refresh(isInitial = false)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: ViewerProfileState = _state.value
        if (current is ViewerProfileState.Ready) feedback.error(Res.string.community_action_error, detail)
        else _state.value = ViewerProfileState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
        const val NoTwitchIdError: String =
            "This person has no linked Twitch identity yet — this action needs one."
        const val HistoryPageSize: Int = 20
    }
}

/** The Community Profile page render state. */
sealed interface ViewerProfileState {
    data object Loading : ViewerProfileState

    /**
     * The person's full profile plus a page of their moderation history log. A failed write announces on the
     * shell-level feedback toast rather than a field here — see [ViewerProfileController.failWrite]. (Some
     * writes ALSO return their error message directly — see [ViewerProfileController.saveOverrideMessage] —
     * for the caller that shows it inline next to the specific field it belongs to.)
     */
    data class Ready(
        val profile: ViewerProfileSummary,
        val history: List<ModerationHistoryEntry>,
        val historyHasMore: Boolean,
        val availableVoices: List<TtsVoice> = emptyList(),
        val isBanned: Boolean = false,
    ) : ViewerProfileState

    data class Error(val detail: String) : ViewerProfileState
}
