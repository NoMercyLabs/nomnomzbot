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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ActionDangerTier
import bot.nomnomz.dashboard.core.network.ActionDefault
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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_defaults_error
import nomnomzbot.composeapp.generated.resources.platform_defaults_saved
import nomnomzbot.composeapp.generated.resources.platform_defaults_stale

/**
 * The draft of one platform-default edit: which action, the level the operator picked (null = back to the
 * shipped default), the server's counted blast radius for exactly that pick, and the danger acknowledgement.
 * [preview] is null while it loads — the save stays disabled until the operator has seen the real count.
 */
data class ActionDefaultEdit(
    val actionKey: String,
    val level: Int?,
    val preview: PlatformDefaultBlastRadius? = null,
    val dangerAcknowledged: Boolean = false,
    val saving: Boolean = false,
) {
    /** The save is armed only once the counted blast radius for THIS pick is on screen (and, for a dangerous
     * action, the operator ticked the acknowledgement). */
    val canSave: Boolean
        get() = preview != null && !saving && (!preview.requiresDangerConfirmation || dangerAcknowledged)
}

/**
 * The draft of one event-response default edit. Any change to [isEnabled], [message] or [speakWithTts] drops
 * [preview], so the save can only ever carry a count the operator saw for exactly the values being saved.
 */
data class EventResponseDefaultEdit(
    val eventType: String,
    val isEnabled: Boolean,
    val message: String,
    val speakWithTts: Boolean = false,
    val preview: PlatformDefaultBlastRadius? = null,
    val previewing: Boolean = false,
    val saving: Boolean = false,
) {
    val change: EventResponseDefaultChange
        get() = EventResponseDefaultChange(
            isEnabled = isEnabled,
            message = message.trim().ifEmpty { null },
            speakWithTts = speakWithTts,
        )

    /** An enabled default must say something; the save arms only once the count for these values is shown. */
    val canSave: Boolean
        get() = preview != null && !saving && (!isEnabled || message.isNotBlank())
}

/**
 * The draft of one built-in reply wording edit. [useShipped] means "back to the shipped wording" (a null
 * template on the wire); otherwise [template] is the platform wording. Any change drops [preview], so the save
 * can only ever carry a count the operator saw for exactly the wording being saved.
 */
data class BuiltinReplyDefaultEdit(
    val builtinKey: String,
    val slot: String,
    val template: String,
    val useShipped: Boolean,
    val preview: PlatformDefaultBlastRadius? = null,
    val previewing: Boolean = false,
    val saving: Boolean = false,
) {
    val change: BuiltinReplyDefaultChange
        get() = BuiltinReplyDefaultChange(template = if (useShipped) null else template.trim().ifEmpty { null })

    /** A platform wording must say something; the save arms only once the count for this wording is shown. */
    val canSave: Boolean
        get() = preview != null && !saving && (useShipped || template.isNotBlank())
}

/**
 * The draft of the platform default voice edit: the candidate picked so far. A different pick drops
 * [preview], so the save can only ever carry a count the operator saw for exactly the voice being saved.
 */
data class TtsVoiceDefaultEdit(
    val voiceId: String,
    val preview: PlatformDefaultBlastRadius? = null,
    val previewing: Boolean = false,
    val saving: Boolean = false,
) {
    val change: TtsVoiceDefaultChange
        get() = TtsVoiceDefaultChange(voiceId = voiceId)

    /** The save arms only once the count for this very pick is shown. */
    val canSave: Boolean
        get() = preview != null && !saving
}

data class PlatformDefaultsState(
    val actionDefaults: List<ActionDefault> = emptyList(),
    val actionsLoaded: Boolean = false,
    val actionsLoading: Boolean = false,
    val actionsError: String? = null,
    val actionFilter: String = "",
    val actionEdit: ActionDefaultEdit? = null,
    val eventDefaults: List<EventResponseDefault> = emptyList(),
    val eventsLoaded: Boolean = false,
    val eventsLoading: Boolean = false,
    val eventsError: String? = null,
    val eventEdit: EventResponseDefaultEdit? = null,
    val replyDefaults: List<BuiltinReplyDefault> = emptyList(),
    val repliesLoaded: Boolean = false,
    val repliesLoading: Boolean = false,
    val repliesError: String? = null,
    val replyEdit: BuiltinReplyDefaultEdit? = null,
    val voiceDefault: TtsVoiceDefault? = null,
    val voiceCandidates: List<TtsVoiceCandidate> = emptyList(),
    val voiceLoaded: Boolean = false,
    val voiceLoading: Boolean = false,
    val voiceError: String? = null,
    val voiceEdit: TtsVoiceDefaultEdit? = null,
) {
    /** The action rows matching the filter (key or description, case-insensitive). */
    val visibleActionDefaults: List<ActionDefault>
        get() {
            val needle: String = actionFilter.trim()
            if (needle.isEmpty()) return actionDefaults
            return actionDefaults.filter { row ->
                row.actionKey.contains(needle, ignoreCase = true) ||
                    (row.description?.contains(needle, ignoreCase = true) == true)
            }
        }
}

/**
 * State holder for the admin "Platform defaults" tab (plan item A4). Every edit follows one law: pick a value,
 * see the server's counted blast radius for exactly that value, then save — the save echoes the previewed
 * count so a moved count is refused rather than applied blind, and the row on screen is replaced by the one
 * the server read back after saving (truthful data, never the local guess).
 */
class PlatformDefaultsController(
    private val api: PlatformDefaultsApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<PlatformDefaultsState> = MutableStateFlow(PlatformDefaultsState())
    val state: StateFlow<PlatformDefaultsState> = _state.asStateFlow()

    suspend fun loadActionDefaults() {
        _state.value = _state.value.copy(actionsLoading = true, actionsError = null)
        when (val result: ApiResult<List<ActionDefault>> = api.actionDefaults()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                actionDefaults = result.value,
                actionsLoaded = true,
                actionsLoading = false,
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(actionsLoading = false, actionsError = result.error.message)
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    fun setActionFilter(filter: String) {
        _state.value = _state.value.copy(actionFilter = filter)
    }

    /** Opens the editor on [actionKey], pre-selected to its current platform default, and fetches its count. */
    suspend fun openActionEdit(actionKey: String) {
        val row: ActionDefault = _state.value.actionDefaults.firstOrNull { it.actionKey == actionKey } ?: return
        pickActionLevel(actionKey, row.platformDefaultLevel)
    }

    /** Picks a level (null = back to the shipped default) and fetches the counted blast radius for it. */
    suspend fun pickActionLevel(actionKey: String, level: Int?) {
        _state.value = _state.value.copy(actionEdit = ActionDefaultEdit(actionKey = actionKey, level = level))
        when (val result: ApiResult<PlatformDefaultBlastRadius> = api.previewActionDefault(actionKey, level)) {
            is ApiResult.Ok -> updateEdit(actionKey, level) { it.copy(preview = result.value) }
            is ApiResult.Failure -> feedback.error(Res.string.platform_defaults_error, result.error.message)
        }
    }

    fun acknowledgeDanger(acknowledged: Boolean) {
        val edit: ActionDefaultEdit = _state.value.actionEdit ?: return
        _state.value = _state.value.copy(actionEdit = edit.copy(dangerAcknowledged = acknowledged))
    }

    fun dismissActionEdit() {
        _state.value = _state.value.copy(actionEdit = null)
    }

    /** Saves the previewed edit; the row is replaced by the server's read-back. A stale count reloads the
     * preview so the operator sees the new number before trying again. */
    suspend fun saveActionEdit() {
        val edit: ActionDefaultEdit = _state.value.actionEdit ?: return
        val preview: PlatformDefaultBlastRadius = edit.preview ?: return
        if (!edit.canSave) return
        _state.value = _state.value.copy(actionEdit = edit.copy(saving = true))
        val body = SetActionDefaultRequest(
            level = edit.level,
            confirmedChannelsAffected = preview.channelsAffected,
            confirmDanger = edit.dangerAcknowledged,
        )
        when (val result: ApiResult<ActionDefault> = api.setActionDefault(edit.actionKey, body)) {
            is ApiResult.Ok -> {
                val saved: ActionDefault = result.value
                _state.value = _state.value.copy(
                    actionDefaults = _state.value.actionDefaults.map { if (it.actionKey == saved.actionKey) saved else it },
                    actionEdit = null,
                )
                feedback.success(Res.string.platform_defaults_saved, preview.channelsAffected)
            }
            is ApiResult.Failure ->
                if (result.error.code == STALE_CODE) {
                    feedback.error(Res.string.platform_defaults_stale)
                    pickActionLevel(edit.actionKey, edit.level)
                } else {
                    updateEdit(edit.actionKey, edit.level) { it.copy(saving = false) }
                    feedback.error(Res.string.platform_defaults_error, result.error.message)
                }
        }
    }

    suspend fun loadEventResponseDefaults() {
        _state.value = _state.value.copy(eventsLoading = true, eventsError = null)
        when (val result: ApiResult<List<EventResponseDefault>> = api.eventResponseDefaults()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                eventDefaults = result.value,
                eventsLoaded = true,
                eventsLoading = false,
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(eventsLoading = false, eventsError = result.error.message)
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    /** Opens the editor on [eventType] with its current platform default. Nothing is previewed until asked. */
    fun openEventEdit(eventType: String) {
        val row: EventResponseDefault = _state.value.eventDefaults.firstOrNull { it.eventType == eventType } ?: return
        _state.value = _state.value.copy(
            eventEdit = EventResponseDefaultEdit(
                eventType = eventType,
                isEnabled = row.isEnabled,
                message = row.message.orEmpty(),
                speakWithTts = row.speakWithTts,
            ),
        )
    }

    fun editEventEnabled(enabled: Boolean) {
        val edit: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        _state.value = _state.value.copy(eventEdit = edit.copy(isEnabled = enabled, preview = null))
    }

    fun editEventSpeakWithTts(speak: Boolean) {
        val edit: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        _state.value = _state.value.copy(eventEdit = edit.copy(speakWithTts = speak, preview = null))
    }

    fun editEventMessage(message: String) {
        val edit: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        _state.value = _state.value.copy(eventEdit = edit.copy(message = message, preview = null))
    }

    /** Fetches the counted blast radius for exactly the values in the editor. */
    suspend fun previewEventEdit() {
        val edit: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        val change: EventResponseDefaultChange = edit.change
        _state.value = _state.value.copy(eventEdit = edit.copy(previewing = true))
        val result: ApiResult<PlatformDefaultBlastRadius> = api.previewEventResponseDefault(edit.eventType, change)
        val current: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        // A preview for values the operator already changed again is dropped, never shown as theirs.
        val stillSame: Boolean = current.eventType == edit.eventType && current.change == change
        when (result) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                eventEdit = current.copy(previewing = false, preview = if (stillSame) result.value else null),
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(eventEdit = current.copy(previewing = false))
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    fun dismissEventEdit() {
        _state.value = _state.value.copy(eventEdit = null)
    }

    /** Saves the previewed values; the row is replaced by the server read-back. A stale count re-previews. */
    suspend fun saveEventEdit() {
        val edit: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        val preview: PlatformDefaultBlastRadius = edit.preview ?: return
        if (!edit.canSave) return
        _state.value = _state.value.copy(eventEdit = edit.copy(saving = true))
        val body = SetEventResponseDefaultRequest(
            isEnabled = edit.change.isEnabled,
            message = edit.change.message,
            confirmedChannelsAffected = preview.channelsAffected,
            speakWithTts = edit.change.speakWithTts,
        )
        when (val result: ApiResult<EventResponseDefault> = api.setEventResponseDefault(edit.eventType, body)) {
            is ApiResult.Ok -> {
                val saved: EventResponseDefault = result.value
                _state.value = _state.value.copy(
                    eventDefaults = _state.value.eventDefaults.map { if (it.eventType == saved.eventType) saved else it },
                    eventEdit = null,
                )
                feedback.success(Res.string.platform_defaults_saved, preview.channelsAffected)
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(eventEdit = edit.copy(saving = false, preview = null))
                if (result.error.code == STALE_CODE) {
                    feedback.error(Res.string.platform_defaults_stale)
                    previewEventEdit()
                } else {
                    feedback.error(Res.string.platform_defaults_error, result.error.message)
                }
            }
        }
    }

    suspend fun loadBuiltinReplyDefaults() {
        _state.value = _state.value.copy(repliesLoading = true, repliesError = null)
        when (val result: ApiResult<List<BuiltinReplyDefault>> = api.builtinReplyDefaults()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                replyDefaults = result.value,
                repliesLoaded = true,
                repliesLoading = false,
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(repliesLoading = false, repliesError = result.error.message)
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    /** Opens the editor on one slot with its current wording. Nothing is previewed until asked. */
    fun openReplyEdit(builtinKey: String, slot: String) {
        val row: BuiltinReplyDefault = _state.value.replyDefaults.firstOrNull {
            it.builtinKey == builtinKey && it.slot == slot
        } ?: return
        _state.value = _state.value.copy(
            replyEdit = BuiltinReplyDefaultEdit(
                builtinKey = builtinKey,
                slot = slot,
                template = row.platformTemplate ?: row.shippedTemplate.orEmpty(),
                useShipped = row.platformTemplate == null,
            ),
        )
    }

    fun editReplyUseShipped(useShipped: Boolean) {
        val edit: BuiltinReplyDefaultEdit = _state.value.replyEdit ?: return
        _state.value = _state.value.copy(replyEdit = edit.copy(useShipped = useShipped, preview = null))
    }

    fun editReplyTemplate(template: String) {
        val edit: BuiltinReplyDefaultEdit = _state.value.replyEdit ?: return
        _state.value = _state.value.copy(replyEdit = edit.copy(template = template, preview = null))
    }

    /** Fetches the counted blast radius for exactly the wording in the editor. */
    suspend fun previewReplyEdit() {
        val edit: BuiltinReplyDefaultEdit = _state.value.replyEdit ?: return
        val change: BuiltinReplyDefaultChange = edit.change
        _state.value = _state.value.copy(replyEdit = edit.copy(previewing = true))
        val result: ApiResult<PlatformDefaultBlastRadius> =
            api.previewBuiltinReplyDefault(edit.builtinKey, edit.slot, change)
        val current: BuiltinReplyDefaultEdit = _state.value.replyEdit ?: return
        // A preview for a wording the operator already changed again is dropped, never shown as theirs.
        val stillSame: Boolean =
            current.builtinKey == edit.builtinKey && current.slot == edit.slot && current.change == change
        when (result) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                replyEdit = current.copy(previewing = false, preview = if (stillSame) result.value else null),
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(replyEdit = current.copy(previewing = false))
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    fun dismissReplyEdit() {
        _state.value = _state.value.copy(replyEdit = null)
    }

    /** Saves the previewed wording; the row is replaced by the server read-back. A stale count re-previews. */
    suspend fun saveReplyEdit() {
        val edit: BuiltinReplyDefaultEdit = _state.value.replyEdit ?: return
        val preview: PlatformDefaultBlastRadius = edit.preview ?: return
        if (!edit.canSave) return
        _state.value = _state.value.copy(replyEdit = edit.copy(saving = true))
        val body = SetBuiltinReplyDefaultRequest(
            template = edit.change.template,
            confirmedChannelsAffected = preview.channelsAffected,
        )
        when (val result: ApiResult<BuiltinReplyDefault> = api.setBuiltinReplyDefault(edit.builtinKey, edit.slot, body)) {
            is ApiResult.Ok -> {
                val saved: BuiltinReplyDefault = result.value
                _state.value = _state.value.copy(
                    replyDefaults = _state.value.replyDefaults.map {
                        if (it.builtinKey == saved.builtinKey && it.slot == saved.slot) saved else it
                    },
                    replyEdit = null,
                )
                feedback.success(Res.string.platform_defaults_saved, preview.channelsAffected)
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(replyEdit = edit.copy(saving = false, preview = null))
                if (result.error.code == STALE_CODE) {
                    feedback.error(Res.string.platform_defaults_stale)
                    previewReplyEdit()
                } else {
                    feedback.error(Res.string.platform_defaults_error, result.error.message)
                }
            }
        }
    }

    /** Loads the platform voice and the candidates it may be set to; a missing default is an empty card, not an error. */
    suspend fun loadTtsVoiceDefault() {
        _state.value = _state.value.copy(voiceLoading = true, voiceError = null)
        val current: TtsVoiceDefault? = when (val result: ApiResult<TtsVoiceDefault> = api.ttsVoiceDefault()) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> {
                if (result.error.code != NOT_FOUND_CODE) {
                    _state.value = _state.value.copy(voiceError = result.error.message)
                    feedback.error(Res.string.platform_defaults_error, result.error.message)
                }
                null
            }
        }
        when (val result: ApiResult<List<TtsVoiceCandidate>> = api.ttsVoiceCandidates()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                voiceDefault = current,
                voiceCandidates = result.value,
                voiceLoaded = true,
                voiceLoading = false,
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(voiceLoading = false, voiceError = result.error.message)
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    /** Opens the editor on the current voice. Nothing is previewed until asked. */
    fun openVoiceEdit() {
        val current: String = _state.value.voiceDefault?.voiceId
            ?: _state.value.voiceCandidates.firstOrNull()?.voiceId
            ?: return
        _state.value = _state.value.copy(voiceEdit = TtsVoiceDefaultEdit(voiceId = current))
    }

    fun pickVoice(voiceId: String) {
        val edit: TtsVoiceDefaultEdit = _state.value.voiceEdit ?: return
        if (edit.voiceId == voiceId) return
        _state.value = _state.value.copy(voiceEdit = edit.copy(voiceId = voiceId, preview = null))
    }

    /** Fetches the counted blast radius for exactly the voice in the editor. */
    suspend fun previewVoiceEdit() {
        val edit: TtsVoiceDefaultEdit = _state.value.voiceEdit ?: return
        val change: TtsVoiceDefaultChange = edit.change
        _state.value = _state.value.copy(voiceEdit = edit.copy(previewing = true))
        val result: ApiResult<PlatformDefaultBlastRadius> = api.previewTtsVoiceDefault(change)
        val current: TtsVoiceDefaultEdit = _state.value.voiceEdit ?: return
        // A preview for a voice the operator already changed again is dropped, never shown as theirs.
        val stillSame: Boolean = current.change == change
        when (result) {
            is ApiResult.Ok -> _state.value = _state.value.copy(
                voiceEdit = current.copy(previewing = false, preview = if (stillSame) result.value else null),
            )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(voiceEdit = current.copy(previewing = false))
                feedback.error(Res.string.platform_defaults_error, result.error.message)
            }
        }
    }

    fun dismissVoiceEdit() {
        _state.value = _state.value.copy(voiceEdit = null)
    }

    /** Saves the previewed voice; the card becomes the server read-back. A stale count re-previews. */
    suspend fun saveVoiceEdit() {
        val edit: TtsVoiceDefaultEdit = _state.value.voiceEdit ?: return
        val preview: PlatformDefaultBlastRadius = edit.preview ?: return
        if (!edit.canSave) return
        _state.value = _state.value.copy(voiceEdit = edit.copy(saving = true))
        val body = SetTtsVoiceDefaultRequest(voiceId = edit.voiceId, confirmedChannelsAffected = preview.channelsAffected)
        when (val result: ApiResult<TtsVoiceDefault> = api.setTtsVoiceDefault(body)) {
            is ApiResult.Ok -> {
                val saved: TtsVoiceDefault = result.value
                _state.value = _state.value.copy(
                    voiceDefault = saved,
                    voiceCandidates = _state.value.voiceCandidates.map { it.copy(isDefault = it.voiceId == saved.voiceId) },
                    voiceEdit = null,
                )
                feedback.success(Res.string.platform_defaults_saved, preview.channelsAffected)
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(voiceEdit = edit.copy(saving = false, preview = null))
                if (result.error.code == STALE_CODE) {
                    feedback.error(Res.string.platform_defaults_stale)
                    previewVoiceEdit()
                } else {
                    feedback.error(Res.string.platform_defaults_error, result.error.message)
                }
            }
        }
    }

    // Applies [change] only while the editor still shows the same pick — a late preview for a pick the operator
    // already moved away from must never overwrite the newer one.
    private fun updateEdit(actionKey: String, level: Int?, change: (ActionDefaultEdit) -> ActionDefaultEdit) {
        val current: ActionDefaultEdit = _state.value.actionEdit ?: return
        if (current.actionKey != actionKey || current.level != level) return
        _state.value = _state.value.copy(actionEdit = change(current))
    }

    private companion object {
        const val STALE_CODE: String = "PREVIEW_STALE"
        const val NOT_FOUND_CODE: String = "NOT_FOUND"
    }
}

/** True when the action guards something the server treats as dangerous (Critical or ToS tier). */
val ActionDefault.isDangerous: Boolean
    get() = floorTier != ActionDangerTier.LOW
