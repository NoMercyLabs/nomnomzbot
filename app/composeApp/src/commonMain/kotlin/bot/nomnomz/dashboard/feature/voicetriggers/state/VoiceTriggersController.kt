// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.voicetriggers.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelAsset
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.AssetsApi
import bot.nomnomz.dashboard.core.network.CreateVoiceTriggerBody
import bot.nomnomz.dashboard.core.network.UpdateVoiceTriggerBody
import bot.nomnomz.dashboard.core.network.VoiceTrigger
import bot.nomnomz.dashboard.core.network.VoiceTriggersApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_voice_trigger_deleted
import nomnomzbot.composeapp.generated.resources.feedback_voice_trigger_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_voice_trigger_saved

// The Voice Triggers page's state-holder (Chat group, beside Chat Triggers). Resolves the active channel, then
// lists its real voice triggers and the channel's uploaded assets (for the sticker picker) from the backend —
// no fabricated rows. Also resolves the listener-page link the "copy your listener link" button copies.
class VoiceTriggersController(
    private val channelsApi: ChannelsApi,
    private val voiceTriggersApi: VoiceTriggersApi,
    private val assetsApi: AssetsApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<VoiceTriggersState> = MutableStateFlow(VoiceTriggersState.Loading)
    val state: StateFlow<VoiceTriggersState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then list its voice triggers, assets, and listener link. */
    suspend fun load() {
        if (_state.value !is VoiceTriggersState.Ready) _state.value = VoiceTriggersState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = VoiceTriggersState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val triggersResult: ApiResult<List<VoiceTrigger>> = voiceTriggersApi.list(channel.id)
        val assetsResult: ApiResult<List<ChannelAsset>> = assetsApi.list()
        val linkResult: ApiResult<String> = voiceTriggersApi.listenerLink(channel.id)

        val triggers: List<VoiceTrigger> =
            when (triggersResult) {
                is ApiResult.Failure -> {
                    _state.value = VoiceTriggersState.Error(triggersResult.error.message)
                    return
                }
                is ApiResult.Ok -> triggersResult.value
            }
        val assets: List<ChannelAsset> =
            if (assetsResult is ApiResult.Ok) assetsResult.value.filter { it.kind == "image" } else emptyList()
        val listenerLink: String? = if (linkResult is ApiResult.Ok) linkResult.value else null

        _state.value = VoiceTriggersState.Ready(triggers = triggers, assets = assets, listenerLink = listenerLink)
    }

    /** Create a trigger, then reload so the new row (and its live count) appears. */
    suspend fun createTrigger(
        word: String,
        startingCount: Int,
        cooldownSeconds: Int,
        isEnabled: Boolean,
        stickerAssetId: String?,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            voiceTriggersApi.create(
                channel,
                CreateVoiceTriggerBody(
                    word = word.trim(),
                    isEnabled = isEnabled,
                    startingCount = startingCount,
                    cooldownSeconds = cooldownSeconds,
                    stickerAssetId = stickerAssetId,
                ),
            )
        )
    }

    /** Edit a trigger, addressed by its [triggerId]. Note: [startingCount] is NOT editable — it is a one-time seed. */
    suspend fun updateTrigger(
        triggerId: String,
        word: String,
        cooldownSeconds: Int,
        isEnabled: Boolean,
        stickerAssetId: String?,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            voiceTriggersApi.update(
                channel,
                triggerId,
                UpdateVoiceTriggerBody(
                    word = word.trim(),
                    isEnabled = isEnabled,
                    cooldownSeconds = cooldownSeconds,
                    stickerAssetId = stickerAssetId,
                ),
            )
        )
    }

    /** Flip a trigger's enabled flag via the update endpoint. Reloads. */
    suspend fun toggleTrigger(triggerId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(voiceTriggersApi.update(channel, triggerId, UpdateVoiceTriggerBody(isEnabled = enabled)))
    }

    /** Delete a trigger, addressed by its [triggerId]. Reloads on success. */
    suspend fun deleteTrigger(triggerId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(voiceTriggersApi.delete(channel, triggerId), success = Res.string.feedback_voice_trigger_deleted)
    }

    private suspend fun afterWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_voice_trigger_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                load()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: VoiceTriggersState = _state.value
        if (current is VoiceTriggersState.Ready) {
            feedback.error(Res.string.feedback_voice_trigger_save_failed, detail)
        } else {
            _state.value = VoiceTriggersState.Error(detail)
        }
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Voice Triggers page render state. */
sealed interface VoiceTriggersState {
    data object Loading : VoiceTriggersState

    /**
     * The channel's triggers are listed, with [assets] (image assets only, for the sticker picker) and the
     * [listenerLink] ready-to-copy URL (null only if the backend call itself failed — the page still renders).
     */
    data class Ready(
        val triggers: List<VoiceTrigger>,
        val assets: List<ChannelAsset> = emptyList(),
        val listenerLink: String? = null,
    ) : VoiceTriggersState

    data class Error(val detail: String) : VoiceTriggersState
}
