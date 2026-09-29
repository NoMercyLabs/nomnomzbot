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
import bot.nomnomz.dashboard.core.network.BuiltinReplyDefault
import bot.nomnomz.dashboard.core.network.BuiltinReplyDefaultChange
import bot.nomnomz.dashboard.core.network.EventResponseDefault
import bot.nomnomz.dashboard.core.network.EventResponseDefaultChange
import bot.nomnomz.dashboard.core.network.PlatformDefaultBlastRadius
import bot.nomnomz.dashboard.core.network.PlatformDefaultsApi
import bot.nomnomz.dashboard.core.network.SetActionDefaultRequest
import bot.nomnomz.dashboard.core.network.SetBuiltinReplyDefaultRequest
import bot.nomnomz.dashboard.core.network.SetEventResponseDefaultRequest
import bot.nomnomz.dashboard.core.network.SetTtsVoiceDefaultRequest
import bot.nomnomz.dashboard.core.network.TtsVoiceCandidate
import bot.nomnomz.dashboard.core.network.TtsVoiceDefault
import bot.nomnomz.dashboard.core.network.TtsVoiceDefaultChange

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
    replies: List<BuiltinReplyDefault> = emptyList(),
    voice: TtsVoiceDefault? = null,
    voices: List<TtsVoiceCandidate> = emptyList(),
) : PlatformDefaultsApi {
    /** Set to make the matching list-load call fail once, proving a section's loading/error state actually reacts. */
    var failActionDefaults: ApiError? = null
    var failEventResponseDefaults: ApiError? = null
    var failBuiltinReplyDefaults: ApiError? = null
    var failTtsVoiceCandidates: ApiError? = null

    private var voiceRow: TtsVoiceDefault? = voice
    private var voiceRows: List<TtsVoiceCandidate> = voices
    val voicePreviews: MutableList<TtsVoiceDefaultChange> = mutableListOf()
    val voiceSaves: MutableList<SetTtsVoiceDefaultRequest> = mutableListOf()

    private val eventRows: MutableMap<String, EventResponseDefault> = events.associateBy { it.eventType }.toMutableMap()
    val eventPreviews: MutableList<Pair<String, EventResponseDefaultChange>> = mutableListOf()
    val eventSaves: MutableList<Pair<String, SetEventResponseDefaultRequest>> = mutableListOf()

    private val replyRows: MutableMap<Pair<String, String>, BuiltinReplyDefault> =
        replies.associateBy { it.builtinKey to it.slot }.toMutableMap()
    val replyPreviews: MutableList<Pair<Pair<String, String>, BuiltinReplyDefaultChange>> = mutableListOf()
    val replySaves: MutableList<Pair<Pair<String, String>, SetBuiltinReplyDefaultRequest>> = mutableListOf()

    private val rows: MutableMap<String, ActionDefault> = actions.associateBy { it.actionKey }.toMutableMap()
    val previews: MutableList<Pair<String, Int?>> = mutableListOf()
    val saves: MutableList<Pair<String, SetActionDefaultRequest>> = mutableListOf()

    override suspend fun actionDefaults(): ApiResult<List<ActionDefault>> =
        failActionDefaults?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(rows.values.toList())

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
        failEventResponseDefaults?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(eventRows.values.toList())

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

    override suspend fun builtinReplyDefaults(): ApiResult<List<BuiltinReplyDefault>> =
        failBuiltinReplyDefaults?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(replyRows.values.toList())

    override suspend fun previewBuiltinReplyDefault(
        builtinKey: String,
        slot: String,
        change: BuiltinReplyDefaultChange,
    ): ApiResult<PlatformDefaultBlastRadius> {
        replyPreviews += (builtinKey to slot) to change
        val row: BuiltinReplyDefault =
            replyRows[builtinKey to slot] ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no slot"))
        return ApiResult.Ok(replyRadius(row, change))
    }

    override suspend fun setBuiltinReplyDefault(
        builtinKey: String,
        slot: String,
        body: SetBuiltinReplyDefaultRequest,
    ): ApiResult<BuiltinReplyDefault> {
        replySaves += (builtinKey to slot) to body
        val row: BuiltinReplyDefault =
            replyRows[builtinKey to slot] ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no slot"))
        if (replyRadius(row, BuiltinReplyDefaultChange(body.template)).channelsAffected != body.confirmedChannelsAffected) {
            return ApiResult.Failure(ApiError(409, "PREVIEW_STALE", "stale"))
        }
        val saved: BuiltinReplyDefault = row.copy(platformTemplate = body.template)
        replyRows[builtinKey to slot] = saved
        return ApiResult.Ok(saved)
    }

    override suspend fun ttsVoiceDefault(): ApiResult<TtsVoiceDefault> =
        voiceRow?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(404, "NOT_FOUND", "no default voice"))

    override suspend fun ttsVoiceCandidates(): ApiResult<List<TtsVoiceCandidate>> =
        failTtsVoiceCandidates?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(voiceRows)

    override suspend fun previewTtsVoiceDefault(change: TtsVoiceDefaultChange): ApiResult<PlatformDefaultBlastRadius> {
        voicePreviews += change
        val candidate: TtsVoiceCandidate =
            voiceRows.firstOrNull { it.voiceId == change.voiceId }
                ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no voice"))
        return ApiResult.Ok(voiceRadius(candidate))
    }

    override suspend fun setTtsVoiceDefault(body: SetTtsVoiceDefaultRequest): ApiResult<TtsVoiceDefault> {
        voiceSaves += body
        val candidate: TtsVoiceCandidate =
            voiceRows.firstOrNull { it.voiceId == body.voiceId }
                ?: return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no voice"))
        if (voiceRadius(candidate).channelsAffected != body.confirmedChannelsAffected) {
            return ApiResult.Failure(ApiError(409, "PREVIEW_STALE", "stale"))
        }
        val saved = TtsVoiceDefault(
            voiceId = candidate.voiceId,
            displayName = candidate.displayName,
            locale = candidate.locale,
            provider = "edge",
            channelsFollowing = followers,
            channelsWithOwnVoice = keeping,
        )
        voiceRow = saved
        voiceRows = voiceRows.map { it.copy(isDefault = it.voiceId == saved.voiceId) }
        return ApiResult.Ok(saved)
    }

    // The server's rule: a different voice reaches every channel that never picked its own; the current voice
    // changes nobody.
    private fun voiceRadius(candidate: TtsVoiceCandidate): PlatformDefaultBlastRadius {
        val changes: Boolean = candidate.voiceId != voiceRow?.voiceId
        return PlatformDefaultBlastRadius(
            channelsAffected = if (changes) followers else 0,
            channelsKeepingOwnSetting = keeping,
            sampleChannelNames = if (changes) sample else emptyList(),
        )
    }

    // The server's rule: a wording change reaches every channel except those answering with their own reply
    // (only on slots that take one); an unchanged wording changes nobody.
    private fun replyRadius(row: BuiltinReplyDefault, change: BuiltinReplyDefaultChange): PlatformDefaultBlastRadius {
        val changes: Boolean = row.platformTemplate != change.template
        return PlatformDefaultBlastRadius(
            channelsAffected = if (changes) followers else 0,
            channelsKeepingOwnSetting = row.channelsWithOwnReply,
            sampleChannelNames = if (changes) sample else emptyList(),
        )
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
