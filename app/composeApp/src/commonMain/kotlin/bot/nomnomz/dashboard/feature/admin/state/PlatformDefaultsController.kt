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
import bot.nomnomz.dashboard.core.network.EventResponseDefault
import bot.nomnomz.dashboard.core.network.EventResponseDefaultChange
import bot.nomnomz.dashboard.core.network.PlatformDefaultBlastRadius
import bot.nomnomz.dashboard.core.network.PlatformDefaultsApi
import bot.nomnomz.dashboard.core.network.SetActionDefaultRequest
import bot.nomnomz.dashboard.core.network.SetEventResponseDefaultRequest
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
 * The draft of one event-response default edit. Any change to [isEnabled] or [message] drops [preview], so the
 * save can only ever carry a count the operator saw for exactly the values being saved.
 */
data class EventResponseDefaultEdit(
    val eventType: String,
    val isEnabled: Boolean,
    val message: String,
    val preview: PlatformDefaultBlastRadius? = null,
    val previewing: Boolean = false,
    val saving: Boolean = false,
) {
    val change: EventResponseDefaultChange
        get() = EventResponseDefaultChange(isEnabled = isEnabled, message = message.trim().ifEmpty { null })

    /** An enabled default must say something; the save arms only once the count for these values is shown. */
    val canSave: Boolean
        get() = preview != null && !saving && (!isEnabled || message.isNotBlank())
}

data class PlatformDefaultsState(
    val actionDefaults: List<ActionDefault> = emptyList(),
    val actionsLoaded: Boolean = false,
    val actionFilter: String = "",
    val actionEdit: ActionDefaultEdit? = null,
    val eventDefaults: List<EventResponseDefault> = emptyList(),
    val eventsLoaded: Boolean = false,
    val eventEdit: EventResponseDefaultEdit? = null,
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
        when (val result: ApiResult<List<ActionDefault>> = api.actionDefaults()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(actionDefaults = result.value, actionsLoaded = true)
            is ApiResult.Failure -> feedback.error(Res.string.platform_defaults_error, result.error.message)
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
        when (val result: ApiResult<List<EventResponseDefault>> = api.eventResponseDefaults()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(eventDefaults = result.value, eventsLoaded = true)
            is ApiResult.Failure -> feedback.error(Res.string.platform_defaults_error, result.error.message)
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
            ),
        )
    }

    fun editEventEnabled(enabled: Boolean) {
        val edit: EventResponseDefaultEdit = _state.value.eventEdit ?: return
        _state.value = _state.value.copy(eventEdit = edit.copy(isEnabled = enabled, preview = null))
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

    // Applies [change] only while the editor still shows the same pick — a late preview for a pick the operator
    // already moved away from must never overwrite the newer one.
    private fun updateEdit(actionKey: String, level: Int?, change: (ActionDefaultEdit) -> ActionDefaultEdit) {
        val current: ActionDefaultEdit = _state.value.actionEdit ?: return
        if (current.actionKey != actionKey || current.level != level) return
        _state.value = _state.value.copy(actionEdit = change(current))
    }

    private companion object {
        const val STALE_CODE: String = "PREVIEW_STALE"
    }
}

/** True when the action guards something the server treats as dangerous (Critical or ToS tier). */
val ActionDefault.isDangerous: Boolean
    get() = floorTier != ActionDangerTier.LOW
