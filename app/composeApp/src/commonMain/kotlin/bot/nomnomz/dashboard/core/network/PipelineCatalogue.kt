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

// The pipeline builder's block palette is sourced from the BACKEND — `GET pipelines/actions` returns every
// registered `ICommandAction` (with its category + description) and `ICommandCondition`, so the list of blocks
// the editor offers can never drift out of sync with what the engine actually runs (see [PipelinesController]
// and [PipelineCatalogueRemote]). What lives HERE is the local FIELD-HINT layer: the editable-parameter shapes
// (key + kind + label) the backend contract does not carry. When a backend block has a matching hint below the
// editor renders typed fields for it; when it does not, the editor falls back to a generic key/value param
// editor so EVERY discovered block stays configurable. Adding rich fields for a new backend action means adding
// its hint here — but the block already appears in the palette from the backend regardless.
//
// The `type` discriminators and the parameter keys/kinds mirror the backend classes' `GetString`/`GetInt`
// reads, so a typed field the editor writes is one the action reads.

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
//   song_pause                SongPauseAction                — (no params)
//   song_resume               SongResumeAction               — (no params)
//   song_previous             SongPreviousAction             — (no params)
//   playlist_add              PlaylistAddAction              — "playlist_id", "track_uri"?
//   song_wrong                SongWrongAction                — (no params)
//   song_ban                  SongBanAction                  — "reason"?
//   play_sound                PlaySoundAction                — "clip", "volume" (int)?, "wait_for_finish"?, "handle"?
//   play_tts                  PlayTtsAction                  — "text" (template, required), "voice" (voice id)?
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
            BlockType(
                type = "song_pause",
                role = BlockRole.Action,
                labelKey = "song_pause",
            ),
            BlockType(
                type = "song_resume",
                role = BlockRole.Action,
                labelKey = "song_resume",
            ),
            BlockType(
                type = "song_previous",
                role = BlockRole.Action,
                labelKey = "song_previous",
            ),
            BlockType(
                type = "playlist_add",
                role = BlockRole.Action,
                labelKey = "playlist_add",
                fields =
                    listOf(
                        BlockField("playlist_id", "playlist_id", required = true),
                        // Optional track URI override; empty adds the currently playing track.
                        BlockField("track_uri", "track_uri", required = false),
                    ),
            ),
            BlockType(
                type = "song_wrong",
                role = BlockRole.Action,
                labelKey = "song_wrong",
            ),
            BlockType(
                type = "song_ban",
                role = BlockRole.Action,
                labelKey = "song_ban",
                fields = listOf(BlockField("reason", "reason", required = false)),
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
            BlockType(
                type = "play_tts",
                role = BlockRole.Action,
                labelKey = "play_tts",
                fields =
                    listOf(
                        // The text to read aloud — a template string, e.g. {{args}}. The action also accepts a
                        // "message" alias, but "text" is the canonical key the editor writes.
                        BlockField("text", "text", required = true),
                        // Optional voice id override; empty falls back to the viewer's voice → the channel default.
                        BlockField("voice", "voice", required = false),
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
            // ── Integrations / overlay / live ─────────────────────────────────
            // send_webhook: POSTs the pipeline's current variables to one of the channel's outbound webhook
            // endpoints (the `endpoint` field is rendered as an outbound-endpoint picker — see the screen).
            BlockType(
                type = "send_webhook",
                role = BlockRole.Action,
                labelKey = "send_webhook",
                fields =
                    listOf(
                        BlockField("endpoint", "endpoint", required = true),
                        BlockField("event_type", "event_type", required = false),
                    ),
            ),
            // pick_from_list: draws one random line from a channel pick-list into a pipeline variable (the
            // `list` field is rendered as a pick-list picker — see the screen).
            BlockType(
                type = "pick_from_list",
                role = BlockRole.Action,
                labelKey = "pick_from_list",
                fields =
                    listOf(
                        BlockField("list", "list", required = true),
                        BlockField("variable", "pick_variable", required = false),
                    ),
            ),
            // stop_sound: stops a playing sound clip; an empty handle stops all.
            BlockType(
                type = "stop_sound",
                role = BlockRole.Action,
                labelKey = "stop_sound",
                fields = listOf(BlockField("handle", "handle", required = false)),
            ),
            // start_live_game / cancel_live_game: the live overlay-game rounds (drop/raffle/…).
            BlockType(
                type = "start_live_game",
                role = BlockRole.Action,
                labelKey = "start_live_game",
                fields = listOf(BlockField("game_type", "game_type", required = true)),
            ),
            BlockType(
                type = "cancel_live_game",
                role = BlockRole.Action,
                labelKey = "cancel_live_game",
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
            // var_compare: gate the step on a pipeline variable versus an expected value (e.g. {{count}} >= 5).
            BlockType(
                type = "var_compare",
                role = BlockRole.Condition,
                labelKey = "var_compare",
                fields =
                    listOf(
                        BlockField("left", "compare_left", required = true),
                        BlockField("operator", "compare_operator", required = true),
                        BlockField("right", "compare_right", required = false),
                    ),
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

    /**
     * Merge the backend-sourced [remote] palette (the authoritative block LIST + category + description) with the
     * local field HINTS above, producing the palette the editor renders. Every backend block appears — one with a
     * matching hint gets typed fields, one without gets the generic key/value editor ([PaletteBlock.hasHints] =
     * false). This is the keystone: the palette can never drift because its membership comes from the backend.
     */
    fun buildPalette(remote: PipelineCatalogueRemote): RuntimePalette =
        RuntimePalette(
            actions = remote.actions.map { descriptor ->
                paletteBlock(
                    type = descriptor.type,
                    role = BlockRole.Action,
                    category = descriptor.category.ifBlank { GeneralCategory },
                    description = descriptor.description,
                    hint = action(descriptor.type),
                )
            },
            conditions = remote.conditions.map { descriptor ->
                paletteBlock(
                    type = descriptor.type,
                    role = BlockRole.Condition,
                    category = ConditionCategory,
                    description = "",
                    hint = condition(descriptor.type),
                )
            },
        )

    /**
     * The offline fallback palette — the locally-known hint blocks only. Used when the backend catalogue fetch
     * fails so the editor still opens with the core blocks rather than an empty palette.
     */
    fun fallbackPalette(): RuntimePalette =
        RuntimePalette(
            actions = actions.map { paletteBlock(it.type, BlockRole.Action, GeneralCategory, "", it) },
            conditions = conditions.map { paletteBlock(it.type, BlockRole.Condition, ConditionCategory, "", it) },
        )

    private fun paletteBlock(
        type: String,
        role: BlockRole,
        category: String,
        description: String,
        hint: BlockType?,
    ): PaletteBlock =
        PaletteBlock(
            type = type,
            role = role,
            category = category,
            description = description,
            labelKey = hint?.labelKey,
            fields = hint?.fields.orEmpty(),
            hasHints = hint != null,
        )

    const val GeneralCategory: String = "general"
    const val ConditionCategory: String = "condition"
}

/**
 * One palette entry as the builder renders it: the backend-sourced identity ([type] / [category] /
 * [description]) merged with local hints. [labelKey] is the i18n label suffix when the type is known locally
 * (null → the screen humanizes the raw [type]); [fields] are the typed parameter hints (empty when [hasHints]
 * is false, in which case the editor shows a generic key/value param editor).
 */
data class PaletteBlock(
    val type: String,
    val role: BlockRole,
    val category: String,
    val description: String,
    val labelKey: String?,
    val fields: List<BlockField>,
    val hasHints: Boolean,
)

/**
 * The builder's live palette: every action block (grouped by [actionsByCategory] in the backend's category
 * order) and every condition block. Built by [PipelineCatalogue.buildPalette] from the backend catalogue.
 */
data class RuntimePalette(
    val actions: List<PaletteBlock>,
    val conditions: List<PaletteBlock>,
) {
    fun action(type: String): PaletteBlock? = actions.firstOrNull { it.type == type }

    fun condition(type: String): PaletteBlock? = conditions.firstOrNull { it.type == type }

    /** Action blocks grouped by category — categories and members each in first-seen (backend) order. */
    val actionsByCategory: List<Pair<String, List<PaletteBlock>>>
        get() = actions.groupBy { it.category }.toList()

    val isEmpty: Boolean
        get() = actions.isEmpty()
}

/** The canonical role-floor options for the `user_role` condition's `min_role` field (the backend ladder). */
val UserRoleOptions: List<String> =
    listOf("viewer", "subscriber", "vip", "moderator", "broadcaster")
