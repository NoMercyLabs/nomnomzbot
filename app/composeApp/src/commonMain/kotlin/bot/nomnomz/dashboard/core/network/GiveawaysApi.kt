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

// The typed giveaways facade — the channel's giveaway campaigns the Giveaways page renders and drives through
// their open → collect entries → draw → fulfill lifecycle, plus the secret-safe code pools a code-prize giveaway
// draws from. Real data only: the backend lists the channel's stored campaigns, winners, and pools (no fabricated
// rows). The state holder depends on this interface and fakes it in tests without HTTP.
//
// Like the quotes / pick-lists routes, the giveaways controllers resolve the tenant (channel) from the request, so
// the routes carry no `{channelId}` — every call is "my own channel". A giveaway / winner / pool is addressed by
// its opaque [id] (a 26-character ULID, also accepted as a raw GUID string), treated as a string end-to-end and
// never parsed.
//
// Two Gate-2 floors back these routes (giveaways.md §6): the campaign + lifecycle surface floors at Moderator
// (`giveaways:read` / `giveaways:write`); the code-pool routes AND the winner code reveal are Broadcaster-only
// (`giveaways:codes:write`) — pools hold valuable secrets, so even their reads are held to the top and never echo a
// code back. The one plaintext path is [revealCode], the failed-whisper fallback.
//
// Backend routes (GiveawaysController + GiveawayCodePoolsController):
//   GET    /api/v1/giveaways?status=                     →  PaginatedResponse<GiveawayDto>
//   POST   /api/v1/giveaways                             →  StatusResponseDto<GiveawayDto>
//   PUT    /api/v1/giveaways/{id}                        →  StatusResponseDto<GiveawayDto>
//   DELETE /api/v1/giveaways/{id}                        →  StatusResponseDto<object>
//   POST   /api/v1/giveaways/{id}/open|close             →  StatusResponseDto<GiveawayDto>
//   POST   /api/v1/giveaways/{id}/draw                   →  StatusResponseDto<IReadOnlyList<GiveawayWinnerDto>>
//   POST   /api/v1/giveaways/{id}/winners/{winnerId}/redraw → StatusResponseDto<GiveawayWinnerDto>
//   GET    /api/v1/giveaways/{id}/winners                →  PaginatedResponse<GiveawayWinnerDto>
//   GET    /api/v1/giveaways/{id}/winners/{winnerId}/code →  StatusResponseDto<string>   (broadcaster reveal)
//   GET    /api/v1/giveaways/code-pools                  →  PaginatedResponse<CodePoolDto>   (masked)
//   POST   /api/v1/giveaways/code-pools                  →  StatusResponseDto<CodePoolDto>
//   GET    /api/v1/giveaways/code-pools/{poolId}         →  StatusResponseDto<CodePoolDetailDto>   (masked)
//   DELETE /api/v1/giveaways/code-pools/{poolId}         →  StatusResponseDto<object>
//   POST   /api/v1/giveaways/code-pools/{poolId}/codes   →  StatusResponseDto<CodePoolDto>
interface GiveawaysApi {
    /** The channel's giveaways (non-archived by default; [status] filters to one lifecycle state). */
    suspend fun list(status: String? = null): ApiResult<List<Giveaway>>

    /** Create a giveaway (draft) on the channel (backend POST). */
    suspend fun create(body: UpsertGiveawayBody): ApiResult<Unit>

    /** Update a draft/closed giveaway's configuration, addressed by its [id] (backend PUT). */
    suspend fun update(id: String, body: UpsertGiveawayBody): ApiResult<Unit>

    /** Soft-delete a giveaway, addressed by its [id] (backend DELETE). */
    suspend fun delete(id: String): ApiResult<Unit>

    /** Open a giveaway for entries — one active giveaway per channel (backend POST). */
    suspend fun open(id: String): ApiResult<Unit>

    /** Stop accepting entries; the giveaway stays drawable (backend POST). */
    suspend fun close(id: String): ApiResult<Unit>

    /** Draw the winners (weighted CSPRNG) and fulfill per the prize mode; returns the drawn winners. */
    suspend fun draw(id: String): ApiResult<List<GiveawayWinner>>

    /** Replace one winner (forfeit / no-show) with a fresh draw, addressed by its [winnerId] (backend POST). */
    suspend fun redraw(id: String, winnerId: String): ApiResult<Unit>

    /** The giveaway's append-only winner history. */
    suspend fun winners(id: String): ApiResult<List<GiveawayWinner>>

    /**
     * Reveal a winner's assigned code — the failed-whisper fallback, Broadcaster-gated (`giveaways:codes:write`).
     * Returns the PLAINTEXT code (the single path that ever decrypts one); the screen shows it once, on demand.
     */
    suspend fun revealCode(id: String, winnerId: String): ApiResult<String>

    /** The channel's code pools — counts only, never code contents (masked by design). */
    suspend fun listCodePools(): ApiResult<List<CodePool>>

    /** Create a code pool (backend POST). */
    suspend fun createCodePool(body: CreateCodePoolBody): ApiResult<Unit>

    /** One code pool's detail — the codes come back MASKED (label + status), never plaintext. */
    suspend fun codePool(poolId: String): ApiResult<CodePoolDetail>

    /** Soft-delete a code pool (backend DELETE; blocked while it backs an active giveaway). */
    suspend fun deleteCodePool(poolId: String): ApiResult<Unit>

    /** Bulk-add codes to a pool — AEAD-encrypted on write, never echoed back (backend POST). */
    suspend fun addCodes(poolId: String, body: AddCodesBody): ApiResult<Unit>
}

class RestGiveawaysApi(private val client: ApiClient) : GiveawaysApi {
    override suspend fun list(status: String?): ApiResult<List<Giveaway>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read with
        // getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as the quotes
        // and pick-lists lists. `take` (not `pageSize`) is the backend's page-size query param (PageRequestDto).
        val filter: String = if (status != null) "&status=$status" else ""
        return when (
            val page: ApiResult<PaginatedEnvelope<Giveaway>> =
                client.getDirect("api/v1/giveaways?page=1&take=25$filter")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // Every write response is a `StatusResponseDto<GiveawayDto>`, but the controller re-lists after each write, so
    // the body is irrelevant here — any 2xx is success (the reload observes the real consequence).
    override suspend fun create(body: UpsertGiveawayBody): ApiResult<Unit> =
        client.postUnit("api/v1/giveaways", body)

    override suspend fun update(id: String, body: UpsertGiveawayBody): ApiResult<Unit> =
        client.putUnit("api/v1/giveaways/$id", body)

    override suspend fun delete(id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/giveaways/$id")

    override suspend fun open(id: String): ApiResult<Unit> =
        client.postUnit("api/v1/giveaways/$id/open")

    override suspend fun close(id: String): ApiResult<Unit> =
        client.postUnit("api/v1/giveaways/$id/close")

    // Draw returns the fresh winner list in its `data` array, so it IS read (postEnvelope unwraps the payload) —
    // the page shows the winners the moment the draw lands, without a second round-trip.
    override suspend fun draw(id: String): ApiResult<List<GiveawayWinner>> =
        client.postEnvelope("api/v1/giveaways/$id/draw")

    override suspend fun redraw(id: String, winnerId: String): ApiResult<Unit> =
        client.postUnit("api/v1/giveaways/$id/winners/$winnerId/redraw")

    override suspend fun winners(id: String): ApiResult<List<GiveawayWinner>> {
        // Winner history is a PaginatedResponse; `take=100` fetches the full history for display (winnerCount plus
        // any redraws), no pagination UI needed.
        return when (
            val page: ApiResult<PaginatedEnvelope<GiveawayWinner>> =
                client.getDirect("api/v1/giveaways/$id/winners?page=1&take=100")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // The reveal is a `StatusResponseDto<string>` whose `data` is the plaintext code, so it is read with
    // getEnvelope (unwraps `data`) — the one place a code is ever decrypted for the operator.
    override suspend fun revealCode(id: String, winnerId: String): ApiResult<String> =
        client.getEnvelope("api/v1/giveaways/$id/winners/$winnerId/code")

    override suspend fun listCodePools(): ApiResult<List<CodePool>> {
        return when (
            val page: ApiResult<PaginatedEnvelope<CodePool>> =
                client.getDirect("api/v1/giveaways/code-pools?page=1&take=50")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    override suspend fun createCodePool(body: CreateCodePoolBody): ApiResult<Unit> =
        client.postUnit("api/v1/giveaways/code-pools", body)

    // A single pool's detail is a `StatusResponseDto<CodePoolDetailDto>` (a `data: T` envelope), read with
    // getEnvelope. The codes it carries are already masked by the backend — a read never sees plaintext.
    override suspend fun codePool(poolId: String): ApiResult<CodePoolDetail> =
        client.getEnvelope("api/v1/giveaways/code-pools/$poolId")

    override suspend fun deleteCodePool(poolId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/giveaways/code-pools/$poolId")

    override suspend fun addCodes(poolId: String, body: AddCodesBody): ApiResult<Unit> =
        client.postUnit("api/v1/giveaways/code-pools/$poolId/codes", body)
}

/**
 * The create/update request body (backend `UpsertGiveawayRequest`) — the FULL configurable surface. camelCase
 * JSON. [title] and [entryMode] are required; the rest carry the backend's defaults. `explicitNulls = false` on
 * the shared Json omits the null optionals ([keyword], [entryCost], the prize references, …) from the wire body,
 * and `encodeDefaults = true` sends the defaulted scalars so a create matches the backend's own defaults.
 *
 * On an EDIT the controller seeds EVERY field from the existing giveaway — including the ones the form does not
 * surface ([eligibilityJson] / [weightingJson] / [prizePipelineId]) — so they round-trip unchanged instead of
 * being wiped to null. The form edits the exposed subset; the rest pass through from the seed.
 */
@Serializable
data class UpsertGiveawayBody(
    val title: String,
    val entryMode: String,
    val keyword: String? = null,
    val entryCost: Long? = null,
    val maxEntriesPerUser: Int = 1,
    val eligibilityJson: String? = null,
    val weightingJson: String? = null,
    val winnerCount: Int = 1,
    val excludeModerators: Boolean = false,
    val claimWindowMinutes: Int? = null,
    val prizeMode: String = GiveawayPrizeMode.Announce,
    val prizeCurrencyAmount: Long? = null,
    val prizeFromPot: Boolean = false,
    val prizePipelineId: String? = null,
    val prizeCodePoolId: String? = null,
)

/**
 * A giveaway campaign (backend `GiveawayDto`): its config + lifecycle + live [entryCount]. The opaque [id]
 * addresses it; [status] is one of [GiveawayStatus]; [entryMode] one of [GiveawayEntryMode]; [prizeMode] one of
 * [GiveawayPrizeMode]. [entryCost] / [prizeCurrencyAmount] are loyalty-point amounts (nullable, `long`). Dates
 * are the backend's ISO-8601 strings, left as text (the page shows them verbatim / relative).
 */
@Serializable
data class Giveaway(
    val id: String = "",
    val title: String = "",
    val entryMode: String = "",
    val keyword: String? = null,
    val entryCost: Long? = null,
    val maxEntriesPerUser: Int = 1,
    val eligibilityJson: String? = null,
    val weightingJson: String? = null,
    val winnerCount: Int = 1,
    val excludeModerators: Boolean = false,
    val claimWindowMinutes: Int? = null,
    val prizeMode: String = GiveawayPrizeMode.Announce,
    val prizeCurrencyAmount: Long? = null,
    val prizeFromPot: Boolean = false,
    val prizePipelineId: String? = null,
    val prizeCodePoolId: String? = null,
    val status: String = GiveawayStatus.Draft,
    val openedAt: String? = null,
    val closesAt: String? = null,
    val drawnAt: String? = null,
    val entryCount: Int = 0,
    val createdAt: String = "",
)

/**
 * One drawn winner with its fulfillment trail (backend `GiveawayWinnerDto`). [status] is one of
 * [GiveawayWinnerStatus]; [isRedraw] marks a replacement for a forfeited/redrawn winner. For a code-prize
 * giveaway, [assignedCodeId] is set once a code is assigned and [whisperDelivered] tells whether the whisper
 * landed — `false` is the "needs manual reveal" state the broadcaster resolves via [GiveawaysApi.revealCode];
 * `null` for non-code prize modes.
 */
@Serializable
data class GiveawayWinner(
    val id: String = "",
    val giveawayId: String = "",
    val viewerUserId: String = "",
    val viewerDisplayName: String = "",
    val drawnAt: String = "",
    val status: String = "",
    val isRedraw: Boolean = false,
    val assignedCodeId: String? = null,
    val whisperDelivered: Boolean? = null,
)

/**
 * A code pool summary (backend `CodePoolDto`) — counts only, NEVER code contents (masked by design). [total] is
 * every code in the pool; [available] the unassigned ones a draw can still pull; [assigned] the ones already
 * handed to a winner.
 */
@Serializable
data class CodePool(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val total: Int = 0,
    val available: Int = 0,
    val assigned: Int = 0,
)

/**
 * A code pool's detail (backend `CodePoolDetailDto`): the pool plus its [codes], each a [MaskedCode] — the read
 * shows a label/tail and a status, never the plaintext.
 */
@Serializable
data class CodePoolDetail(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val codes: List<MaskedCode> = emptyList(),
)

/**
 * One masked code row (backend `MaskedCodeDto`): the opaque [id], its optional [label] / last-4 tail, its
 * [status] (one of [GiveawayCodeStatus]), and when it was [assignedAt] (null while still available). A read never
 * carries the code itself.
 */
@Serializable
data class MaskedCode(
    val id: String = "",
    val label: String? = null,
    val status: String = "",
    val assignedAt: String? = null,
)

/** The create-code-pool request body (backend `CreateCodePoolRequest`). */
@Serializable
data class CreateCodePoolBody(
    val name: String,
    val description: String? = null,
)

/** The bulk add-codes request body (backend `AddCodesRequest`) — each plaintext is AEAD-encrypted on write. */
@Serializable
data class AddCodesBody(
    val codes: List<CodeInput>,
)

/** One code to add (backend `CodeInput`): the plaintext [code] and an optional [label] to identify it later. */
@Serializable
data class CodeInput(
    val code: String,
    val label: String? = null,
)

/**
 * The giveaway [Giveaway.status] lifecycle values (backend `GiveawayStatus`). Kept beside the DTO as the single
 * source the page compares against — the wire carries these lowercase strings, never a numeric enum.
 */
object GiveawayStatus {
    const val Draft: String = "draft"
    const val Open: String = "open"
    const val Closed: String = "closed"
    const val Drawn: String = "drawn"
    const val Archived: String = "archived"
}

/** The giveaway [Giveaway.entryMode] values (backend `GiveawayEntryMode`). */
object GiveawayEntryMode {
    const val Keyword: String = "keyword"
    const val ActiveViewers: String = "active_viewers"
}

/** The giveaway [Giveaway.prizeMode] values (backend `GiveawayPrizeMode`). */
object GiveawayPrizeMode {
    const val Announce: String = "announce"
    const val Currency: String = "currency"
    const val Pipeline: String = "pipeline"
    const val CodePool: String = "code_pool"
}

/** The [GiveawayWinner.status] values (backend `GiveawayWinnerStatus`). */
object GiveawayWinnerStatus {
    const val Drawn: String = "drawn"
    const val Claimed: String = "claimed"
    const val Forfeited: String = "forfeited"
    const val Redrawn: String = "redrawn"
}

/** The [MaskedCode.status] values (backend `GiveawayCodeStatus`). */
object GiveawayCodeStatus {
    const val Available: String = "available"
    const val Assigned: String = "assigned"
    const val Delivered: String = "delivered"
    const val Revoked: String = "revoked"
}
