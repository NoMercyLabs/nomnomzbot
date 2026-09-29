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

import kotlinx.serialization.Serializable

// The typed built-in-commands facade — the platform-defined commands (music !sr/!skip/!queue/!volume/!song)
// the Commands page shows in a separate section. The backend controls the catalogue; the dashboard can only
// toggle each builtin on/off per channel. Real data only — no fabricated rows.
//
// Backend routes (BuiltinsController):
//   GET   /api/v1/channels/{channelId}/builtins                → StatusResponseDto<List<BuiltinCommand>>
//   PATCH /api/v1/channels/{channelId}/builtins/{builtinKey}   → StatusResponseDto<Unit> (toggle enabled)
//   GET   /api/v1/channels/{channelId}/builtins/replies        → StatusResponseDto<List<BuiltinReplyGroup>>
//         (commands-pipelines.md §11: every reply slot of every built-in, as this channel sees it now)
//   PUT   /api/v1/channels/{channelId}/builtins/{key}/replies/{slot} → StatusResponseDto<BuiltinReply> (the
//         channel's own text for exactly one slot)
//   DELETE /api/v1/channels/{channelId}/builtins/{key}/replies/{slot} → StatusResponseDto<BuiltinReply> (back to
//         the default)
//   PUT   /api/v1/channels/{channelId}/builtins/{key}/tts      → StatusResponseDto<Unit> (S-OBS-12: toggle
//         the per-channel "speak with TTS" option for a built-in that supports it, e.g. !quote)
//   GET   /api/v1/channels/{channelId}/builtins/{key}          → StatusResponseDto<BuiltinCommand> (one built-in)
//   PUT   /api/v1/channels/{channelId}/builtins/{key}/settings → StatusResponseDto<BuiltinCommand> (the channel's
//         cooldown + permission floor; null = the default)
//   DELETE /api/v1/channels/{channelId}/builtins/{key}/settings → StatusResponseDto<BuiltinCommand> (everything
//         back to default: enabled, TTS off, cooldown, permission, every reply of its reply group)
interface BuiltinsApi {
    /** Lists all platform-defined built-in commands for the channel, with their enabled state. */
    suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>>

    /** One built-in as the channel sees it — defaults plus the channel's own settings. */
    suspend fun get(channelId: String, builtinKey: String): ApiResult<BuiltinCommand>

    /**
     * Sets the channel's cooldown (seconds) and permission floor (rung name) for a built-in; null = use the
     * default. Returns the built-in as it now resolves.
     */
    suspend fun updateSettings(
        channelId: String,
        builtinKey: String,
        cooldownSeconds: Int?,
        minPermissionLevel: String?,
    ): ApiResult<BuiltinCommand>

    /** Puts a built-in back on every default for the channel; returns it as it now resolves. */
    suspend fun reset(channelId: String, builtinKey: String): ApiResult<BuiltinCommand>

    /** Enable or disable a single builtin by its [builtinKey] (e.g. "sr", "skip"). */
    suspend fun setEnabled(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit>

    /** Every reply slot of every built-in, resolved for the channel's current personality. */
    suspend fun replies(channelId: String): ApiResult<List<BuiltinReplyGroup>>

    /** Sets the channel's own text for one reply slot; returns the slot as it now resolves. */
    suspend fun setReply(channelId: String, builtinKey: String, slot: String, template: String): ApiResult<BuiltinReply>

    /** Removes the channel's own text for one reply slot; returns the slot back on its default. */
    suspend fun resetReply(channelId: String, builtinKey: String, slot: String): ApiResult<BuiltinReply>

    /**
     * Enable or disable the channel's "speak with TTS" option for a built-in that supports it (S-OBS-12,
     * e.g. "quote"). Off by default — the built-in posts to chat only until this is turned on.
     */
    suspend fun setSpeakWithTts(channelId: String, builtinKey: String, enabled: Boolean): ApiResult<Unit>
}

class RestBuiltinsApi(private val client: ApiClient) : BuiltinsApi {
    // The list is a StatusResponseDto (envelope with `data: [...]`), not a PaginatedResponse, so it is read
    // with getEnvelope which unwraps the `data` field.
    override suspend fun list(channelId: String): ApiResult<List<BuiltinCommand>> =
        client.getEnvelope("api/v1/channels/$channelId/builtins")

    override suspend fun get(channelId: String, builtinKey: String): ApiResult<BuiltinCommand> =
        client.getEnvelope("api/v1/channels/$channelId/builtins/$builtinKey")

    override suspend fun updateSettings(
        channelId: String,
        builtinKey: String,
        cooldownSeconds: Int?,
        minPermissionLevel: String?,
    ): ApiResult<BuiltinCommand> =
        client.putEnvelope(
            "api/v1/channels/$channelId/builtins/$builtinKey/settings",
            UpdateBuiltinSettingsBody(cooldownSeconds, minPermissionLevel),
        )

    override suspend fun reset(channelId: String, builtinKey: String): ApiResult<BuiltinCommand> =
        client.deleteEnvelope("api/v1/channels/$channelId/builtins/$builtinKey/settings")

    override suspend fun setEnabled(
        channelId: String,
        builtinKey: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.patchUnit(
            "api/v1/channels/$channelId/builtins/$builtinKey",
            SetBuiltinEnabledBody(enabled),
        )

    override suspend fun replies(channelId: String): ApiResult<List<BuiltinReplyGroup>> =
        client.getEnvelope("api/v1/channels/$channelId/builtins/replies")

    override suspend fun setReply(
        channelId: String,
        builtinKey: String,
        slot: String,
        template: String,
    ): ApiResult<BuiltinReply> =
        client.putEnvelope(
            "api/v1/channels/$channelId/builtins/$builtinKey/replies/$slot",
            SetBuiltinReplyBody(template),
        )

    override suspend fun resetReply(channelId: String, builtinKey: String, slot: String): ApiResult<BuiltinReply> =
        client.deleteEnvelope("api/v1/channels/$channelId/builtins/$builtinKey/replies/$slot")

    override suspend fun setSpeakWithTts(
        channelId: String,
        builtinKey: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/builtins/$builtinKey/tts",
            SetBuiltinSpeakWithTtsBody(enabled),
        )
}

/**
 * A platform-defined built-in command (backend `BuiltinCommandDto`): what it is ([builtinKey] / [name]),
 * whether it is enabled for this channel ([isEnabled]), its defaults, the reply group it speaks with
 * ([replyGroup] — e.g. `unlurk` speaks with the `lurk` replies), and its "speak with TTS" toggle
 * ([speakWithTts], S-OBS-12, default off). [cooldownSecondsOverride] / [minPermissionLevelOverride] are the
 * channel's own settings (null = the default applies); [isReserved] data-rights commands are locked;
 * [replyOverrideCount] is how many replies the channel has reworded.
 */
@Serializable
data class BuiltinCommand(
    val builtinKey: String = "",
    val name: String = "",
    val isEnabled: Boolean = true,
    val defaultCooldownSeconds: Int = 0,
    val defaultMinPermissionLevel: String = "Everyone",
    val replyGroup: String = "",
    val speakWithTts: Boolean = false,
    val isReserved: Boolean = false,
    val cooldownSecondsOverride: Int? = null,
    val minPermissionLevelOverride: String? = null,
    val replyOverrideCount: Int = 0,
) {
    /** The cooldown chat actually gets — the channel's own, else the default. */
    val cooldownSeconds: Int get() = cooldownSecondsOverride ?: defaultCooldownSeconds

    /** Who can use it right now — the channel's own floor, else the default. */
    val minPermissionLevel: String get() = minPermissionLevelOverride ?: defaultMinPermissionLevel

    /** True when anything differs from the defaults — what a reset would undo. */
    val isCustomized: Boolean
        get() = !isEnabled || speakWithTts || cooldownSecondsOverride != null ||
            minPermissionLevelOverride != null || replyOverrideCount > 0
}

/**
 * One reply group of the built-in reply catalogue (backend `BuiltinReplyGroupDto`): the replies of one built-in,
 * or of the bot itself when [commandKeys] is empty (the `system` and `botstatus` groups).
 */
@Serializable
data class BuiltinReplyGroup(
    val builtinKey: String = "",
    val commandKeys: List<String> = emptyList(),
    val replies: List<BuiltinReply> = emptyList(),
)

/**
 * One reply slot as this channel sees it (backend `BuiltinReplyDto`). [effectiveTemplate] is what the bot sends;
 * [source] names where it comes from (`channel`, `platform`, `tone`); [defaultTemplate] is what it sends after a
 * reset. [isLocked] replies (data-rights wording) are read-only.
 */
@Serializable
data class BuiltinReply(
    val builtinKey: String = "",
    val slot: String = "",
    val label: LocalizedTextDto = LocalizedTextDto(),
    val description: LocalizedTextDto = LocalizedTextDto(),
    val effectiveTemplate: String = "",
    val source: String = "",
    val defaultTemplate: String = "",
    val toneVariations: List<String> = emptyList(),
    val variables: List<BuiltinReplyVariable> = emptyList(),
    val isOverridden: Boolean = false,
    val isLocked: Boolean = false,
)

/** A value a reply can use, with the example the preview fills in (backend `BuiltinReplyVariableDto`). */
@Serializable
data class BuiltinReplyVariable(
    val name: String = "",
    val description: LocalizedTextDto = LocalizedTextDto(),
    val sampleValue: String = "",
)

/** Toggle request body (backend `SetBuiltinEnabledRequest`). */
@Serializable
private data class SetBuiltinEnabledBody(val enabled: Boolean)

/** Reply-text request body (backend `SetBuiltinReplyRequest`). */
@Serializable
private data class SetBuiltinReplyBody(val template: String?)

/** Speak-with-TTS request body (backend `SetBuiltinSpeakWithTtsRequest`). */
@Serializable
private data class SetBuiltinSpeakWithTtsBody(val enabled: Boolean)

/** Cooldown + permission request body (backend `UpdateBuiltinSettingsRequest`); null = use the default. */
@Serializable
private data class UpdateBuiltinSettingsBody(val cooldownSeconds: Int?, val minPermissionLevel: String?)
