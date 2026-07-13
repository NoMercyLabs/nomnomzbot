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
// All backend actions and conditions are catalogued here so the editor surfaces the complete capability.

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
//   send_message              SendMessageAction              — "message"
//   send_reply                SendReplyAction                — "message"
//   timeout                   TimeoutAction                  — "user_id"?, "duration" (int), "reason"?
//   ban                       BanAction                      — "user_id"?, "reason"?
//   delete_message            DeleteMessageAction            — "message_id"? (defaults to triggering message)
//   shoutout                  ShoutoutAction                 — "user_id", "cooldown_minutes" (int)?
//   set_variable              SetVariableAction              — "name", "value"
//   wait                      WaitAction                     — "seconds" (int)
//   stop                      StopAction                     — (no params)
//   song_request              SongRequestAction              — "query"
//   song_skip                 SongSkipAction                 — (no params)
//   song_volume               SongVolumeAction               — "volume" (int 0-100)
//   song_current              SongCurrentAction              — (no params)
//   song_queue                SongQueueAction                — "max" (int, default 5)?
//   play_sound                PlaySoundAction                — "clip", "volume" (int)?, "wait_for_finish"?, "handle"?
//   grant_currency            GrantCurrencyAction            — "amount" (int), "reason"?
//   deduct_currency           DeductCurrencyAction           — "amount" (int), "reason"?
//   check_balance             CheckBalanceAction             — "set_var"?, "min" (int)?
//   play_game                 PlayGameAction                 — "game_type", "bet" (int)
//   jar_contribute            JarContributeAction            — "jar_id", "amount" (int)
//   run_code                  RunCodeAction                  — "code_script_id"
//   send_discord_notification SendDiscordNotificationAction  — "trigger_type", "dedupe_key"?
//   post_quote                PostQuoteAction                — "number" (int)?
//   require_tier              RequireTierAction              — "min_tier", "denied_message"?
//   user_role (cond)          UserRoleCondition              — "min_role"
//   random (cond)             RandomCondition                — "percent" (int 0-100)
object PipelineCatalogue {

    /** Every catalogued action block, in a sensible authoring order (chat → mod → music → economy → flow). */
    val actions: List<BlockType> =
        listOf(
            // ── Chat ─────────────────────────────────────────────────────────
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
            // ── Moderation ───────────────────────────────────────────────────
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
            // ── Music ────────────────────────────────────────────────────────
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
                type = "song_current",
                role = BlockRole.Action,
                labelKey = "song_current",
            ),
            BlockType(
                type = "song_queue",
                role = BlockRole.Action,
                labelKey = "song_queue",
                fields = listOf(BlockField("max", "song_queue_max", required = false, kind = FieldKind.Number)),
            ),
            // ── Sound / overlay ──────────────────────────────────────────────
            BlockType(
                type = "play_sound",
                role = BlockRole.Action,
                labelKey = "play_sound",
                fields =
                    listOf(
                        // The sound clip to play — its slug/name or id (SoundClip.name is the value the action reads).
                        BlockField("clip", "clip", required = true),
                        BlockField("volume", "volume", required = false, kind = FieldKind.Number),
                        BlockField("wait_for_finish", "wait_for_finish", required = false),
                        BlockField("handle", "handle", required = false),
                    ),
            ),
            // ── Economy ──────────────────────────────────────────────────────
            BlockType(
                type = "grant_currency",
                role = BlockRole.Action,
                labelKey = "grant_currency",
                fields =
                    listOf(
                        BlockField("amount", "amount", required = true, kind = FieldKind.Number),
                        BlockField("reason", "reason", required = false),
                    ),
            ),
            BlockType(
                type = "deduct_currency",
                role = BlockRole.Action,
                labelKey = "deduct_currency",
                fields =
                    listOf(
                        BlockField("amount", "amount", required = true, kind = FieldKind.Number),
                        BlockField("reason", "reason", required = false),
                    ),
            ),
            BlockType(
                type = "check_balance",
                role = BlockRole.Action,
                labelKey = "check_balance",
                fields =
                    listOf(
                        BlockField("set_var", "set_var", required = false),
                        BlockField("min", "min_balance", required = false, kind = FieldKind.Number),
                    ),
            ),
            BlockType(
                type = "play_game",
                role = BlockRole.Action,
                labelKey = "play_game",
                fields =
                    listOf(
                        BlockField("game_type", "game_type", required = true),
                        BlockField("bet", "bet_amount", required = true, kind = FieldKind.Number),
                    ),
            ),
            BlockType(
                type = "jar_contribute",
                role = BlockRole.Action,
                labelKey = "jar_contribute",
                fields =
                    listOf(
                        BlockField("jar_id", "jar_id", required = true),
                        BlockField("amount", "amount", required = true, kind = FieldKind.Number),
                    ),
            ),
            // ── Content ──────────────────────────────────────────────────────
            BlockType(
                type = "post_quote",
                role = BlockRole.Action,
                labelKey = "post_quote",
                fields = listOf(BlockField("number", "quote_number", required = false, kind = FieldKind.Number)),
            ),
            BlockType(
                type = "send_discord_notification",
                role = BlockRole.Action,
                labelKey = "send_discord_notification",
                fields =
                    listOf(
                        BlockField("trigger_type", "trigger_type", required = true),
                        BlockField("dedupe_key", "dedupe_key", required = false),
                    ),
            ),
            // ── Gating ───────────────────────────────────────────────────────
            BlockType(
                type = "require_tier",
                role = BlockRole.Action,
                labelKey = "require_tier",
                fields =
                    listOf(
                        BlockField("min_tier", "min_tier", required = true),
                        BlockField("denied_message", "denied_message", required = false),
                    ),
            ),
            // ── Code ─────────────────────────────────────────────────────────
            BlockType(
                type = "run_code",
                role = BlockRole.Action,
                labelKey = "run_code",
                fields = listOf(BlockField("code_script_id", "code_script_id", required = true)),
            ),
            // ── Flow ─────────────────────────────────────────────────────────
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
