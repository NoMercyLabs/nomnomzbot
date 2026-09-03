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

// The typed GDPR self-service facade — the signed-in caller's own data-subject rights (privacy.md §5). These
// routes are NOT channel-scoped: they act on the caller's own subject record (Gate-1, the caller's own data),
// and the active channel still rides along in X-Channel-Id automatically. Real data only: the backend exports
// the subject's actual stored rows and lists their genuine erasure requests + consent ledger.
//
// Backend routes (GdprController):
//   GET    /api/v1/gdpr/export                     →  StatusResponseDto<DataExportDto>
//   POST   /api/v1/gdpr/erasure                    →  StatusResponseDto<ErasureRequestDto>
//   POST   /api/v1/gdpr/opt-out                    →  StatusResponseDto<ErasureRequestDto>
//   GET    /api/v1/gdpr/requests                   →  PaginatedResponse<ErasureRequestDto>
//   GET    /api/v1/gdpr/requests/{id}              →  StatusResponseDto<ErasureRequestDto>
//   GET    /api/v1/gdpr/consents                   →  StatusResponseDto<List<ConsentRecordDto>>
//   POST   /api/v1/gdpr/consents                   →  StatusResponseDto<ConsentRecordDto>
//   DELETE /api/v1/gdpr/consents/{consentType}     →  204 No Content
interface GdprApi {
    /** Export the caller's whole data-subject record as a portable document (the JSON is in [DataExport.document]). */
    suspend fun exportData(): ApiResult<DataExport>

    /**
     * Export ANOTHER subject's record on their behalf — the operator half of a right-of-access request,
     * for when the request arrives to the channel rather than from the subject's own dashboard.
     *
     * Lives on the compliance plane (`compliance:erasure`), not the Gate-1 self-service plane, because
     * it reads a stranger's personal data. Same document shape, so the caller handles it identically.
     */
    suspend fun exportSubject(subjectUserId: String, channelId: String? = null): ApiResult<DataExport>

    /**
     * The counted preview of what erasure WOULD destroy for the caller (S-CONSEQ) — real backend row counts,
     * read before the irreversible save. A failure here is a genuine unknown: the confirm surface must say so
     * and never render it as a zero blast radius.
     */
    suspend fun previewErasure(): ApiResult<ErasurePreview>

    /** Request erasure of the caller's data (default deployment scope) — an irreversible crypto-shred. */
    suspend fun requestErasure(scope: String = "deployment"): ApiResult<ErasureRequest>

    /** Opt out of analytics/marketing processing without erasing the account (a lighter erasure request). */
    suspend fun optOut(): ApiResult<ErasureRequest>

    /** The caller's erasure requests — received / processing / completed / failed. */
    suspend fun requests(): ApiResult<List<ErasureRequest>>

    /** One erasure request by [id]. */
    suspend fun request(id: String): ApiResult<ErasureRequest>

    /** The caller's consent ledger — every granted / withdrawn consent record. */
    suspend fun consents(): ApiResult<List<ConsentRecord>>

    /** Grant a consent (the caller records lawful basis for a processing purpose). */
    suspend fun grantConsent(body: GrantConsentBody): ApiResult<ConsentRecord>

    /** Withdraw the consent of [consentType] (a tombstone — the record stays listed with a withdrawn badge). */
    suspend fun withdrawConsent(consentType: String): ApiResult<Unit>
}

class RestGdprApi(private val client: ApiClient) : GdprApi {
    override suspend fun exportData(): ApiResult<DataExport> = client.getEnvelope("api/v1/gdpr/export")

    override suspend fun exportSubject(
        subjectUserId: String,
        channelId: String?,
    ): ApiResult<DataExport> =
        client.getEnvelope(
            "api/v1/compliance/export?subjectUserId=$subjectUserId" +
                if (channelId.isNullOrBlank()) "" else "&broadcasterId=$channelId"
        )

    override suspend fun previewErasure(): ApiResult<ErasurePreview> =
        client.getEnvelope("api/v1/gdpr/erasure/preview")

    override suspend fun requestErasure(scope: String): ApiResult<ErasureRequest> =
        client.postEnvelope("api/v1/gdpr/erasure", RequestErasureBody(scope = scope))

    override suspend fun optOut(): ApiResult<ErasureRequest> = client.postEnvelope("api/v1/gdpr/opt-out")

    // The list is a PaginatedResponse (a flat `{ data: [...] }`), read with getDirect rather than getEnvelope.
    override suspend fun requests(): ApiResult<List<ErasureRequest>> =
        when (
            val page: ApiResult<PaginatedEnvelope<ErasureRequest>> =
                client.getDirect("api/v1/gdpr/requests?page=1&pageSize=50")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun request(id: String): ApiResult<ErasureRequest> =
        client.getEnvelope("api/v1/gdpr/requests/$id")

    override suspend fun consents(): ApiResult<List<ConsentRecord>> = client.getEnvelope("api/v1/gdpr/consents")

    override suspend fun grantConsent(body: GrantConsentBody): ApiResult<ConsentRecord> =
        client.postEnvelope("api/v1/gdpr/consents", body)

    override suspend fun withdrawConsent(consentType: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/gdpr/consents/$consentType")
}

/**
 * A generated data export (backend `DataExportDto`): the portable [document] (the JSON payload the screen saves
 * to disk) plus its provenance — the originating [erasureRequestId], the [exportFormat] / [exportLocation], and
 * the [sizeBytes] / [rowsAffected] the backend gathered when it built the export at [generatedAt].
 */
@Serializable
data class DataExport(
    val erasureRequestId: String = "",
    val exportFormat: String = "",
    val exportLocation: String = "",
    val sizeBytes: Long = 0,
    val rowsAffected: Int = 0,
    val generatedAt: String = "",
    val document: String = "",
)

/**
 * One erasure request (backend `ErasureRequestDto`) — a data-subject right in flight or completed. [status] is
 * received / processing / completed / failed; [requestType] distinguishes an erasure from an opt-out; [scope]
 * names how far it reaches (deployment). [cryptoShredApplied] / [anonymizationApplied] record what the backend
 * actually did; [failureReason] is non-null only on a failed request.
 */
@Serializable
data class ErasureRequest(
    val id: String = "",
    val subjectUserId: String = "",
    val subjectIdHash: String = "",
    val broadcasterId: String? = null,
    val requestType: String = "",
    val requestedBy: String = "",
    val status: String = "",
    val scope: String = "",
    val cryptoShredApplied: Boolean = false,
    val anonymizationApplied: Boolean = false,
    val rowsAffected: Int = 0,
    val exportLocation: String? = null,
    val exportFormat: String? = null,
    val failureReason: String? = null,
    val requestedAt: String = "",
    val completedAt: String? = null,
)

/**
 * One consent record (backend `ConsentRecordDto`) — the caller's stance on a processing purpose. [status] is
 * granted / withdrawn; [lawfulBasis] records the GDPR basis; [consentVersion] pins the policy version consented
 * to. [withdrawnAt] non-null marks a withdrawn tombstone (still listed); [expiresAt] is an optional expiry.
 */
@Serializable
data class ConsentRecord(
    val id: String = "",
    val broadcasterId: String? = null,
    val subjectUserId: String = "",
    val consentType: String = "",
    val status: String = "",
    val lawfulBasis: String = "",
    val consentVersion: String? = null,
    val grantedAt: String = "",
    val withdrawnAt: String? = null,
    val expiresAt: String? = null,
)

/**
 * One counted category of the erasure blast radius (backend `ErasurePreviewCategoryDto`). [categoryKey] is an
 * i18n resource KEY (the backend never ships a rendered sentence); [rowCount] is a real backend COUNT. Only
 * non-zero categories are sent, so a category is never rendered as a misleading "0 rows".
 */
@Serializable
data class ErasurePreviewCategory(val categoryKey: String = "", val rowCount: Int = 0)

/**
 * The counted preview of a GDPR erasure (backend `ErasurePreviewDto`) — what an erasure WOULD destroy, read
 * BEFORE the irreversible save. An empty [categories] with a zero [totalRows] is the genuine "nothing would be
 * destroyed" answer; a FAILED fetch is a different thing entirely and must never be rendered as this.
 */
@Serializable
data class ErasurePreview(
    val subjectAlreadyAnonymized: Boolean = false,
    val totalRows: Int = 0,
    val categories: List<ErasurePreviewCategory> = emptyList(),
)

/** The erasure-request body (backend `RequestErasureRequest`). [scope] defaults to the whole deployment. */
@Serializable
data class RequestErasureBody(val scope: String = "deployment")

/**
 * The grant-consent body (backend `GrantConsentRequest`). [subjectUserId] is the caller's own id; [consentType]
 * names the processing purpose and [lawfulBasis] its GDPR basis; [consentVersion] / [source] / [proofOfConsentIp]
 * optionally record the policy version, the surface the consent came from, and the originating IP for the audit.
 */
@Serializable
data class GrantConsentBody(
    val subjectUserId: String,
    val broadcasterId: String? = null,
    val consentType: String,
    val lawfulBasis: String,
    val consentVersion: String? = null,
    val source: String? = null,
    val proofOfConsentIp: String? = null,
)
