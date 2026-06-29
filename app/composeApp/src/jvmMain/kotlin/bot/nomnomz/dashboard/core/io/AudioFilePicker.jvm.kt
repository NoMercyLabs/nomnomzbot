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

// Desktop audio file picker — a native AWT Open dialog filtered to audio file extensions.
// The dialog and disk read run on Dispatchers.IO (never the Compose UI thread). MIME type is
// inferred from the file extension, since Java's File API does not expose the browser's File.type.
actual class AudioFilePicker : AudioFilePickerIO {
    actual override suspend fun pick(): AudioFile? =
        withContext(Dispatchers.IO) {
            val dialog: FileDialog = FileDialog(null as Frame?, "Select audio clip", FileDialog.LOAD)
            dialog.setFilenameFilter { _, name ->
                AUDIO_EXTENSIONS.any { ext -> name.endsWith(ext, ignoreCase = true) }
            }
            dialog.isVisible = true

            val directory: String? = dialog.directory
            val file: String? = dialog.file
            if (directory == null || file == null) {
                null
            } else {
                try {
                    val handle: File = File(directory, file)
                    AudioFile(
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
        val AUDIO_EXTENSIONS: List<String> =
            listOf(".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".opus")

        fun mimeTypeForExtension(ext: String): String =
            when (ext) {
                "mp3" -> "audio/mpeg"
                "wav" -> "audio/wav"
                "ogg" -> "audio/ogg"
                "flac" -> "audio/flac"
                "aac" -> "audio/aac"
                "m4a" -> "audio/mp4"
                "opus" -> "audio/ogg; codecs=opus"
                else -> "audio/octet-stream"
            }
    }
}
