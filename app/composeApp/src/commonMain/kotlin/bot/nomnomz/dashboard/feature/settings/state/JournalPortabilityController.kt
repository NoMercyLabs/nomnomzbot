// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.EventJournalImportSummary
import bot.nomnomz.dashboard.core.network.EventStoreApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The state-holder for the Settings page's Event Journal export/import section. It resolves the active channel,
// then drives two real round-trips against the backend:
//   • export — pull the channel's whole journal as JSONL bytes and hand them to the OS save dialog;
//   • import — pick a JSONL file from the OS, upload it (the backend appends idempotently), and surface the
//     import/skip/upcast counts.
// The screen renders [state]; it gates the import behind a ConfirmDialog (it mutates the journal). Both methods
// re-resolve the channel lazily and cache it so a second action reuses it.
class JournalPortabilityController(
    private val channelsApi: ChannelsApi,
    private val eventStoreApi: EventStoreApi,
    private val fileBridge: JournalFileIO,
) {
    private val _state: MutableStateFlow<JournalPortabilityState> =
        MutableStateFlow(JournalPortabilityState())

    /** The export/import section render state. */
    val state: StateFlow<JournalPortabilityState> = _state.asStateFlow()

    private var channelId: String? = null

    /**
     * Export the channel's journal: resolve the channel, fetch the JSONL bytes, then offer them to the OS save
     * dialog. A cancelled save returns the section to idle; a backend or empty result surfaces an error.
     */
    suspend fun export() {
        if (_state.value.busy) return
        _state.value = JournalPortabilityState(busy = true)

        val target: String =
            when (val resolved: ApiResult<String> = resolveChannel()) {
                is ApiResult.Failure -> {
                    _state.value = JournalPortabilityState(error = resolved.error.message)
                    return
                }
                is ApiResult.Ok -> resolved.value
            }

        when (val export: ApiResult<ByteArray> = eventStoreApi.exportJournal(target)) {
            is ApiResult.Failure ->
                _state.value = JournalPortabilityState(error = export.error.message)
            is ApiResult.Ok -> {
                val saved: Boolean =
                    fileBridge.saveFile(suggestedName = suggestedFileName(target), bytes = export.value)
                _state.value = JournalPortabilityState(exported = saved && export.value.isNotEmpty())
            }
        }
    }

    /**
     * Import a journal file: pick a JSONL file from the OS, then upload it. A cancelled pick returns to idle
     * without a call; an empty file is rejected before the upload. On success the import summary is surfaced.
     * The caller is responsible for confirming first (the import mutates the journal).
     */
    suspend fun import() {
        if (_state.value.busy) return
        _state.value = JournalPortabilityState(busy = true)

        val target: String =
            when (val resolved: ApiResult<String> = resolveChannel()) {
                is ApiResult.Failure -> {
                    _state.value = JournalPortabilityState(error = resolved.error.message)
                    return
                }
                is ApiResult.Ok -> resolved.value
            }

        val picked: PickedFile? = fileBridge.pickFile()
        if (picked == null) {
            // User cancelled the file picker — return to idle, no call made.
            _state.value = JournalPortabilityState()
            return
        }
        if (picked.bytes.isEmpty()) {
            _state.value = JournalPortabilityState(error = "The selected file is empty.")
            return
        }

        _state.value = JournalPortabilityState(busy = true)
        when (
            val result: ApiResult<EventJournalImportSummary> =
                eventStoreApi.importJournal(target, picked.name, picked.bytes)
        ) {
            is ApiResult.Failure ->
                _state.value = JournalPortabilityState(error = result.error.message)
            is ApiResult.Ok -> _state.value = JournalPortabilityState(imported = result.value)
        }
    }

    /** Clear a surfaced result/error back to idle (after the user has seen it). */
    fun dismiss() {
        _state.value = JournalPortabilityState()
    }

    private suspend fun resolveChannel(): ApiResult<String> {
        channelId?.let { return ApiResult.Ok(it) }
        return when (val channel: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Failure -> ApiResult.Failure(channel.error)
            is ApiResult.Ok -> {
                channelId = channel.value.id
                ApiResult.Ok(channel.value.id)
            }
        }
    }

    private fun suggestedFileName(channelId: String): String = "event-journal-$channelId.jsonl"
}

/**
 * The export/import section render state: [busy] while an action is in flight, [exported] true after a save
 * completed, [imported] holding the summary after a successful import, and [error] for any failure. At most one
 * of exported/imported/error is set at a time; an idle state is all-default.
 */
data class JournalPortabilityState(
    val busy: Boolean = false,
    val exported: Boolean = false,
    val imported: EventJournalImportSummary? = null,
    val error: String? = null,
)
