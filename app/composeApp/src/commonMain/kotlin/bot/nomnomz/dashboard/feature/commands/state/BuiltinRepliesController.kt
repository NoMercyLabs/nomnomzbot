// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BuiltinReply
import bot.nomnomz.dashboard.core.network.BuiltinReplyGroup
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The built-in reply editor's state-holder (commands-pipelines.md §11). Lists every reply slot of the open
// reply group as the backend resolves it for this channel, and drives the per-slot edit, save and reset. Every
// write replaces the one slot with the backend's answer, so the editor always shows the backend's truth.
class BuiltinRepliesController(
    private val channelsApi: ChannelsApi,
    private val builtinsApi: BuiltinsApi,
) {
    private val _state: MutableStateFlow<BuiltinRepliesState> = MutableStateFlow(BuiltinRepliesState())

    /** The editor render state. */
    val state: StateFlow<BuiltinRepliesState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Opens the editor on [replyGroup] and loads the channel's reply catalogue. */
    suspend fun open(replyGroup: String) {
        _state.value = BuiltinRepliesState(openGroup = replyGroup, loading = true)
        load()
    }

    /** Closes the editor and drops any unsaved edit. */
    fun close() {
        _state.value = BuiltinRepliesState()
    }

    /** Starts editing one slot, pre-filled with the text the bot sends now. */
    fun startEdit(reply: BuiltinReply) {
        _state.value = _state.value.copy(editing = ReplyEdit(reply.builtinKey, reply.slot, reply.effectiveTemplate))
    }

    fun cancelEdit() {
        _state.value = _state.value.copy(editing = null)
    }

    fun editTemplate(template: String) {
        val edit: ReplyEdit = _state.value.editing ?: return
        _state.value = _state.value.copy(editing = edit.copy(template = template, error = null))
    }

    /** Adds `{name}` to the end of the text being edited — the variable chips' action. */
    fun insertVariable(name: String) {
        val edit: ReplyEdit = _state.value.editing ?: return
        val separator: String = if (edit.template.isEmpty() || edit.template.endsWith(" ")) "" else " "
        editTemplate(edit.template + separator + "{" + name + "}")
    }

    /** Saves the edited slot. A validation refusal stays in the editor with the backend's message. */
    suspend fun save() {
        val edit: ReplyEdit = _state.value.editing ?: return
        val channel: String = channelId ?: return
        _state.value = _state.value.copy(editing = edit.copy(saving = true, error = null))
        when (val result: ApiResult<BuiltinReply> =
            builtinsApi.setReply(channel, edit.builtinKey, edit.slot, edit.template)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(groups = replace(result.value), editing = null)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(editing = edit.copy(saving = false, error = result.error.message))
        }
    }

    /** Puts one slot back on its default. */
    suspend fun reset(reply: BuiltinReply): ApiResult<Unit> {
        val channel: String = channelId ?: return ApiResult.Ok(Unit)
        return when (val result: ApiResult<BuiltinReply> = builtinsApi.resetReply(channel, reply.builtinKey, reply.slot)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(groups = replace(result.value), editing = null)
                ApiResult.Ok(Unit)
            }
            // The confirm dialog shows the reason inline, so the page behind it gets no second copy.
            is ApiResult.Failure -> ApiResult.Failure(result.error)
        }
    }

    private suspend fun load() {
        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = _state.value.copy(loading = false, error = result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id
        _state.value =
            when (val result: ApiResult<List<BuiltinReplyGroup>> = builtinsApi.replies(channel.id)) {
                is ApiResult.Ok -> _state.value.copy(loading = false, groups = result.value)
                is ApiResult.Failure -> _state.value.copy(loading = false, error = result.error.message)
            }
    }

    private fun replace(updated: BuiltinReply): List<BuiltinReplyGroup> =
        _state.value.groups.map { group ->
            if (group.builtinKey != updated.builtinKey) group
            else group.copy(replies = group.replies.map { if (it.slot == updated.slot) updated else it })
        }
}

/**
 * The reply editor's state. [openGroup] is the reply group shown (null = editor closed); the reserved key
 * [BOT_REPLIES_GROUP] shows every group no command owns (the bot's own lines).
 */
data class BuiltinRepliesState(
    val openGroup: String? = null,
    val loading: Boolean = false,
    val error: String? = null,
    val groups: List<BuiltinReplyGroup> = emptyList(),
    val editing: ReplyEdit? = null,
) {
    /** The groups the open editor lists. */
    val visibleGroups: List<BuiltinReplyGroup>
        get() =
            when (openGroup) {
                null -> emptyList()
                BOT_REPLIES_GROUP -> groups.filter { it.commandKeys.isEmpty() }
                else -> groups.filter { it.builtinKey == openGroup }
            }
}

/** One slot being edited. [error] is the backend's refusal (e.g. an unknown variable), shown under the field. */
data class ReplyEdit(
    val builtinKey: String,
    val slot: String,
    val template: String,
    val saving: Boolean = false,
    val error: String? = null,
)

/** The editor key for "the bot's own lines" — every reply group no command owns. */
const val BOT_REPLIES_GROUP: String = "*bot"

/** Fills each `{variable}` with its sample so the editor can show what chat would read. */
fun previewReply(template: String, reply: BuiltinReply): String =
    reply.variables.fold(template) { text, variable -> text.replace("{" + variable.name + "}", variable.sampleValue) }
