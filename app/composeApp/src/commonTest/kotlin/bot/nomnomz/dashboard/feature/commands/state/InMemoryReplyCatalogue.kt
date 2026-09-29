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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.BuiltinReply
import bot.nomnomz.dashboard.core.network.BuiltinReplyGroup
import bot.nomnomz.dashboard.core.network.BuiltinReplyVariable
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.LocalizedTextDto
import bot.nomnomz.dashboard.core.network.ModeratedChannel

/**
 * A reply catalogue that behaves like the backend's (commands-pipelines.md §11): a slot's effective text is the
 * channel's own text when set, else its default; a template naming a variable the slot does not have is refused
 * with a validation error. Records every write so a test can assert what reached the "server".
 */
internal class InMemoryReplyCatalogue : BuiltinsApi {
    val writes: MutableList<String> = mutableListOf()
    private val own: MutableMap<Pair<String, String>, String> = mutableMapOf()

    private val defaults: List<Triple<String, List<String>, List<Pair<String, String>>>> =
        listOf(
            Triple("sr", listOf("sr"), listOf("added" to "Added {track.name} to the queue.", "duplicate" to "Already queued by {requested.by}.")),
            Triple("system", emptyList(), listOf("permissiondenied" to "You don't have permission to use that command.")),
        )

    override suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>> = ApiResult.Ok(emptyList())

    override suspend fun setEnabled(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun setSpeakWithTts(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun replies(channelId: String): ApiResult<List<BuiltinReplyGroup>> =
        ApiResult.Ok(
            defaults.map { (key, commands, slots) ->
                BuiltinReplyGroup(key, commands, slots.map { (slot, _) -> reply(key, slot) })
            },
        )

    override suspend fun setReply(
        channelId: String,
        builtinKey: String,
        slot: String,
        template: String,
    ): ApiResult<BuiltinReply> {
        val allowed: Set<String> = variablesOf(builtinKey, slot).map { it.name }.toSet()
        val unknown: String? =
            Regex("\\{([^{}]+)\\}").findAll(template).map { it.groupValues[1] }.firstOrNull { it !in allowed }
        if (unknown != null) {
            return ApiResult.Failure(ApiError(status = 400, code = "VALIDATION_FAILED", message ="'{$unknown}' is not a recognized template helper"))
        }
        writes += "set $builtinKey/$slot=$template"
        own[builtinKey to slot] = template
        return ApiResult.Ok(reply(builtinKey, slot))
    }

    override suspend fun resetReply(channelId: String, builtinKey: String, slot: String): ApiResult<BuiltinReply> {
        writes += "reset $builtinKey/$slot"
        own.remove(builtinKey to slot)
        return ApiResult.Ok(reply(builtinKey, slot))
    }

    private fun reply(key: String, slot: String): BuiltinReply {
        val default: String = defaults.first { it.first == key }.third.first { it.first == slot }.second
        val mine: String? = own[key to slot]
        return BuiltinReply(
            builtinKey = key,
            slot = slot,
            label = LocalizedTextDto("builtin.reply.$key.$slot.label"),
            description = LocalizedTextDto("builtin.reply.$key.$slot.description"),
            effectiveTemplate = mine ?: default,
            source = if (mine != null) "channel" else "tone",
            defaultTemplate = default,
            toneVariations = listOf(default),
            variables = variablesOf(key, slot),
            isOverridden = mine != null,
        )
    }

    private fun variablesOf(key: String, slot: String): List<BuiltinReplyVariable> =
        when (key to slot) {
            "sr" to "added" -> listOf(BuiltinReplyVariable("track.name", LocalizedTextDto("builtin.reply.var.track.name"), "Never Gonna Give You Up"))
            "sr" to "duplicate" -> listOf(BuiltinReplyVariable("requested.by", LocalizedTextDto("builtin.reply.var.requested.by"), "StreamFan42"))
            else -> emptyList()
        }
}

/** A channels API whose primary channel is always `ch1`. */
internal class PrimaryChannelOnly : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))
    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}
