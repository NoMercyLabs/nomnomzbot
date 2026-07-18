// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.automation.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AutomationApi
import bot.nomnomz.dashboard.core.network.AutomationToken
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateAutomationTokenBody
import bot.nomnomz.dashboard.core.network.IssuedAutomationToken
import bot.nomnomz.dashboard.core.network.MintPairingCodeBody
import bot.nomnomz.dashboard.core.network.PairingCode
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Automation API-tokens page state-holder (automation-api.md §5 + stream-deck.md): the channel's external
// API tokens (issue / rotate / revoke) and one-time device pairing codes. It resolves the active channel, reads
// the token list (a revoked token stays listed as a tombstone), and reads the channel's pipelines to populate
// the create dialog's optional "restrict to pipelines" picker.
//
// The plaintext secret is shown EXACTLY ONCE: create + rotate return it in their response and the screen surfaces
// it in a copy-once dialog. This controller returns the issued token straight to the caller for that dialog and
// re-lists so the row then shows only its prefix.
class AutomationController(
    private val channelsApi: ChannelsApi,
    private val automationApi: AutomationApi,
    private val pipelinesApi: PipelinesApi,
) {
    private val _state: MutableStateFlow<AutomationUiState> = MutableStateFlow(AutomationUiState.Loading)

    /** The page render state: loading / ready (tokens + pipelines) / error. */
    val state: StateFlow<AutomationUiState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then read its tokens and pipelines. */
    suspend fun load() {
        if (_state.value !is AutomationUiState.Ready) _state.value = AutomationUiState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = AutomationUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id
        refresh()
    }

    /** Re-read the tokens (fatal on failure) + pipelines (best-effort), preserving any transient action error. */
    suspend fun refresh() {
        val id: String = channelId ?: return

        val tokens: List<AutomationToken> =
            when (val result: ApiResult<List<AutomationToken>> = automationApi.tokens(id)) {
                is ApiResult.Failure -> {
                    _state.value = AutomationUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The pipeline picker is a convenience — a failure just leaves it empty, never a page error.
        val pipelines: List<PipelineSummary> =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        val previous: AutomationUiState.Ready? = _state.value as? AutomationUiState.Ready
        _state.value =
            AutomationUiState.Ready(tokens = tokens, pipelines = pipelines, actionError = previous?.actionError)
    }

    /**
     * Issue a new token. Returns the [IssuedAutomationToken] (the secret shown once) on success, then re-lists so
     * the row shows only its prefix; null on failure (the error is surfaced on the Ready state).
     */
    suspend fun createToken(
        name: String,
        scopes: List<String>,
        allowedPipelineIds: List<String>,
        expiresAt: String?,
    ): IssuedAutomationToken? {
        val id: String = channelId ?: run { failWrite(NoChannelError); return null }
        return when (
            val result: ApiResult<IssuedAutomationToken> =
                automationApi.createToken(
                    id,
                    CreateAutomationTokenBody(
                        name = name,
                        scopes = scopes,
                        allowedPipelineIds = allowedPipelineIds.ifEmpty { null },
                        expiresAt = expiresAt,
                    ),
                )
        ) {
            is ApiResult.Ok -> {
                refresh()
                result.value
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    /** Rotate token [tokenId]: invalidates the old secret and returns the fresh one (shown once). */
    suspend fun rotateToken(tokenId: String): IssuedAutomationToken? {
        val id: String = channelId ?: run { failWrite(NoChannelError); return null }
        return when (val result: ApiResult<IssuedAutomationToken> = automationApi.rotateToken(id, tokenId)) {
            is ApiResult.Ok -> {
                refresh()
                result.value
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    /** Revoke token [tokenId] (a tombstone — the row stays listed). Reloads on success. */
    suspend fun revokeToken(tokenId: String) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(automationApi.revokeToken(id, tokenId))
    }

    /**
     * Mint a one-time device pairing code. Returns the [PairingCode] (the 8-char code + expiry) for the
     * show-and-copy dialog; null on failure. The device redeems it itself and then appears in the token list.
     */
    suspend fun mintPairCode(deviceLabel: String, scopes: List<String>): PairingCode? {
        channelId ?: run { failWrite(NoChannelError); return null }
        return when (
            val result: ApiResult<PairingCode> =
                automationApi.mintPairCode(MintPairingCodeBody(deviceLabel = deviceLabel, scopes = scopes))
        ) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> refresh()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: AutomationUiState = _state.value
        _state.value =
            if (current is AutomationUiState.Ready) current.copy(actionError = detail)
            else AutomationUiState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Automation page render state. */
sealed interface AutomationUiState {
    data object Loading : AutomationUiState

    /**
     * The channel's tokens (active + revoked tombstones) and its pipelines (for the create dialog's optional
     * restrict-to-pipelines picker). [actionError] is non-null only when the last write failed — a transient
     * banner over the content.
     */
    data class Ready(
        val tokens: List<AutomationToken>,
        val pipelines: List<PipelineSummary>,
        val actionError: String? = null,
    ) : AutomationUiState

    data class Error(val detail: String) : AutomationUiState
}
