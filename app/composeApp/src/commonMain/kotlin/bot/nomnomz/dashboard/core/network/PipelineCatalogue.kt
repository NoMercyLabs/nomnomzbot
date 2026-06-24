// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

// The closed catalogue of pipeline action + condition block types the editor offers, with the editable
// parameters each accepts. This is a deterministic reflection of the backend's ICommandAction /
// ICommandCondition implementations (NomNomzBot.Infrastructure/**/PipelineActions + Platform/Pipeline) — the
// `type` discriminators and the parameter keys/kinds come straight from those classes' `GetString`/`GetInt`
// reads, so the editor can only build chains the PipelineEngine actually runs. Adding a backend action means
// adding its descriptor here; the editor then surfaces it with no further wiring.
//
// Only the core + commonly-wired blocks are catalogued for this slice's editor (the chat / moderation / media
// / flow set); the side-effecting economy/quotes/code blocks register on the backend the same way and slot in
// here as descriptors when their pages need them.

/** Whether a parameter field is free text or a number — drives the input control and the JSON encoding. */
enum class FieldKind {
    Text,
    Number,
}

/**
 * One editable parameter on a block: the backend [key] (the exact param name the action/condition reads), the
 * i18n [labelKey] suffix for its field label, whether it is [required] for a valid block, its [kind], and an
 * optional [placeholder] hint key. The screen resolves `pipelines_field_<labelKey>` for the label.
 */
data class BlockField(
    val key: String,
    val labelKey: String,
    val required: Boolean,
    val kind: FieldKind = FieldKind.Text,
)

/** What a block is — an action (does something) or a condition (gates the step). */
enum class BlockRole {
    Action,
    Condition,
}

/**
 * A catalogued block type: its backend [type] discriminator, its [role], the i18n [labelKey] suffix for its
 * display name, and its editable [fields]. The screen resolves `pipelines_block_<labelKey>` for the name.
 */
data class BlockType(
    val type: String,
    val role: BlockRole,
    val labelKey: String,
    val fields: List<BlockField> = emptyList(),
)

// The catalogue. Every descriptor below is grounded in a real backend class:
//   send_message        SendMessageAction      — "message"
//   send_reply          SendReplyAction        — "message"
//   timeout             TimeoutAction          — "user_id"?, "duration" (int, def 60), "reason"?
//   ban                 BanAction              — "user_id"?, "reason"?
//   delete_message      DeleteMessageAction    — "message_id"? (defaults to triggering message)
//   shoutout            ShoutoutAction         — "user_id", "cooldown_minutes" (int), "global_cooldown_minutes" (int)
//   set_variable        SetVariableAction      — "name", "value"
//   wait                WaitAction             — "seconds" (int), "milliseconds" (int)
//   stop                StopAction             — (no params)
//   song_request        SongRequestAction      — "query"
//   song_skip           SongSkipAction         — (no params)
//   song_volume         SongVolumeAction       — "volume" (int 0-100)
//   user_role (cond)    UserRoleCondition      — "min_role"
//   random (cond)       RandomCondition        — "percent" (int 0-100)
object PipelineCatalogue {

    /** Every catalogued action block, in a sensible authoring order (chat first, then mod, media, flow). */
    val actions: List<BlockType> =
        listOf(
            BlockType(
                type = "send_message",
                role = BlockRole.Action,
                labelKey = "send_message",
                fields = listOf(BlockField("message", "message", required = true)),
            ),
            BlockType(
                type = "send_reply",
                role = BlockRole.Action,
                labelKey = "send_reply",
                fields = listOf(BlockField("message", "message", required = true)),
            ),
            BlockType(
                type = "timeout",
                role = BlockRole.Action,
                labelKey = "timeout",
                fields =
                    listOf(
                        BlockField("user_id", "user_id", required = false),
                        BlockField("duration", "duration_seconds", required = true, kind = FieldKind.Number),
                        BlockField("reason", "reason", required = false),
                    ),
            ),
            BlockType(
                type = "ban",
                role = BlockRole.Action,
                labelKey = "ban",
                fields =
                    listOf(
                        BlockField("user_id", "user_id", required = false),
                        BlockField("reason", "reason", required = false),
                    ),
            ),
            BlockType(
                type = "delete_message",
                role = BlockRole.Action,
                labelKey = "delete_message",
                fields = listOf(BlockField("message_id", "message_id", required = false)),
            ),
            BlockType(
                type = "shoutout",
                role = BlockRole.Action,
                labelKey = "shoutout",
                fields =
                    listOf(
                        BlockField("user_id", "user_id", required = true),
                        BlockField("cooldown_minutes", "cooldown_minutes", required = false, kind = FieldKind.Number),
                    ),
            ),
            BlockType(
                type = "song_request",
                role = BlockRole.Action,
                labelKey = "song_request",
                fields = listOf(BlockField("query", "query", required = true)),
            ),
            BlockType(
                type = "song_skip",
                role = BlockRole.Action,
                labelKey = "song_skip",
            ),
            BlockType(
                type = "song_volume",
                role = BlockRole.Action,
                labelKey = "song_volume",
                fields = listOf(BlockField("volume", "volume", required = true, kind = FieldKind.Number)),
            ),
            BlockType(
                type = "set_variable",
                role = BlockRole.Action,
                labelKey = "set_variable",
                fields =
                    listOf(
                        BlockField("name", "variable_name", required = true),
                        BlockField("value", "variable_value", required = false),
                    ),
            ),
            BlockType(
                type = "wait",
                role = BlockRole.Action,
                labelKey = "wait",
                fields = listOf(BlockField("seconds", "wait_seconds", required = true, kind = FieldKind.Number)),
            ),
            BlockType(
                type = "stop",
                role = BlockRole.Action,
                labelKey = "stop",
            ),
        )

    /** Every catalogued condition block. A step has at most one condition gating its action. */
    val conditions: List<BlockType> =
        listOf(
            BlockType(
                type = "user_role",
                role = BlockRole.Condition,
                labelKey = "user_role",
                fields = listOf(BlockField("min_role", "min_role", required = true)),
            ),
            BlockType(
                type = "random",
                role = BlockRole.Condition,
                labelKey = "random",
                fields = listOf(BlockField("percent", "percent", required = true, kind = FieldKind.Number)),
            ),
        )

    /** Look up an action descriptor by its backend type, or null when unknown (e.g. a server-only block). */
    fun action(type: String): BlockType? = actions.firstOrNull { it.type == type }

    /** Look up a condition descriptor by its backend type, or null when unknown. */
    fun condition(type: String): BlockType? = conditions.firstOrNull { it.type == type }

    /** Look up a single field descriptor on any catalogued block — used by the JSON encoder to type a param. */
    fun fieldFor(nodeType: String, key: String): BlockField? {
        val block: BlockType? = action(nodeType) ?: condition(nodeType)
        return block?.fields?.firstOrNull { it.key == key }
    }
}

/** The canonical role-floor options for the `user_role` condition's `min_role` field (the backend ladder). */
val UserRoleOptions: List<String> =
    listOf("viewer", "subscriber", "vip", "moderator", "broadcaster")
