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

// Contract for opening a native OS file picker filtered to the asset-library media types (images + audio)
// and returning the selected file's bytes with its name and detected MIME type. The controller depends on
// this interface; per-platform implementations live in wasmJs and jvm source sets (the AudioFilePicker twin).
interface AssetFilePickerIO {
    /** Prompts the user to pick an image or audio file. Returns the picked file, or null if cancelled. */
    suspend fun pick(): AssetFile?
}

// The per-target [AssetFilePickerIO] implementation:
//
//   Desktop (jvm):  a native Open dialog filtered to image + audio extensions; MIME from extension.
//   Web (wasmJs):   a hidden <input type="file" accept="image/*,audio/*"> staged via a global slot and
//                   polled by the Kotlin side (same handshake as AudioFilePicker / JournalFileBridge).
expect class AssetFilePicker() : AssetFilePickerIO {
    override suspend fun pick(): AssetFile?
}

/** A media file selected by the user: its display [name], detected [mimeType], and raw [bytes]. */
data class AssetFile(val name: String, val mimeType: String, val bytes: ByteArray) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is AssetFile) return false
        return name == other.name && mimeType == other.mimeType && bytes.contentEquals(other.bytes)
    }

    override fun hashCode(): Int {
        var result: Int = name.hashCode()
        result = 31 * result + mimeType.hashCode()
        result = 31 * result + bytes.contentHashCode()
        return result
    }
}
