// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.assets.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.io.AssetFile
import bot.nomnomz.dashboard.core.io.AssetFilePickerIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssetsApi
import bot.nomnomz.dashboard.core.network.ChannelAsset
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_asset_deleted
import nomnomzbot.composeapp.generated.resources.feedback_asset_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_asset_uploaded

// The Assets page's state-holder (the SoundController twin). Lists the channel's uploaded media assets
// from the backend (real data only), drives upload (create-or-replace by name) and delete; each write
// re-lists on success so the page stays in sync. [publicUrl] turns a DTO's relative serving URL into the
// absolute anonymous link OBS browser sources and widgets use.
class AssetsController(
    private val assetsApi: AssetsApi,
    private val assetPicker: AssetFilePickerIO,
    private val baseUrlProvider: () -> String? = { null },
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<AssetsState> = MutableStateFlow(AssetsState.Loading)
    val state: StateFlow<AssetsState> = _state.asStateFlow()

    private val _isUploading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isUploading: StateFlow<Boolean> = _isUploading.asStateFlow()

    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is AssetsState.Ready) _state.value = AssetsState.Loading
        when (val result: ApiResult<List<ChannelAsset>> = assetsApi.list()) {
            is ApiResult.Failure -> _state.value = AssetsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) AssetsState.Empty
                    else AssetsState.Ready(result.value)
        }
    }

    /** Opens the OS picker, then uploads the chosen file with its filename stem as slug + display name. */
    suspend fun uploadAsset() {
        _isUploading.value = true
        try {
            val file: AssetFile = assetPicker.pick() ?: return
            val name: String = file.name.substringBeforeLast('.')
            upload(fileName = file.name, bytes = file.bytes, name = name, displayName = name)
        } finally {
            _isUploading.value = false
        }
    }

    /**
     * Upload [bytes] as [fileName] under the slug [name] (same name = the backend replaces that asset in
     * place). The MIME type is inferred from the file extension — the backend validates it regardless.
     */
    suspend fun upload(fileName: String, bytes: ByteArray, name: String, displayName: String) {
        val file: AssetFile =
            AssetFile(
                name = fileName,
                mimeType = mimeTypeForFileName(fileName),
                bytes = bytes,
            )
        when (val result: ApiResult<ChannelAsset> = assetsApi.upload(name, displayName, file)) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_asset_uploaded)
                load()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    suspend fun deleteAsset(id: String) {
        when (val result: ApiResult<Unit> = assetsApi.delete(id)) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_asset_deleted)
                load()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * The absolute anonymous serving URL for an asset's [relativeUrl] (`/api/v1/assets/file/…`) — the
     * active backend base URL prefixed on, for pasting into OBS browser sources and widget configs.
     * Null when no backend connection is active.
     */
    fun publicUrl(relativeUrl: String): String? =
        baseUrlProvider()?.trimEnd('/')?.takeIf { it.isNotBlank() }?.let { origin ->
            origin + relativeUrl
        }

    private fun failWrite(detail: String) {
        feedback.error(Res.string.feedback_asset_save_failed, detail)
        val current: AssetsState = _state.value
        _state.value =
            if (current is AssetsState.Ready) current.copy(actionError = detail)
            else AssetsState.Error(detail)
    }

    private fun mimeTypeForFileName(fileName: String): String =
        when (fileName.substringAfterLast('.', missingDelimiterValue = "").lowercase()) {
            "png" -> "image/png"
            "jpg", "jpeg" -> "image/jpeg"
            "gif" -> "image/gif"
            "webp" -> "image/webp"
            "svg" -> "image/svg+xml"
            "mp3" -> "audio/mpeg"
            "ogg" -> "audio/ogg"
            "wav" -> "audio/wav"
            else -> "application/octet-stream"
        }
}

/** The Assets page render state. */
sealed interface AssetsState {
    data object Loading : AssetsState

    data class Ready(val assets: List<ChannelAsset>, val actionError: String? = null) : AssetsState

    data object Empty : AssetsState

    data class Error(val detail: String) : AssetsState
}
