// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformtemplates.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PlatformTemplateUpdate
import bot.nomnomz.dashboard.core.network.PlatformTemplateUpdatesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.template_update_done
import nomnomzbot.composeapp.generated.resources.template_update_failed

/**
 * "Update available" for one page's template-installed rows (timers, pick lists, …). [refresh] asks the backend
 * which of this channel's copies of [kind] sit behind their template; [request] takes one update. A copy the
 * channel never changed updates at once; a changed copy opens [pendingConfirm] first, because the update
 * replaces those changes. After an update [onUpdated] reloads the host page, and that reload calls [refresh].
 */
class TemplateUpdatesController(
    private val kind: String,
    private val api: PlatformTemplateUpdatesApi,
    private val channelId: suspend () -> ApiResult<String>,
    private val feedback: Feedback,
    private val onUpdated: suspend () -> Unit,
) {
    private val _updates: MutableStateFlow<Map<String, PlatformTemplateUpdate>> = MutableStateFlow(emptyMap())

    /** The rows that have an update, keyed by row id. Empty when none do or when the check failed. */
    val updates: StateFlow<Map<String, PlatformTemplateUpdate>> = _updates.asStateFlow()

    private val _pendingConfirm: MutableStateFlow<TemplateUpdateConfirm?> = MutableStateFlow(null)

    /** The changed copy waiting for "replace my changes", or null when no confirm is open. */
    val pendingConfirm: StateFlow<TemplateUpdateConfirm?> = _pendingConfirm.asStateFlow()

    private val _applying: MutableStateFlow<String?> = MutableStateFlow(null)

    /** The row id whose update is in flight, so its action can show as busy. */
    val applying: StateFlow<String?> = _applying.asStateFlow()

    /** Reloads which rows have an update. A failed check shows no badges rather than stale ones. */
    suspend fun refresh() {
        val channel: String =
            when (val result: ApiResult<String> = channelId()) {
                is ApiResult.Failure -> {
                    _updates.value = emptyMap()
                    return
                }
                is ApiResult.Ok -> result.value
            }
        _updates.value =
            when (val result: ApiResult<List<PlatformTemplateUpdate>> = api.list(channel, kind)) {
                is ApiResult.Ok -> result.value.associateBy { it.rowId }
                is ApiResult.Failure -> emptyMap()
            }
    }

    /** Takes the update for [rowId]: at once for an unchanged copy, through [pendingConfirm] for a changed one. */
    suspend fun request(rowId: String) {
        val update: PlatformTemplateUpdate = _updates.value[rowId] ?: return
        if (update.editedSinceInstall) {
            _pendingConfirm.value = TemplateUpdateConfirm(update)
            return
        }
        when (val result: ApiResult<PlatformTemplateUpdate> = apply(update)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure ->
                feedback.error(Res.string.template_update_failed, update.displayName, result.error.message)
        }
    }

    /** Applies the confirmed update. True on success; on failure the confirm stays open with the reason. */
    suspend fun confirm(): Boolean {
        val pending: TemplateUpdateConfirm = _pendingConfirm.value ?: return false
        _pendingConfirm.value = pending.copy(applying = true, error = null)
        return when (val result: ApiResult<PlatformTemplateUpdate> = apply(pending.update)) {
            is ApiResult.Ok -> {
                _pendingConfirm.value = null
                true
            }
            is ApiResult.Failure -> {
                _pendingConfirm.value = pending.copy(applying = false, error = result.error.message)
                false
            }
        }
    }

    /** Closes the confirm without updating. */
    fun dismiss() {
        _pendingConfirm.value = null
    }

    private suspend fun apply(update: PlatformTemplateUpdate): ApiResult<PlatformTemplateUpdate> {
        _applying.value = update.rowId
        val result: ApiResult<PlatformTemplateUpdate> =
            when (val channel: ApiResult<String> = channelId()) {
                is ApiResult.Failure -> channel
                is ApiResult.Ok -> api.apply(channel.value, update.definitionId, update.rowId)
            }
        _applying.value = null
        if (result is ApiResult.Ok) {
            feedback.success(Res.string.template_update_done, update.displayName)
            onUpdated()
        }
        return result
    }
}

/** One changed copy waiting for confirm. [error] is the last failed attempt's reason. */
data class TemplateUpdateConfirm(
    val update: PlatformTemplateUpdate,
    val applying: Boolean = false,
    val error: String? = null,
)
