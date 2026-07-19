// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

import java.awt.FileDialog
import java.awt.Frame
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Desktop asset file picker — a native AWT Open dialog filtered to the asset-library media extensions
// (images + audio, matching the backend's allowed MIME types). The dialog and disk read run on
// Dispatchers.IO (never the Compose UI thread). MIME type is inferred from the file extension, since
// Java's File API does not expose the browser's File.type.
actual class AssetFilePicker : AssetFilePickerIO {
    actual override suspend fun pick(): AssetFile? =
        withContext(Dispatchers.IO) {
            val dialog: FileDialog = FileDialog(null as Frame?, "Select asset", FileDialog.LOAD)
            dialog.setFilenameFilter { _, name ->
                ASSET_EXTENSIONS.any { ext -> name.endsWith(ext, ignoreCase = true) }
            }
            dialog.isVisible = true

            val directory: String? = dialog.directory
            val file: String? = dialog.file
            if (directory == null || file == null) {
                null
            } else {
                try {
                    val handle: File = File(directory, file)
                    AssetFile(
                        name = handle.name,
                        mimeType = mimeTypeForExtension(handle.extension.lowercase()),
                        bytes = handle.readBytes(),
                    )
                } catch (_: Throwable) {
                    null
                }
            }
        }

    private companion object {
        val ASSET_EXTENSIONS: List<String> =
            listOf(".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".mp3", ".ogg", ".wav")

        fun mimeTypeForExtension(ext: String): String =
            when (ext) {
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
}
