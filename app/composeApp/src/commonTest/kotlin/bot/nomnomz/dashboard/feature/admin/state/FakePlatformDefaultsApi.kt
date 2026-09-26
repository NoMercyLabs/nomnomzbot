// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.ActionDefault
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.EventResponseDefault
import bot.nomnomz.dashboard.core.network.EventResponseDefaultChange
import bot.nomnomz.dashboard.core.network.PlatformDefaultBlastRadius
import bot.nomnomz.dashboard.core.network.PlatformDefaultsApi
import bot.nomnomz.dashboard.core.network.SetActionDefaultRequest
import bot.nomnomz.dashboard.core.network.SetEventResponseDefaultRequest

/**
 * An in-memory platform-defaults backend with the server's rules: the preview counts the followers only when
 * the level actually changes, and a save with a count that differs from the live one is refused
 * (`PREVIEW_STALE`). Records every call so a test can prove what was sent.
 */
internal class FakePlatformDefaultsApi(
    actions: List<ActionDefault>,
    var followers: Int = 3,
    var keeping: Int = 1,
    var sample: List<String> = listOf("alpha", "bravo"),
    events: List<EventResponseDefault> = emptyList(),
) : PlatformDefaultsApi {
    private val eventRows: MutableMap<String, EventResponseDefault> = events.associateBy { it.eventType }.toMutableMap()
    val eventPreviews: MutableList<Pair<String, EventResponseDefaultChange>> = mutableListOf()
    val eventSaves: MutableList<Pair<String, SetEventResponseDefaultRequest>> = mutableListOf()

    private val rows: MutableMap<String, ActionDefault> = actions.associateBy { it.actionKey }.toMutableMap()
    val previews: MutableList<Pair<String, Int?>> = mutableListOf()
    val saves: MutableList<Pair<String, SetActionDefaultRequest>> = mutableListOf()

    override suspend fun actionDefaults(): ApiResult<List<ActionDefault>> = ApiResult.Ok(rows.values.toList())

    override suspend fun previewActionDefault(actionKey: String, level: Int?): ApiResult<PlatformDefaultBlastRadius> {
        previews += actionKey to level
        val row: ActionDefault = rows[actionKey] ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no action"))
        return ApiResult.Ok(radius(row, level))
    }

    override suspend fun setActionDefault(actionKey: String, body: SetActionDefaultRequest): ApiResult<ActionDefault> {
        saves += actionKey to body
        val row: ActionDefault = rows[actionKey] ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no action"))
        if (radius(row, body.level).channelsAffected != body.confirmedChannelsAffected) {
            return ApiResult.Failure(ApiError(409, "PREVIEW_STALE", "stale"))
        }
        val saved: ActionDefault = row.copy(
            platformDefaultLevel = body.level,
            effectiveDefaultLevel = body.level ?: row.shippedDefaultLevel,
        )
        rows[actionKey] = saved
        return ApiResult.Ok(saved)
    }

    override suspend fun eventResponseDefaults(): ApiResult<List<EventResponseDefault>> =
        ApiResult.Ok(eventRows.values.toList())

    override suspend fun previewEventResponseDefault(
        eventType: String,
        change: EventResponseDefaultChange,
    ): ApiResult<PlatformDefaultBlastRadius> {
        eventPreviews += eventType to change
        val row: EventResponseDefault = eventRows[eventType] ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no event"))
        return ApiResult.Ok(eventRadius(row, change))
    }

    override suspend fun setEventResponseDefault(
        eventType: String,
        body: SetEventResponseDefaultRequest,
    ): ApiResult<EventResponseDefault> {
        eventSaves += eventType to body
        val row: EventResponseDefault = eventRows[eventType] ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no event"))
        val change = EventResponseDefaultChange(body.isEnabled, body.message)
        if (eventRadius(row, change).channelsAffected != body.confirmedChannelsAffected) {
            return ApiResult.Failure(ApiError(409, "PREVIEW_STALE", "stale"))
        }
        val saved: EventResponseDefault = row.copy(isEnabled = body.isEnabled, message = body.message)
        eventRows[eventType] = saved
        return ApiResult.Ok(saved)
    }

    private fun eventRadius(row: EventResponseDefault, change: EventResponseDefaultChange): PlatformDefaultBlastRadius {
        val changes: Boolean = row.isEnabled != change.isEnabled || row.message != change.message
        return PlatformDefaultBlastRadius(
            channelsAffected = if (changes) row.channelsFollowing else 0,
            channelsKeepingOwnSetting = row.channelsWithOwnResponse,
            sampleChannelNames = if (changes) sample else emptyList(),
        )
    }

    private fun radius(row: ActionDefault, level: Int?): PlatformDefaultBlastRadius {
        val changes: Boolean = (level ?: row.shippedDefaultLevel) != row.effectiveDefaultLevel
        return PlatformDefaultBlastRadius(
            channelsAffected = if (changes) followers else 0,
            channelsKeepingOwnSetting = keeping,
            sampleChannelNames = if (changes) sample else emptyList(),
            requiresDangerConfirmation = row.isDangerous,
        )
    }
}
