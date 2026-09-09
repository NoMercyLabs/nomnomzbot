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

import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityPage
import bot.nomnomz.dashboard.core.network.ViewerOption
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Community DIRECTORY page's state-holder (owner punch list 2026-09-08 §3A) — a search-first list for
// finding someone fast. Resolves the active channel, then loads its real member list from the backend (Twitch
// API + chat history; no fabricated viewers). Every management action on a person — trust, ban/VIP, TTS voice,
// overrides, permits, GDPR — lives on the Community PROFILE page ([ViewerProfileController]) that a Directory
// row opens, never duplicated here: this controller's only job is finding and sorting, matching the owner's
// "mirrors how Moderation was already split by job instead of topic" framing. The screen renders [state]; a
// pull / reconnect calls [load] again.
class CommunityController(
    private val channelsApi: ChannelsApi,
    private val communityApi: CommunityApi,
) {
    private val _state: MutableStateFlow<CommunityState> = MutableStateFlow(CommunityState.Loading)

    /** The page render state: loading / ready (with the members) / empty / error. */
    val state: StateFlow<CommunityState> = _state.asStateFlow()

    // The channel the reads target — resolved by [load] and reused by [selectRole]/paging/search so they never
    // have to re-resolve it. Null until the first successful resolve.
    private var channelId: String? = null

    // The paging cursor the page currently sits on. The role-filtered member list is served one page at a time:
    // the page-numbered tabs walk [currentPage]; the cursor-paginated followers tab walks [followerCursorTrail]
    // (each entry the cursor that fetched a page, the last one the current page), so prev can step back.
    // [lastNextCursor] is the follower continuation the backend just handed us.
    private var currentRole: String = CommunityRole.All
    private var currentPage: Int = 1
    private var lastNextCursor: String? = null
    private val followerCursorTrail: ArrayDeque<String?> = ArrayDeque()

    /** Resolve the active channel, then load the first page of all members. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch keeps the current content on screen
        // (no flash) and swaps it when the new data arrives.
        if (_state.value !is CommunityState.Ready) _state.value = CommunityState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = CommunityState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        // Reset to the first page of the unfiltered list.
        currentRole = CommunityRole.All
        currentPage = 1
        lastNextCursor = null
        followerCursorTrail.clear()
        fetchPage(isInitial = true)
    }

    /** Switch the active role filter (all / follower / vip / moderator) and load its first page. */
    suspend fun selectRole(role: String) {
        currentRole = role
        currentPage = 1
        lastNextCursor = null
        followerCursorTrail.clear()
        // The followers tab is cursor-paginated: seed the trail with the first-page (null) cursor so prev/next
        // and the has-prev signal can walk it.
        if (role == CommunityRole.Follower) followerCursorTrail.addLast(null)
        fetchPage(isInitial = false)
    }

    /** Advance to the next page of the current role filter. The screen only calls this while `hasMore` is true. */
    suspend fun nextPage() {
        if (currentRole == CommunityRole.Follower) {
            val next: String = lastNextCursor ?: return
            followerCursorTrail.addLast(next)
        } else {
            currentPage += 1
        }
        fetchPage(isInitial = false)
    }

    /** Step back to the previous page of the current role filter. A no-op on the first page. */
    suspend fun prevPage() {
        if (currentRole == CommunityRole.Follower) {
            if (followerCursorTrail.size <= 1) return
            followerCursorTrail.removeLast()
        } else {
            if (currentPage <= 1) return
            currentPage -= 1
        }
        fetchPage(isInitial = false)
    }

    /**
     * Autocomplete over the channel's known viewers by name (the "reach a viewer beyond this page" search). Each
     * [PickerOption.id] is the viewer's Twitch user id. Best-effort: no resolved channel or a failed search
     * yields an empty list so the search shows "no matches" rather than an error.
     */
    suspend fun searchViewers(query: String): List<PickerOption> {
        val channel: String = channelId ?: return emptyList()
        return when (val result: ApiResult<List<ViewerOption>> = communityApi.searchViewers(channel, query)) {
            is ApiResult.Ok -> result.value.map { PickerOption(id = it.id, label = it.label, sublabel = it.subLabel) }
            is ApiResult.Failure -> emptyList()
        }
    }

    /**
     * Resolve a search hit's real member state — critically, its [CommunityMember.internalUserId], the id a
     * Directory row needs to open that person's Profile (the search endpoint only returns a Twitch id). Returns
     * null when the channel hasn't resolved or the lookup fails.
     */
    suspend fun memberDetail(twitchUserId: String): CommunityMember? {
        val channel: String = channelId ?: return null
        return when (val result: ApiResult<CommunityMember> = communityApi.member(channel, twitchUserId)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }
    }

    // Fetch the current role/page, sort it "most recently active first" (the default the owner asked for —
    // [CommunityMember.lastSeen] is an ISO-8601 string, so a plain descending string sort orders it correctly),
    // and project it. [isInitial] distinguishes the first full-page load (a failure or a truly empty community
    // becomes a full-page Error/Empty) from a navigation/reload (a failure surfaces over the kept list, an
    // empty filtered role just shows an empty list under its filter).
    //
    // NOTE: this sorts each fetched PAGE, not the whole community — the backend's `GET /community` has no
    // `sort=recentlyActive` query parameter today (`CommunityController.ListMembers` orders the "all" role by
    // Twitch id only), so a true cross-page recency ordering needs that backend support added first; flagged,
    // not silently faked as a page-only sort would otherwise look like.
    private suspend fun fetchPage(isInitial: Boolean) {
        val channel: String =
            channelId
                ?: run {
                    if (isInitial) _state.value = CommunityState.Error(NoChannelError) else failWrite(NoChannelError)
                    return
                }
        val cursor: String? =
            if (currentRole == CommunityRole.Follower) followerCursorTrail.lastOrNull() else null
        when (
            val result: ApiResult<CommunityPage> =
                communityApi.membersPage(channel, currentRole, currentPage, PageSize, cursor)
        ) {
            is ApiResult.Failure ->
                if (isInitial) _state.value = CommunityState.Error(result.error.message)
                else failWrite(result.error.message)
            is ApiResult.Ok -> {
                val page: CommunityPage = result.value
                lastNextCursor = page.nextCursor
                val members: List<CommunityMember> = page.data.sortedByDescending { it.lastSeen }
                _state.value =
                    if (
                        isInitial &&
                        members.isEmpty() &&
                        currentRole == CommunityRole.All &&
                        currentPage == 1
                    ) {
                        CommunityState.Empty
                    } else {
                        CommunityState.Ready(
                            members = members,
                            role = currentRole,
                            page = currentPage,
                            hasPrev =
                                if (currentRole == CommunityRole.Follower) followerCursorTrail.size > 1
                                else currentPage > 1,
                            hasMore = page.hasMore,
                            total = page.total,
                        )
                    }
            }
        }
    }

    private fun failWrite(detail: String) {
        val current: CommunityState = _state.value
        _state.value =
            if (current is CommunityState.Ready) current.copy(actionError = detail)
            else CommunityState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
        const val PageSize: Int = 25
    }
}

/**
 * The role filters the Directory offers (the backend `role` query value). "all" is the unfiltered list (the
 * default, sorted most-recently-active first); [Follower] is served cursor-paginated straight from Twitch, the
 * rest are page-numbered.
 */
object CommunityRole {
    const val All: String = "all"
    const val Follower: String = "follower"
    const val Vip: String = "vip"
    const val Moderator: String = "moderator"

    /** Ordered for the filter control. */
    val tabs: List<String> = listOf(All, Follower, Vip, Moderator)
}

/** The Community Directory page render state. */
sealed interface CommunityState {
    data object Loading : CommunityState

    /**
     * The channel's members are listed, one page of the active [role] filter, sorted most-recently-active
     * first. [page] is the 1-based page number; [hasPrev]/[hasMore] drive the prev/next controls and [total]
     * the "of N" count when the backend knows it. [actionError] is non-null only when the last page fetch or
     * search failed — surfaced as a transient banner while the list stays rendered.
     */
    data class Ready(
        val members: List<CommunityMember>,
        val role: String = CommunityRole.All,
        val page: Int = 1,
        val hasPrev: Boolean = false,
        val hasMore: Boolean = false,
        val total: Int? = null,
        val actionError: String? = null,
    ) : CommunityState

    data object Empty : CommunityState

    data class Error(val detail: String) : CommunityState
}
