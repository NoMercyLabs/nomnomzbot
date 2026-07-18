// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mydata.state

import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ConsentRecord
import bot.nomnomz.dashboard.core.network.DataExport
import bot.nomnomz.dashboard.core.network.ErasureRequest
import bot.nomnomz.dashboard.core.network.GdprApi
import bot.nomnomz.dashboard.core.network.GrantConsentBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The "My data" (GDPR self-service) page state-holder (privacy.md §5): the signed-in caller's own data-subject
// rights — export, erasure, opt-out, the request history, and the consent ledger. The routes are Gate-1 (the
// caller's own data, never channel-scoped), so this holder has no channel to resolve — it just reads the two
// lists and drives the four write actions, re-listing after each one.
//
// The export is handed straight to the OS via [fileBridge] (the shared journal file bridge's save dialog); on
// success a transient [MyDataUiState.Ready.notice] confirms the save, and any write failure surfaces as a
// transient [MyDataUiState.Ready.actionError] banner over the content.
class MyDataController(
    private val gdprApi: GdprApi,
    private val fileBridge: JournalFileIO,
    private val currentUserId: () -> String?,
) {
    private val _state: MutableStateFlow<MyDataUiState> = MutableStateFlow(MyDataUiState.Loading)

    /** The page render state: loading / ready (requests + consents) / error. */
    val state: StateFlow<MyDataUiState> = _state.asStateFlow()

    /** Read the erasure requests (fatal on failure) and the consent ledger (best-effort → empty on failure). */
    suspend fun load() {
        if (_state.value !is MyDataUiState.Ready) _state.value = MyDataUiState.Loading

        val requests: List<ErasureRequest> =
            when (val result: ApiResult<List<ErasureRequest>> = gdprApi.requests()) {
                is ApiResult.Failure -> {
                    _state.value = MyDataUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The consent ledger is secondary — a failure just leaves it empty, never a page error.
        val consents: List<ConsentRecord> =
            when (val result: ApiResult<List<ConsentRecord>> = gdprApi.consents()) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        val previous: MyDataUiState.Ready? = _state.value as? MyDataUiState.Ready
        _state.value =
            MyDataUiState.Ready(
                requests = requests,
                consents = consents,
                actionError = previous?.actionError,
            )
    }

    /**
     * Export the caller's data: fetch the portable document, then offer it to the OS save dialog. Returns true
     * when the user completed the save. Sets a transient notice on success or an action error on failure; a
     * cancelled save is not an error (returns false, leaves the state as-is).
     */
    suspend fun exportData(): Boolean {
        return when (val result: ApiResult<DataExport> = gdprApi.exportData()) {
            is ApiResult.Ok -> {
                val saved: Boolean =
                    fileBridge.saveFile(
                        suggestedName = "nomnomz-my-data.json",
                        bytes = result.value.document.encodeToByteArray(),
                    )
                if (saved) noticeReady(ExportSavedNotice)
                saved
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                false
            }
        }
    }

    /** Request erasure of the caller's data (default deployment scope) — reloads to show the new request. */
    suspend fun requestErasure() {
        afterWrite(gdprApi.requestErasure())
    }

    /** Opt out of analytics/marketing without erasing the account — reloads to show the new request. */
    suspend fun optOut() {
        afterWrite(gdprApi.optOut())
    }

    /** Grant a consent for [consentType] under [lawfulBasis] (the caller is the subject) — reloads on success. */
    suspend fun grantConsent(consentType: String, lawfulBasis: String) {
        val subject: String = currentUserId() ?: return failWrite(NoUserError)
        afterWrite(
            gdprApi.grantConsent(
                GrantConsentBody(
                    subjectUserId = subject,
                    consentType = consentType,
                    lawfulBasis = lawfulBasis,
                )
            )
        )
    }

    /** Withdraw the consent of [consentType] (a tombstone — the record stays listed). Reloads on success. */
    suspend fun withdrawConsent(consentType: String) {
        afterWrite(gdprApi.withdrawConsent(consentType))
    }

    // ── internals ────────────────────────────────────────────────────────────

    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun noticeReady(notice: String) {
        val current: MyDataUiState = _state.value
        if (current is MyDataUiState.Ready) _state.value = current.copy(notice = notice, actionError = null)
    }

    private fun failWrite(detail: String) {
        val current: MyDataUiState = _state.value
        _state.value =
            if (current is MyDataUiState.Ready) current.copy(actionError = detail)
            else MyDataUiState.Error(detail)
    }

    private companion object {
        const val NoUserError: String = "No signed-in user — reconnect and try again."
        const val ExportSavedNotice: String = "export_saved"
    }
}

/** The "My data" page render state. */
sealed interface MyDataUiState {
    data object Loading : MyDataUiState

    /**
     * The caller's erasure [requests] (history) and their [consents] ledger. [actionError] is non-null only when
     * the last write failed (a transient banner); [notice] is a transient success marker (e.g. an export saved).
     */
    data class Ready(
        val requests: List<ErasureRequest>,
        val consents: List<ConsentRecord>,
        val actionError: String? = null,
        val notice: String? = null,
    ) : MyDataUiState

    data class Error(val detail: String) : MyDataUiState
}
